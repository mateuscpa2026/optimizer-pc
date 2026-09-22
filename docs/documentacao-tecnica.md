# Documentação técnica — Optimizer PC 1.0

Documento para quem vai dar manutenção ou auditar o código. Descreve a arquitetura, as
decisões de projeto, o modelo de segurança, o banco de dados e a localização.

---

## Índice

1. [Visão geral](#1-visão-geral)
2. [Estrutura das camadas](#2-estrutura-das-camadas)
3. [Padrões adotados](#3-padrões-adotados)
4. [Modelo de segurança](#4-modelo-de-segurança)
5. [Execução de comandos externos](#5-execução-de-comandos-externos)
6. [Diagnóstico e índice de saúde](#6-diagnóstico-e-índice-de-saúde)
7. [Limpeza](#7-limpeza)
8. [Otimização, perfis e restauração](#8-otimização-perfis-e-restauração)
9. [Camada de interoperabilidade](#9-camada-de-interoperabilidade)
10. [Persistência](#10-persistência)
11. [Logging](#11-logging)
12. [Localização](#12-localização)
13. [Interface](#13-interface)
14. [Testes](#14-testes)
15. [Propriedades de build](#15-propriedades-de-build)

---

## 1. Visão geral

| Item | Valor |
| --- | --- |
| Linguagem | C# 12 |
| Plataforma | .NET 8 (LTS), `net8.0-windows` |
| Interface | WPF |
| Arquitetura | Clean Architecture + MVVM |
| Arquitetura alvo | x64 |
| Versão | 1.0 |
| Público-alvo | Windows 10 / 11, 64 bits |

Princípio que atravessa todo o código: **o programa nunca faz uma alteração que não
possa descrever, registrar e desfazer.**

## 2. Estrutura das camadas

```
src/OptimizerPC.Core/        21 arquivos .cs — sem dependências além da BCL
├── Abstractions/            contratos: Infrastructure, SystemServices, MaintenanceServices
├── Enums.cs                 severidade, categorias, formatos, tipos de ação
├── Formatting/Humanize.cs   formatação de bytes, percentuais, datas
├── Models/                  Cleanup, Component, Diagnostics, Optimization, Records, Storage, System
└── Security/                ProtectedPaths, SafePathValidator, CommandAllowList

src/OptimizerPC.Services/    84 arquivos .cs — implementa as abstrações do Core
├── Cleaning/                catálogo de alvos, resolução de caminho, varredura, filtro, segurança
├── Configuration/           leitura e gravação de settings.json
├── DependencyInjection/     registro de todos os serviços no contêiner
├── Diagnostics/             coleta de dados, diagnóstico, montagem do Dashboard
├── Gaming/                  detecção de jogos, sessão de boost
├── Interop/                 P/Invoke: kernel, psapi, pdh, advapi32, powrprof, user32, setupapi
├── Localization/            localizador JSON (pt-BR, en-US, es)
├── Logging/                 logger de aplicativo e sink para o banco
├── Notifications/           notificações da bandeja
├── Optimization/            ações, motor de recomendações, índice de saúde, perfil PC Fraco
├── Reports/                 geradores HTML, PDF, JSON, CSV, TXT
├── Restore/                 registros de restauração, payloads, pontos de restauração
├── Scheduling/              integração com o Agendador de Tarefas
├── Security/                execução de comandos, elevação
├── Storage/                 SQLite, repositórios, caminhos do aplicativo, serviço de registro
└── System/                  processos, serviços, inicialização, volumes, monitoramento, ferramentas

src/OptimizerPC.App/         155 arquivos .cs + 24 .xaml — interface
├── Assets/                  ícone e logotipos
├── Controls/                controles próprios (medidor em arco, cartões, gráficos)
├── Converters/              conversores de valor para XAML
├── Localization/            catálogos de texto da interface
├── Services/                diálogos, temas, bandeja
├── Themes/                  tema escuro e claro
├── ViewModels/              um ViewModel por tela (+ Items/ para itens de lista)
└── Views/                   16 telas, janela do shell, assistente inicial, diálogo de mensagem

tests/OptimizerPC.Tests/     24 arquivos .cs — 275 testes
```

Regra de dependência: `App → Services → Core`. O Core não conhece ninguém; os Services
não conhecem WPF; o App não acessa APIs do Windows diretamente.

**Sem projeto Interop separado.** A camada de interoperabilidade vive em
`Services/Interop/`, porque os serviços são os únicos consumidores das APIs nativas — um
projeto extra só acrescentaria indireção.

## 3. Padrões adotados

- **MVVM** com CommunityToolkit.Mvvm: `[ObservableProperty]` para estado,
  `[RelayCommand]` para ações. Nada de code-behind com lógica de negócio.
- **Injeção de dependência** com `Microsoft.Extensions.DependencyInjection`. O registro
  fica em `Services/DependencyInjection/OptimizerPcServiceCollectionExtensions.cs`.
- **Abstrações no Core**: os ViewModels dependem de interfaces (`ISystemInfoService`,
  `ICleanupService`, `IAppLogger`…), o que permite substituir por dublês nos testes sem
  tocar em `Windows`.
- **Assíncrono de ponta a ponta**: operações de sistema são `async` e recebem
  `CancellationToken`. Toda tela tem botão de cancelar enquanto uma operação roda.
- **Nenhuma leitura de sistema no construtor do ViewModel.** O estado de execução vive em
  `ViewModelBase` (`IsBusy`, `ProgressPercent`, `BusyText`, `ErrorKey`); as leituras
  acontecem em `OnInitializeAsync` / `OnNavigatedToAsync`, sempre fora da thread de
  interface.
- **Erros localizados por chave**: o ViewModel guarda `ErrorKey`, a interface traduz.
  Nenhuma mensagem de erro é montada com texto literal no código.

### `ViewModelBase`

Concentra o ciclo de vida das telas:

| Membro | Papel |
| --- | --- |
| `RunAsync` | Executa uma operação com estado de ocupado, cancelamento e tratamento de erro |
| `EnsureInitializedAsync` | Prepara a tela uma única vez, na primeira navegação |
| `NotifyNavigatedToAsync` / `NotifyNavigatedFromAsync` | Chamados pelo shell ao entrar e sair da tela |
| `SetProgress`, `SetStatus`, `ReportError` | Estado de progresso, mensagem de status e erro |
| `Dispose` | Cancela a fonte vitalícia e cancela a inscrição em `LanguageChanged` |

As propriedades de estado (`IsBusy`, `ProgressPercent`, `BusyText`, `ErrorKey`, …) têm
setter privado — só o próprio ViewModel muda o próprio estado.

> **Atenção em XAML:** `ProgressBar.Value`, `CheckBox.IsChecked` e
> `ComboBox.SelectedItem/SelectedValue/SelectedIndex` são de **duas vias por padrão** no
> WPF. Ligar um desses alvos a uma propriedade sem setter público derruba a tela com
> `InvalidOperationException: A TwoWay or OneWayToSource binding cannot work on the
> read-only property`. Para essas propriedades, escreva sempre
> `Value="{Binding ProgressPercent, Mode=OneWay}"`. Existe um teste que falha se alguém
> esquecer — veja [Testes](#14-testes).

## 4. Modelo de segurança

O núcleo de segurança são três classes do Core, todas cobertas por testes.

### `ProtectedPaths` — o que nunca pode ser tocado

Lista explícita de raízes e nomes protegidos:

- Raízes: pasta do Windows, `Windows`, `System32`, `SysWOW64`, `WinSxS`,
  `System32\drivers`, `System32\config`, `Boot`, `Fonts`, `assembly`, `Microsoft.NET`,
  `servicing`.
- Nomes de arquivo: `bootmgr`, `bootnxt`, `ntldr`, `bcd`, `boot.ini`, `pagefile.sys`,
  `swapfile.sys`.

A verificação é feita sobre o **caminho canônico**, não sobre a string informada — links
simbólicos, junções e caminhos com `..` são resolvidos antes da comparação.

### `SafePathValidator` — como um caminho é aprovado

Antes de qualquer operação de arquivo, o caminho passa por:

1. Canonicalização (resolver `..`, `8.3`, links e junções).
2. Verificação contra `ProtectedPaths`.
3. Verificação de que está dentro de uma raiz autorizada para aquela operação.

Um caminho fora das raízes permitidas é recusado, e a recusa é registrada no log.

### `CommandAllowList` — privilégio mínimo na execução

Ver [seção 5](#5-execução-de-comandos-externos).

### Elevação

`ElevationService` usa apenas os mecanismos oficiais do Windows. O aplicativo tem um
manifesto que o mantém em execução normal (`asInvoker`); quando uma função exige
privilégio, o serviço solicita a elevação pelo UAC. Sem ela, a operação é recusada com
mensagem clara e o restante do programa continua utilizável.

Não há instalação de serviço, driver ou agendador com privilégio elevado para contornar o
UAC.

## 5. Execução de comandos externos

Toda execução de processo passa por `CommandExecutionService`, que consulta a
`CommandAllowList`. As regras:

- O executável é resolvido **sempre** em `%SystemRoot%\System32` — nunca pelo `PATH`.
- O executável é identificado por caminho completo; se não estiver na lista, é recusado.
- Os argumentos são validados um a um contra regras pré-aprovadas.
- **Não há interpretador de comandos** (`cmd.exe` / `powershell.exe` não são usados).
- `UseShellExecute = false`, `CreateNoWindow = true`, com captura de saída assíncrona.
- Cancelamento propaga para o processo.

Comandos e argumentos permitidos:

| Executável | Argumentos permitidos | Elevação |
| --- | --- | --- |
| `sfc.exe` | `/scannow`, `/verifyonly`, `/scanfile` | Obrigatória |
| `dism.exe` | `/online`, `/cleanup-image`, `/scanhealth`, `/checkhealth`, `/restorehealth` | Obrigatória |
| `chkdsk.exe` | `/scan`, `/perf` (+ letra de unidade) — somente leitura | — |
| `defrag.exe` | `/o`, `/c`, `/a`, `/v` (+ letra de unidade) | — |
| `powercfg.exe` | `/list`, `/getactivescheme`, `/setactive`, `/energy`, `/lastwake` | — |
| `ipconfig.exe` | `/flushdns`, `/displaydns`, `/all` | — |
| `cleanmgr.exe` | `/d`, `/sagerun:1`, `/lowdisk` (+ letra de unidade) | — |
| `schtasks.exe` | `/create`, `/delete`, `/query`, `/tn`, `/tr`, `/sc`, `/st`, `/d`, `/f` | — |

Os argumentos com valor seguem regras próprias (`ArgumentValueRule`), por exemplo:
`/tn` só aceita `[A-Za-z0-9][A-Za-z0-9._-]{0,63}`; `/sc` só aceita `DAILY`, `WEEKLY` ou
`MONTHLY`; `/st` só aceita `HH:MM`; `/tr` só aceita `%SELF%` com uma opção `--algo`
opcional. Isso impede que um valor vindo da interface seja usado para injetar outro
comando.

## 6. Diagnóstico e índice de saúde

### Coleta

`SystemDataCollector` monta um `SystemSnapshot` com dados de SO, processador, memória,
volumes, discos, temperatura, inicialização, serviços, processos e rede. Cada leitura tem
tratamento próprio de indisponibilidade: quando o Windows ou o firmware não fornece o
dado, o campo fica marcado como indisponível e a interface diz isso ao usuário.

### Verificações

`DiagnosticService` produz uma lista de `DiagnosticCheck`, cada um com `TitleKey`,
severidade, explicação e, quando aplicável, ação sugerida:

`Diagnose.Os`, `Diagnose.Cpu`, `Diagnose.Memory`, `Diagnose.Volume`, `Diagnose.Drives`,
`Diagnose.Temperature`, `Diagnose.Startup`, `Diagnose.Services`, `Diagnose.Processes`,
`Diagnose.Recoverable`, `Diagnose.RecycleBin`, `Diagnose.Reboot`, `Diagnose.Updates`,
`Diagnose.Storage`, além de `AddRegistryFlagCheck` (4 verificações por chave de registro),
`AddSecureBootCheck` e `AddSmartScreenCheck`.

### Índice de saúde

`HealthScoreService` calcula a nota 0–100 a partir de fatores ponderados derivados dos
itens de diagnóstico. A soma dos pesos é normalizada, então a nota é sempre comparável
entre execuções da mesma máquina. A interface rotula explicitamente o número como
estimativa interna — não é um laudo, e o programa não converte a nota em promessa de
ganho de desempenho.

### Recomendações

`RecommendationEngine` gera itens com `TitleKey`, motivo e `ActionId`:

`Recommendation.HighMemory`, `HighCpu`, `Startup`, `Services`, `PowerPlan`
(`SetPowerPlan`), `VisualEffects` (`DisableVisualEffects`), `LowDiskSpace`
(`TrimVolume` quando o disco é SSD), `DiskIntegrity`, `Downloads`.

Cada recomendação diz **por que** foi feita, e a interface usa linguagem de potencial
("espaço potencialmente recuperável", "potencial melhoria identificada"), nunca uma
promessa de resultado.

## 7. Limpeza

Fluxo em duas etapas, sempre:

1. **Varredura** (`CleanupScanner`) — percorre cada alvo e soma o tamanho. Nada é apagado.
   A interface mostra o espaço potencialmente recuperável por categoria.
2. **Limpeza** (`CleanupService`) — apaga somente as categorias marcadas e confirmadas,
   com progresso, cancelamento e registro.

14 alvos, definidos em `CleanupTargetCatalog`: temporários do usuário, temporários do
Windows, cache de miniaturas, lixeira, relatórios de erro, Otimização de Entrega, cache
do Windows Update, prefetch, despejos de falha, resíduos do instalador, logs antigos,
caches de navegador, caches de aplicativo e cache de fontes.

Camadas de proteção aplicadas a cada arquivo antes da exclusão:

- `CleanupPathResolver` resolve o caminho final de cada alvo.
- `CleanupFileFilter` decide se um arquivo **individualmente** pode ser apagado (idade,
  extensão, tipo), dentro de uma categoria já permitida.
- `CleanupSafety` verifica o caminho contra `ProtectedPaths` e `SafePathValidator`.
- `CleanupTargetSelection` impede que categorias não selecionadas sejam tocadas.

Arquivos pessoais não têm categoria correspondente, e pastas de documentos, imagens e
vídeos não são alvos de nenhum alvo de limpeza.

## 8. Otimização, perfis e restauração

### Ações

Cada ação de otimização segue o mesmo contrato: ler o estado atual → gravar o estado
anterior em `Backups` → aplicar → registrar em `RestoreRecords`. Tipos disponíveis
(`OptimizationActionKind`): plano de energia, efeitos visuais, inicialização, serviços,
integridade do volume e perfil visual de alto desempenho.

### Registro usado

Apenas chaves **conhecidas**, e apenas em `HKEY_CURRENT_USER`:

- `Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` — preferências de efeitos
  visuais do usuário (não o preset geral do sistema).
- `Software\Microsoft\Windows\CurrentVersion\Run` e as chaves `StartupApproved`
  correspondentes — estado de inicialização de programas.
- Chaves de Game Mode / notificações para a sessão de jogo.

Nada é apagado do registro: valores anteriores são gravados antes da alteração e
restaurados no desfazer. Não existe "limpeza de registro" no programa.

### Perfis

- **PC Fraco** (`LowEndModeService`): avalia o equipamento (`Assess`), monta a lista de
  ações compatíveis com esse cenário (`BuildActions`) e aplica o plano (`ApplyAsync`).
- **Gamer** (`GameBoostService`): abre uma sessão (`StartAsync`) com plano de energia,
  pausa de notificações e perfil visual; guarda o estado anterior de cada item; encerra
  com `StopAsync`, revertendo o que mudou. A sessão ativa é exposta em `CurrentSession`, e
  a interface mostra o que está aplicado.

Nenhum dos dois perfis altera BIOS, firmware, tensões ou frequências.

### Restauração

`RestoreService` mantém os registros; `RestorePayloads` descreve o estado anterior;
`RegistryValueWriter` grava e restaura valores de registro tipados (DWord, QWord,
ExpandString, MultiString, Binary, String). `RestorePointManager` cria pontos de
restauração do Windows via `SRSetRestorePoint` quando a Proteção do Sistema permite.

`UndoAsync` aplica de volta o estado anterior e marca o registro como desfeito.

## 9. Camada de interoperabilidade

`Services/Interop/` concentra as declarações P/Invoke, todas com `SetLastError = true` e
marshalling explícito:

| Arquivo | Cobre |
| --- | --- |
| `NativeKernel` | Memória, arquivos, tempo de sistema |
| `NativeProcess` | Enumeração e abertura de processos, prioridade |
| `NativeProcessor` | Informações de processador |
| `NativePdh` | Contadores de desempenho (PDH) para CPU, disco e rede |
| `NativeServices` | Service Control Manager |
| `NativeStorage` | Volumes, discos físicos, SMART via `setupapi` |
| `NativePower` | Planos de energia |
| `NativeRestorePoint` | Pontos de restauração |
| `NativeDesktop` | Janela, DPI, bandeja |
| `NativeShell` | Shell do Windows |
| `NativeSmbios` | Informações de firmware expostas pelo SMBIOS (somente leitura) |

`ProcessSafety` e `ServiceSafety` marcam o que é crítico: processos essenciais não podem
ser encerrados pela interface e serviços essenciais não podem ser alterados.

## 10. Persistência

**SQLite** (`optimizerpc.db`), acesso por `Microsoft.Data.Sqlite` com comandos
parametrizados — nenhuma consulta é montada por concatenação de string.

| Tabela | Conteúdo |
| --- | --- |
| `History` | Tudo o que foi executado, com data, categoria e resultado |
| `Logs` | Registros de log persistidos |
| `RestoreRecords` | Alterações reversíveis, com estado anterior e status |
| `Reports` | Relatórios gerados, com formato, caminho e tamanho |

Esquema criado por `SqliteDatabase` com `CREATE TABLE IF NOT EXISTS` (migração
idempotente). O mapeamento fica em `SqliteMapping`; os acessos, nos repositórios
(`HistoryRepository`, `LogRepository`, `RestoreRepository`, `ReportRepository`).

**JSON** (`settings.json`), por `SettingsService`, para as configurações do aplicativo —
arquivo pequeno, editável e legível. A serialização usa `System.Text.Json` com opções
definidas em `AppJson`.

Caminhos definidos em `AppPaths` (implementação de `IAppPaths`), com opção de pasta
alternativa para os testes não escreverem no perfil do usuário.

## 11. Logging

- `AppLogger` é o ponto único de log, com níveis Info / Warning / Error / Debug e
  categorias por área.
- `DatabaseLogSink` persiste no SQLite; o arquivo diário
  `Logs\optimizerpc-AAAAMMDD.log` é gravado em texto.
- Falhas não tratadas são registradas com categoria `Crash` — o programa não fecha em
  silêncio.
- Os textos de log são **ASCII**, sem acentuação, para que o arquivo abra corretamente em
  qualquer editor e ferramenta de análise.

## 12. Localização

Dois pares de catálogos JSON, um por camada, cada um com três idiomas (`pt-BR`, `en-US`,
`es`):

- `Services/Localization/loc.*.json` — mensagens de serviço (~180 chaves).
- `App/Localization/loc.*.json` — textos da interface (~1479 chaves).

O `JsonLocalizer` carrega os catálogos como recursos incorporados, resolve por chave e
dispara `LanguageChanged`. `ViewModelBase` escuta esse evento e atualiza os textos das
telas sem recriar a janela.

Trocar o idioma em tempo de execução não reinicia o aplicativo.

Detalhes que importam na manutenção:

- Os catálogos são embutidos com `WithCulture="false"`. Sem isso, o SDK do .NET infere a
  cultura a partir do nome `loc.pt-BR.json` e empurra o recurso para uma montagem
  satélite, quebrando o carregamento.
- Os três idiomas precisam manter **exatamente o mesmo conjunto de chaves**. Um teste
  falha se uma chave existir em um idioma e faltar em outro.
- Textos em português e espanhol levam acentuação correta. Um teste procura formas sem
  acento de palavras que sempre exigem acento (por exemplo `nao`, `configuracoes`,
  `configuracion`) e falha se encontrar.
- Chaves compartilhadas entre as duas camadas (`App.Title`, `Common.Ok`, …) precisam ter
  o mesmo texto nos dois catálogos — também verificado por teste.

## 13. Interface

- **Shell** com navegação lateral fixa agrupada em 6 seções; cada tela é resolvida por um
  identificador (`Screen`) mapeado em `NavigationCatalog` — nunca por caminho informado
  de fora.
- **Temas escuro e claro** em `Themes/`, trocáveis em tempo de execução.
- **Controles próprios** em `Controls/`: medidor em arco (`GaugeRing`), cartões e
  gráficos. O `GaugeRing` anima o valor para que a leitura não "salte".
- **Responsivo** de 1280x720 a monitores ultrawide; o layout é fluido.
- **Acessibilidade**: nomes de acessibilidade nos controles, navegação por teclado e
  contraste adequado nos dois temas.
- **Assistente inicial** (`FirstRunWindow`) em 5 etapas, do boas-vindas ao botão INICIAR
  DIAGNÓSTICO.
- **Bandeja do sistema** com ícone e notificações; o WinForms é usado **apenas** para o
  ícone da bandeja. Os `using` implícitos de WinForms e Drawing são removidos no csproj,
  porque colidem com tipos homônimos do WPF (`Application`, `Brush`, `Point`, `Screen`,
  `Icon`, `Size`).

## 14. Testes

```powershell
dotnet test tests\OptimizerPC.Tests\OptimizerPC.Tests.csproj
```

275 testes. Áreas cobertas:

| Área | O que verifica |
| --- | --- |
| Cleaning | Catálogo de alvos, filtro de arquivos, seleção de categorias, cálculo de espaço |
| Storage | Análise de ocupação, arquivos grandes, duplicados por conteúdo |
| Diagnostics | Montagem do diagnóstico, severidades, índice de saúde |
| Optimization | Motor de recomendações, ações aplicáveis, plano do perfil PC Fraco |
| Restore | Registro de alterações, desfazer, payloads |
| Configuration | Leitura, gravação e valores padrão |
| Logging | Níveis, retenção, formato |
| Formatting | Formatação de bytes, percentuais e datas |
| Localization | Paridade de chaves entre idiomas e camadas; acentuação |
| Ui | Vínculos XAML incompatíveis com propriedades somente leitura |
| Security | Ver [abaixo](#testes-de-segurança) |

### Testes de segurança

Escritos para **falhar** se o programa passar a aceitar algo perigoso:

- Nenhum caminho crítico do sistema pode ser considerado apagável (`C:\Windows`,
  `System32`, `WinSxS`, `Fonts`, `Boot`, arquivos de inicialização).
- Nenhum arquivo pessoal pode entrar em uma categoria de limpeza.
- Nenhum comando fora da lista pode ser executado.
- Nenhum argumento que não case com a regra pré-aprovada pode ser aceito.
- Nenhuma alteração em Windows Defender, Firewall, BIOS ou firmware pode ser oferecida.
- Nenhum caminho com `..`, link simbólico, junção ou nome curto 8.3 pode escapar da
  validação.

## 15. Propriedades de build

`Directory.Build.props` na raiz define o que vale para todos os projetos:

| Propriedade | Valor |
| --- | --- |
| `Version` | 1.0 |
| `AssemblyVersion` / `FileVersion` | 1.0.0.0 |
| `TargetFramework` | `net8.0-windows` |
| `LangVersion` | 12.0 |
| `Nullable` | enable |
| `ImplicitUsings` | enable |
| `InvariantGlobalization` | false (a localização real exige cultura) |
| `PlatformTarget` | x64 |
| `EnableWindowsTargeting` | true |
| `NeutralLanguage` | pt-BR |
| `Deterministic` | true |
| `DebugType` | embedded (sem arquivos `.pdb` soltos na publicação) |
| `NoWarn` | `CS1591;CA1416` |

Pacotes NuGet:

| Projeto | Pacote | Versão |
| --- | --- | --- |
| App | CommunityToolkit.Mvvm | 8.2.2 |
| App | Microsoft.Data.Sqlite | 8.0.4 |
| App | Microsoft.Extensions.DependencyInjection | 8.0.1 |
| Services | Microsoft.Data.Sqlite | 8.0.4 |
| Services | Microsoft.Extensions.DependencyInjection | 8.0.1 |
| Tests | Microsoft.NET.Test.Sdk | 17.11.1 |
| Tests | xunit | 2.9.2 |
| Tests | xunit.runner.visualstudio | 2.8.2 |

Todas as dependências são oficiais da Microsoft, do xUnit ou do CommunityToolkit. Não há
biblioteca de terceiros fora dessas origens.
