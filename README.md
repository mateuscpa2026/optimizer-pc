# Optimizer PC

**Otimize. Limpe. Desempenhe.**

Aplicativo desktop para Windows 10 e 11 (64 bits) que analisa o computador, mostra o que
realmente consome recursos, limpa apenas arquivos descartáveis e aplica otimizações
conhecidas e reversíveis — sempre com o que será alterado visível antes da execução,
registro no histórico e possibilidade de restauração.

---

## Índice

- [O que o programa faz](#o-que-o-programa-faz)
- [O que o programa não faz](#o-que-o-programa-não-faz)
- [Requisitos](#requisitos)
- [Entregáveis](#entregáveis)
- [Como compilar](#como-compilar)
- [Estrutura do repositório](#estrutura-do-repositório)
- [Onde ficam os dados](#onde-ficam-os-dados)
- [Documentação](#documentação)
- [Tecnologias](#tecnologias)
- [Testes](#testes)

---

## O que o programa faz

16 telas, agrupadas em 6 seções na navegação lateral:

| Seção | Telas |
| --- | --- |
| Visão geral | Dashboard, Diagnóstico |
| Otimização | Otimização, Limpeza, Inicialização |
| Monitoramento | Processos, Serviços, Desempenho, Armazenamento |
| Perfis | PC Fraco, Gamer |
| Ferramentas | Ferramentas, Backup e Restauração, Relatórios, Histórico |
| Sistema | Configurações |

- **Diagnóstico** de sistema operacional, processador, memória, volumes, temperatura,
  inicialização, serviços, processos, espaço recuperável, lixeira, reinicialização
  pendente, atualizações, armazenamento, Secure Boot e SmartScreen.
- **Limpeza** de 14 categorias de arquivos descartáveis, com varredura prévia que mostra
  o espaço potencialmente recuperável antes de qualquer exclusão.
- **Otimização** de plano de energia, efeitos visuais, inicialização e serviços — cada
  alteração com estado anterior guardado e botão de restauração.
- **PC Fraco** e **Gamer**: perfis que aplicam um conjunto de alterações já conhecidas
  pelo programa, sem qualquer ajuste agressivo ou não reversível.
- **Monitoramento** em tempo real de uso de processador, memória, disco e rede.
- **Armazenamento**: análise de ocupação por pasta, arquivos grandes e arquivos
  duplicados por conteúdo.
- **Relatórios** em HTML, PDF, JSON, CSV e TXT, gerados a partir dos dados reais do
  diagnóstico, das limpezas e das otimizações executadas.
- **Histórico** de tudo o que foi feito, com filtro por categoria e data.
- **Ferramentas**: atalhos para utilitários oficiais do próprio Windows (SFC, DISM,
  CHKDSK, Desfragmentador, PowerCfg, IPConfig, Limpeza de Disco, Agendador de Tarefas),
  sempre a partir de `System32` e com os argumentos pré-aprovados pelo programa.
- **Agendamento** de manutenção periódica pelo Agendador de Tarefas do Windows.
- **Interface** em português, inglês e espanhol, com tema escuro e claro.

## O que o programa não faz

Estas ausências são decisões de projeto, não limitações temporárias:

- Não exclui arquivos pessoais automaticamente. Documentos, fotos, vídeos e afins nunca
  são alvo de limpeza.
- Não toca em `C:\Windows`, `System32`, `SysWOW64`, `WinSxS`, `Fonts`, `Boot`, arquivos
  de inicialização nem em `pagefile.sys` / `swapfile.sys`.
- Não desativa Windows Defender, Firewall ou qualquer mecanismo de segurança.
- Não altera BIOS, firmware ou UEFI, não faz overclock e não mexe em tensões.
- Não baixa nem executa nada da internet. O programa não abre conexão de rede: não há
  `HttpClient` nem qualquer cliente de rede no código.
- Não faz limpeza de registro indiscriminada. As poucas chaves de registro usadas são
  conhecidas, listadas no histórico e reversíveis pelo backup gravado antes da alteração.
- Não inventa resultados. Números de "ganho" ou "aceleração" não são exibidos porque não
  são medidos.

> O índice de saúde exibido no Dashboard é uma **estimativa interna** calculada a partir
> dos itens de diagnóstico encontrados, não um diagnóstico científico.

## Requisitos

- Windows 10 ou Windows 11, 64 bits.
- Para **executar** os entregáveis: nada além do Windows. A publicação é *self-contained*
  e não exige o .NET instalado.
- Para **compilar**: .NET SDK 8.0 (a versão LTS) e, opcionalmente, Inno Setup 6 para gerar
  o instalador.

## Entregáveis

Gerados em `dist\`:

| Arquivo | Tamanho aproximado | Descrição |
| --- | --- | --- |
| `dist\publish\OptimizerPC.exe` | 0,2 MB | Executável principal (pasta de publicação completa) |
| `dist\OptimizerPC-Setup.exe` | 48 MB | Instalador |
| `dist\OptimizerPC-Portable.zip` | 63 MB | Versão portátil (extrair e executar) |

A pasta `dist\publish\` deve ser distribuída inteira: o executável depende dos arquivos
ao lado dele. Para uma cópia de arquivo único, use o `OptimizerPC-Setup.exe` ou o
`OptimizerPC-Portable.zip`.

## Como compilar

O caminho mais direto, a partir da raiz do repositório:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Release.ps1
```

O script compila, roda os testes, publica o aplicativo, gera o instalador (quando o
Inno Setup 6 estiver disponível) e monta o pacote portátil.

Passo a passo manual, sem o script:

```powershell
# 1. Compilar e testar
dotnet test tests\OptimizerPC.Tests\OptimizerPC.Tests.csproj -c Release

# 2. Publicar (self-contained, win-x64)
dotnet publish src\OptimizerPC.App\OptimizerPC.App.csproj -c Release -r win-x64 --self-contained true -o dist\publish

# 3. Instalador (requer Inno Setup 6)
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\OptimizerPC.iss
```

Detalhes, incluindo como obter o SDK e o Inno Setup, estão em
[docs/instrucoes-de-build.md](docs/instrucoes-de-build.md).

## Estrutura do repositório

```
Optimizer-PC/
├── OptimizerPC.sln
├── Directory.Build.props          propriedades comuns (versão, TFM, x64, nulabilidade)
├── src/
│   ├── OptimizerPC.Core/          modelos, enums, abstrações e o núcleo de segurança
│   ├── OptimizerPC.Services/      serviços: diagnóstico, limpeza, otimização, dados, log
│   └── OptimizerPC.App/           interface WPF (Views, ViewModels, temas, localização)
├── tests/
│   └── OptimizerPC.Tests/         testes unitários e de segurança
├── installer/
│   ├── OptimizerPC.iss            script do Inno Setup 6
│   └── portable-LEIA-ME.txt       leia-me incluído no pacote portátil
├── tools/
│   ├── Build-Release.ps1          gera todos os entregáveis
│   └── Generate-Assets.ps1        regenera ícone e logotipos
├── docs/
│   ├── manual-do-usuario.md
│   ├── documentacao-tecnica.md
│   └── instrucoes-de-build.md
└── dist/                          saída (não versionada)
```

Responsabilidades por camada:

- **Core** não depende de nada além da BCL. Contém os modelos, as abstrações que os
  serviços implementam e as três classes que decidem o que pode ser apagado
  (`ProtectedPaths`, `SafePathValidator`, `CommandAllowList`).
- **Services** implementa as abstrações do Core. Fala com o Windows pelas APIs nativas
  (`Interop/`), grava em SQLite e nunca referencia WPF.
- **App** é a interface. Nenhum ViewModel toca o sistema no construtor: leituras de
  sistema acontecem fora da thread de interface, ao abrir a tela.

## Onde ficam os dados

Tudo em `%LOCALAPPDATA%\OptimizerPC`:

| Caminho | Conteúdo |
| --- | --- |
| `settings.json` | Configurações do aplicativo (idioma, tema, preferências) |
| `optimizerpc.db` | Banco SQLite: histórico, registros, relatórios e logs |
| `Logs\optimizerpc-AAAAMMDD.log` | Log diário de execução |
| `Relatorios\` | Relatórios gerados |
| `Backups\` | Cópias do estado anterior das alterações, usadas pela restauração |

O programa não grava em nenhum outro lugar, exceto onde o usuário escolher salvar um
relatório. Desinstalar o aplicativo não remove essa pasta.

## Documentação

- [Manual do usuário](docs/manual-do-usuario.md) — instalação, cada tela explicada, perguntas frequentes.
- [Documentação técnica](docs/documentacao-tecnica.md) — arquitetura, segurança, banco de dados, localização.
- [Instruções de build](docs/instrucoes-de-build.md) — como compilar, publicar e empacotar.
- [Changelog](CHANGELOG.md) — histórico de versões.

## Tecnologias

- **C# 12** sobre **.NET 8** (LTS), alvo `net8.0-windows`, x64.
- **WPF** para a interface, com **MVVM** (CommunityToolkit.Mvvm).
- **SQLite** (Microsoft.Data.Sqlite) para histórico, registros e relatórios.
- **Injeção de dependência** (Microsoft.Extensions.DependencyInjection) ligando as
  abstrações do Core às implementações dos Services.
- **APIs nativas do Windows** (`kernel32`, `psapi`, `pdh`, `advapi32`, `powrprof`,
  `user32`, `setupapi`, `shell32`) por P/Invoke, sem dependências externas.
- **Inno Setup 6** para o instalador.
- **xUnit** para os testes.

Nenhuma biblioteca fora das oficiais da Microsoft e do CommunityToolkit.

## Testes

```powershell
dotnet test tests\OptimizerPC.Tests\OptimizerPC.Tests.csproj
```

275 testes cobrindo limpeza, cálculo de espaço, diagnóstico, índice de saúde,
configurações, histórico, log, restauração, localização e — igualmente importante — um
conjunto de testes de segurança que falha se o programa passar a aceitar caminhos
críticos, arquivos pessoais, comandos arbitrários ou alterações em Defender, Firewall,
BIOS ou firmware.
