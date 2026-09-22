# Instruções de build — Optimizer PC 1.0

Como compilar, testar, publicar e empacotar o Optimizer PC do zero.

---

## Índice

1. [O que você precisa](#1-o-que-você-precisa)
2. [Caminho rápido](#2-caminho-rápido)
3. [Passo a passo manual](#3-passo-a-passo-manual)
4. [Instalador](#4-instalador)
5. [Verificação dos entregáveis](#5-verificação-dos-entregáveis)
6. [Solução de problemas](#6-solução-de-problemas)
7. [Regenerar ícone e logotipos](#7-regenerar-ícone-e-logotipos)
8. [Checklist antes de publicar](#8-checklist-antes-de-publicar)

---

## 1. O que você precisa

| Ferramenta | Versão | Obrigatória? |
| --- | --- | --- |
| .NET SDK | 8.0 (LTS) | Sim |
| Inno Setup | 6.x | Só para gerar o instalador |
| Windows | 10 ou 11, 64 bits | Sim |

O SDK está em <https://dotnet.microsoft.com/download/dotnet/8.0>. Para conferir se já
está instalado:

```powershell
dotnet --version
```

O resultado deve começar com `8.`.

> Se o `dotnet` não estiver no `PATH`, ele costuma estar em `%USERPROFILE%\.dotnet\dotnet.exe`
> (instalação por usuário). Nesse caso, chame pelo caminho completo.

**Inno Setup 6** (opcional) está em <https://jrsoftware.org/isdl.php>. Sem ele o
aplicativo e o pacote portátil ainda são gerados; apenas o instalador é pulado.

## 2. Caminho rápido

Na raiz do repositório:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
```

Isso faz, em ordem:

1. Compila a solução e roda os 275 testes.
2. Publica o aplicativo (`self-contained`, `win-x64`) em `dist\publish`.
3. Compila o instalador `dist\OptimizerPC-Setup.exe`.
4. Monta o pacote `dist\OptimizerPC-Portable.zip`.

No final, imprime os três entregáveis com tamanho e os primeiros dígitos do SHA-256.

Parâmetros úteis:

| Parâmetro | Efeito |
| --- | --- |
| `-SkipTests` | Pula os testes (compila e publica direto) |
| `-Configuration Debug` | Gera em Debug em vez de Release |
| `-Iscc "D:\Inno Setup 6\ISCC.exe"` | Aponta para outra instalação do Inno Setup |

Exemplos:

```powershell
# Publicar sem rodar os testes
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1 -SkipTests

# Usando outro caminho do Inno Setup
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1 -Iscc "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

## 3. Passo a passo manual

Se preferir executar etapa por etapa, ou se o script não servir ao seu ambiente:

### 3.1 Compilar

```powershell
dotnet build OptimizerPC.sln -c Release
```

### 3.2 Rodar os testes

```powershell
dotnet test tests\OptimizerPC.Tests\OptimizerPC.Tests.csproj -c Release
```

Esperado: **275 testes aprovados, 0 falhas**.

Para rodar só um grupo:

```powershell
dotnet test tests\OptimizerPC.Tests\OptimizerPC.Tests.csproj --filter "FullyQualifiedName~Security"
dotnet test tests\OptimizerPC.Tests\OptimizerPC.Tests.csproj --filter "FullyQualifiedName~Cleaning"
dotnet test tests\OptimizerPC.Tests\OptimizerPC.Tests.csproj --filter "FullyQualifiedName~Localization"
```

### 3.3 Publicar o aplicativo

```powershell
dotnet publish src\OptimizerPC.App\OptimizerPC.App.csproj -c Release -r win-x64 --self-contained true -o dist\publish
```

`--self-contained true` é o que dispensa o .NET na máquina do usuário. A pasta
`dist\publish` fica com cerca de 63 MB e deve ser distribuída **inteira**.

Para testar a publicação antes de empacotar:

```powershell
.\dist\publish\OptimizerPC.exe
```

### 3.4 Montar a versão portátil manualmente

```powershell
Copy-Item dist\publish dist\OptimizerPC-Portable -Recurse
Copy-Item installer\portable-LEIA-ME.txt dist\OptimizerPC-Portable\LEIA-ME.txt
Compress-Archive -Path dist\OptimizerPC-Portable -DestinationPath dist\OptimizerPC-Portable.zip -CompressionLevel Optimal
Remove-Item dist\OptimizerPC-Portable -Recurse
```

## 4. Instalador

### 4.1 Compilar

Com o Inno Setup 6 instalado:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\OptimizerPC.iss
```

O script `installer/OptimizerPC.iss` lê a pasta `dist\publish` e grava
`dist\OptimizerPC-Setup.exe`. **Publique primeiro** (passo 3.3): o `.iss` não compila o
aplicativo, apenas empacota o que já está em `dist\publish`.

Ajuste a constante `SourceDir` no topo do `.iss` se a pasta de publicação estiver em
outro lugar.

### 4.2 Testar o instalador

1. Execute `dist\OptimizerPC-Setup.exe`.
2. Instale, abra pelo Menu Iniciar e confirme que a janela aparece.
3. Verifique no Menu Iniciar e em **Aplicativos instalados** que o programa consta como
   instalado.
4. Desinstale e confirme que o programa saiu — e que `%LOCALAPPDATA%\OptimizerPC`
   permaneceu (os dados do usuário não são removidos na desinstalação).

### 4.3 Se o ISCC recusar as próprias DLLs

O Inno Setup confere a integridade dos seus próprios arquivos contra a assinatura do
fabricante e **recusa compilar** quando detecta alteração. A mensagem é parecida com:

```
Could not load ISCmplr.dll: File "..." is not trusted (incorrect size).
```

Isso **não é um problema do seu projeto** — é uma cópia do Inno Setup que foi modificada
depois de instalada. O comportamento correto é justamente recusar.

Confira assim:

```powershell
# Hash da DLL presente na instalação
(Get-FileHash "C:\Program Files (x86)\Inno Setup 6\ISCmplr.dll" -Algorithm SHA256).Hash

# Hash esperado, gravado pelo próprio fabricante
Get-Content "C:\Program Files (x86)\Inno Setup 6\ISCmplr.dll.issig"
```

Se os dois valores não baterem, a instalação foi alterada. A saída recomendada:

1. Baixe o instalador oficial em <https://jrsoftware.org/isdl.php>.
2. Instale em uma pasta nova (não sobre a instalação suspeita).
3. Aponte o build para ela:
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1 -Iscc "C:\Caminho\Novo\Inno Setup 6\ISCC.exe"
   ```

Não contorne a verificação: ela existe para impedir que um compilador adulterado gere um
instalador adulterado.

## 5. Verificação dos entregáveis

Depois do build, confira:

```powershell
Get-ChildItem dist | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
Get-FileHash dist\OptimizerPC-Setup.exe -Algorithm SHA256
Get-FileHash dist\OptimizerPC-Portable.zip -Algorithm SHA256
```

Checklist mínimo:

- [ ] `dist\publish\OptimizerPC.exe` existe e a pasta tem ~63 MB.
- [ ] `OptimizerPC.exe` abre a janela "Optimizer PC" sem erro.
- [ ] A primeira execução mostra o assistente de boas-vindas.
- [ ] As 16 telas abrem pela navegação lateral.
- [ ] `%LOCALAPPDATA%\OptimizerPC\Logs\optimizerpc-<data>.log` não tem linha `[ERROR]`.
- [ ] `dist\OptimizerPC-Setup.exe` instala, abre e desinstala.
- [ ] `dist\OptimizerPC-Portable.zip` extrai e executa em pasta limpa.

Para acompanhar o log depois de abrir o programa:

```powershell
$log = Get-ChildItem "$env:LOCALAPPDATA\OptimizerPC\Logs" -Filter *.log | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Get-Content $log.FullName -Tail 40
```

## 6. Solução de problemas

**`SDK do .NET 8 nao encontrado`**
O script não achou o `dotnet`. Instale o SDK 8.0 ou passe o caminho completo ao chamar o
`dotnet` manualmente.

**Build falha com erro de plataforma**
O projeto é `x64`. Em um `dotnet` 32 bits, ou com `PlatformTarget` sobrescrito, a
publicação `win-x64` falha. Confirme com `dotnet --info` que o SDK é x64.

**A janela abre e fecha imediatamente**
Veja o log em `%LOCALAPPDATA%\OptimizerPC\Logs`. Falhas não tratadas são registradas com
a categoria `Crash` e o stack trace completo.

**Acentos errados ou chaves de texto aparecendo na interface**
Confira se os arquivos `loc.*.json` estão sendo embutidos. Eles precisam da marcação
`WithCulture="false"` no `.csproj` — sem isso o SDK trata `loc.pt-BR.json` como recurso
satélite e o carregamento falha.

**Instalador não foi gerado**
O script avisa quando não encontra o `ISCC.exe`. Informe o caminho com `-Iscc` (veja
[4.3](#43-se-o-iscc-recusar-as-próprias-dlls)).

**Quero limpar a saída e recompilar do zero**

```powershell
Remove-Item dist -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -Recurse -Directory -Include bin,obj | Remove-Item -Recurse -Force
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
```

## 7. Regenerar ícone e logotipos

Os assets ficam em `src\OptimizerPC.App\Assets\`:

| Arquivo | Uso |
| --- | --- |
| `OptimizerPC.ico` | Ícone do executável e dos atalhos (16, 24, 32, 48, 64, 128 e 256 px) |
| `logo.png` | Logotipo usado na interface (256x256) |
| `logo-large.png` | Logotipo do assistente inicial (512x512) |

O script também gera as imagens do assistente do instalador
(`installer\wizard-large.bmp` e `installer\wizard-small.bmp`).

Para regerar tudo, a partir da raiz do repositório:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Generate-Assets.ps1 -Root (Get-Location).Path
```

O parâmetro `-Root` é obrigatório e aponta para a raiz do repositório (a pasta que contém
`OptimizerPC.sln`). O script não depende de bibliotecas externas: desenha os assets com
primitivas do `System.Drawing` e grava o `.ico` com todos os tamanhos necessários. Se você
substituir por uma arte própria, mantenha os mesmos nomes de arquivo e o `.ico` com todos
os tamanhos — o Windows escolhe o melhor tamanho por contexto, e a ausência dos pequenos
deixa o ícone borrado no Explorador.

Depois de trocar os assets, recompile e publique novamente para que o novo ícone entre no
executável e no instalador.

## 8. Checklist antes de publicar

- [ ] `dotnet test` — 275 aprovados, 0 falhas.
- [ ] Versão em `Directory.Build.props` está correta (a versão é controlada
      manualmente; não há incremento automático).
- [ ] `dist\publish` regenerado do zero, para não carregar arquivos de builds anteriores.
- [ ] Teste manual: abrir o programa, percorrer as 16 telas, conferir o log sem `[ERROR]`.
- [ ] Teste do instalador: instalar, abrir, desinstalar.
- [ ] Teste do portátil: extrair em pasta limpa e executar.
- [ ] SHA-256 dos entregáveis gerado, se você for distribuir os arquivos.
