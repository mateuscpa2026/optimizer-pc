# Manual do usuário — Optimizer PC 1.0

**Otimize. Limpe. Desempenhe.**

Este manual explica como instalar, usar e desinstalar o Optimizer PC, o que cada tela
faz e por que algumas opções aparecem desativadas.

---

## Índice

1. [Antes de começar](#1-antes-de-começar)
2. [Instalação](#2-instalação)
3. [Primeira execução](#3-primeira-execução)
4. [As 16 telas](#4-as-16-telas)
5. [Privilégios de administrador](#5-privilégios-de-administrador)
6. [Segurança: o que o programa nunca faz](#6-segurança-o-que-o-programa-nunca-faz)
7. [Privacidade](#7-privacidade)
8. [Onde ficam os seus dados](#8-onde-ficam-os-seus-dados)
9. [Perguntas frequentes](#9-perguntas-frequentes)
10. [Desinstalação](#10-desinstalação)
11. [Problemas conhecidos e limitações](#11-problemas-conhecidos-e-limitações)

---

## 1. Antes de começar

**Requisitos:** Windows 10 ou Windows 11, 64 bits.

O Optimizer PC não precisa do .NET instalado: os arquivos já trazem tudo o que é
necessário. Também não precisa de conexão com a internet — o programa não faz nenhuma
conexão de rede, nem para verificar atualizações.

Uma observação importante sobre expectativa: nenhum programa sério promete "deixar o PC
300% mais rápido". O Optimizer PC mostra o que encontrou, o que pode ser liberado e o que
recomenda — sempre com números medidos na sua máquina. Se o ganho não for mensurável, o
programa não inventa um número.

## 2. Instalação

### Opção A — Instalador

1. Execute `OptimizerPC-Setup.exe`.
2. Siga o assistente. Você pode escolher a pasta de instalação e se quer um atalho na
   área de trabalho.
3. Ao final, o Optimizer PC estará no Menu Iniciar.

### Opção B — Versão portátil

1. Extraia `OptimizerPC-Portable.zip` em qualquer pasta (pen drive, Área de Trabalho,
   Documentos).
2. Execute `OptimizerPC.exe` de dentro da pasta extraída.
3. Não há instalação. Para remover, apague a pasta.

> A pasta precisa ser mantida inteira. O `OptimizerPC.exe` depende dos arquivos que
> estão ao lado dele.

Para remover os dados gravados (histórico, configurações), veja a
[seção 10](#10-desinstalação).

## 3. Primeira execução

Na primeira vez, o programa abre o assistente **"Bem-vindo ao Optimizer PC"**, que:

1. Apresenta o programa e o que ele faz.
2. Lê as características do seu computador (processador, memória, discos,
   sistema operacional).
3. Explica o que será analisado.
4. Mostra o resultado dessa leitura inicial.
5. Termina com **"Seu PC está pronto para análise."** e o botão
   **INICIAR DIAGNÓSTICO**.

Nessa primeira execução também é pedida a escolha do idioma (português, inglês ou
espanhol) e do tema (escuro ou claro). As duas escolhas podem ser mudadas depois em
**Configurações**.

## 4. As 16 telas

### Visão geral

#### Dashboard

A tela inicial. Mostra:

- **Índice de saúde** — uma nota de 0 a 100 com um medidor em arco. É uma **estimativa
  interna** calculada a partir dos itens de diagnóstico encontrados, não um diagnóstico
  científico do computador.
- **Uso de processador, memória e disco** em tempo real, com medidores em anel.
- **Espaço livre** do disco do sistema.
- **Problemas encontrados** e **otimizações recomendadas**, com a contagem.
- **Espaço potencialmente recuperável** — o que a limpeza poderia liberar (lixeira e
  arquivos temporários).
- **Fatores** que compõem a nota, cada um com o seu peso.
- O botão **OTIMIZAR AGORA**, que executa de uma vez as ações recomendadas.

Antes de aplicar qualquer coisa, aparece a confirmação com a lista do que será alterado.

#### Diagnóstico

Executa uma análise completa e lista os itens verificados, cada um com severidade
(OK, atenção, crítico) e uma explicação em texto simples:

| Item | O que verifica |
| --- | --- |
| Sistema operacional | Versão, compilação, arquitetura |
| Processador | Modelo, núcleos, uso, velocidade |
| Memória | Total, disponível, pressão de uso |
| Volumes | Espaço livre e ocupação de cada disco |
| Discos | Saúde reportada pelo próprio dispositivo (SMART) |
| Temperatura | Quando o firmware expõe o dado; caso contrário, explica a limitação |
| Inicialização | Programas que iniciam com o Windows |
| Serviços | Serviços em execução e o que é incomum |
| Processos | Consumo por processo |
| Espaço recuperável | O que a limpeza pode liberar |
| Lixeira | Tamanho do que está na lixeira |
| Reinicialização pendente | Se o Windows espera um reinício |
| Atualizações | Estado do serviço de atualização |
| Armazenamento | Tipo de disco (SSD/HDD) e espaço |
| Secure Boot | Se está ativo, quando o firmware informa |
| SmartScreen | Se a proteção está ativa |

Cada item informa o motivo e, quando o dado não está disponível no seu equipamento, o
programa **diz que não está disponível** em vez de mostrar um valor inventado.

### Otimização

#### Otimização

Lista as recomendações com o motivo de cada uma. As ações possíveis incluem:

- **Plano de energia** — trocar para Alto desempenho.
- **Efeitos visuais** — reduzir animações da interface.
- **Integridade do disco** — verificar o volume.
- **Limpeza de disco** — quando há pouco espaço livre.

Cada ação mostra o estado atual e o que vai mudar. Aplicar uma ação registra o estado
anterior no histórico, para que você possa desfazer.

#### Limpeza

Varre 14 categorias de arquivos descartáveis e mostra, **antes de apagar**, o espaço
potencialmente recuperável de cada uma:

| Categoria | O que é |
| --- | --- |
| Arquivos temporários do usuário | `%TEMP%` |
| Arquivos temporários do Windows | `C:\Windows\Temp` |
| Cache de miniaturas | Cache de imagens do Explorer |
| Lixeira | Conteúdo já enviado para a lixeira |
| Relatórios de erro do Windows | Despejos e relatórios de falhas |
| Otimização de Entrega | Cache do serviço de entrega de atualizações |
| Cache do Windows Update | Arquivos baixados já instalados |
| Dados de pré-carregamento | Prefetch |
| Despejos de falha | Crash dumps |
| Resíduos do instalador | Sobras de instalações antigas |
| Logs antigos | Arquivos de log do sistema |
| Caches de navegador | Cache de navegadores instalados |
| Caches de aplicativos | Caches de aplicativos de uso comum |
| Cache de fontes | Cache de fontes do Windows |

Você escolhe o que limpar. Nada é apagado sem que você marque e confirme.

#### Inicialização

Lista os programas que iniciam junto com o Windows, com o impacto estimado de cada um.
Você pode **desativar** um item — o programa guarda o estado anterior e permite
**reativar** depois. Desativar um item de inicialização não desinstala nem apaga nada.

### Monitoramento

#### Processos

Lista os processos em execução com uso de processador, memória, disco e rede, ordenável
por coluna. É possível **encerrar** um processo, com confirmação. Processos do sistema e
processos críticos aparecem marcados e protegidos: o programa não encerra o que
derrubaria o Windows.

#### Serviços

Lista os serviços do Windows com estado (em execução / parado) e tipo de início. Serviços
críticos são marcados e não podem ser alterados pelo programa.

#### Desempenho

Gráficos em tempo real de processador, memória, disco e rede, com histórico recente. É a
tela para acompanhar o efeito de uma otimização enquanto ela acontece.

#### Armazenamento

- **Ocupação por pasta** — onde o espaço do disco está sendo usado.
- **Arquivos grandes** — os maiores arquivos do disco, com caminho e tamanho.
- **Arquivos duplicados** — comparação por **conteúdo** (hash), não por nome, para não
  apontar arquivos diferentes como duplicados.

O programa mostra os resultados e **não apaga nada automaticamente**. Você decide o que
fazer com a informação, e a exclusão de arquivos pessoais não é oferecida.

### Perfis

#### PC Fraco

Avalia se o computador se encaixa no perfil de hardware modesto (pouca memória, disco
mecânico, processador antigo) e monta um conjunto de ajustes adequados a esse cenário:
redução de efeitos visuais, escolha de plano de energia e sugestões de desativação de
inicialização. O programa explica cada sugestão e o motivo dela.

#### Gamer

Inicia uma **sessão de jogo** que, enquanto estiver ativa:

- muda o plano de energia para alto desempenho;
- pausa notificações do Windows;
- registra o estado anterior de cada item alterado.

Ao encerrar a sessão, tudo é revertido ao estado anterior. O programa mostra o que está
ativo na sessão e o que será restaurado.

Este perfil **não** faz overclock, não altera BIOS, não mexe em tensões nem em
frequências do hardware — nada disso é oferecido.

### Ferramentas

#### Ferramentas

Atalhos para utilitários **oficiais do Windows**, executados a partir de
`C:\Windows\System32` com argumentos pré-aprovados:

| Ferramenta | Uso padrão |
| --- | --- |
| SFC | Verificação de arquivos de sistema (`/scannow`) |
| DISM | Verificação de integridade da imagem do Windows |
| CHKDSK | Verificação do disco (`/scan`, somente leitura) |
| Desfragmentador | Otimização/TRIM do volume |
| PowerCfg | Listagem de planos de energia |
| IPConfig | Informações de rede |
| Limpeza de Disco | Utilitário do Windows |
| Agendador de Tarefas | Consulta de tarefas agendadas |

A saída de cada comando aparece na própria tela. O programa **não** executa comandos
digitados livremente: só os comandos e argumentos que já conhece.

#### Backup e Restauração

Mostra tudo o que o programa alterou no seu computador, com data, o valor anterior e o
valor novo. Cada registro pode ser **desfeito** individualmente. Também é possível criar
um **ponto de restauração** do Windows antes de alterações maiores e exportar a lista de
registros.

Esta é a tela que garante a reversibilidade: se algo não agradar, o caminho de volta está
aqui.

#### Relatórios

Gera relatórios a partir dos dados reais coletados, nos formatos:

- **HTML** — para abrir no navegador.
- **PDF** — para arquivar ou imprimir.
- **JSON** — para uso por outros programas.
- **CSV** — para abrir em planilha.
- **TXT** — texto simples.

Você escolhe o título, o que incluir e onde salvar. Os relatórios ficam também listados
para reabertura.

#### Histórico

Tudo o que o programa fez, com data e hora: limpezas, otimizações, alterações de serviços,
sessões de jogo, diagnósticos e relatórios gerados. Filtre por categoria ou por data e
exporte a lista.

### Sistema

#### Configurações

- **Idioma** — português, inglês ou espanhol.
- **Tema** — escuro ou claro.
- **Agendamento de manutenção** — cria uma tarefa no Agendador de Tarefas do Windows para
  executar a manutenção periodicamente. A tarefa pode ser desativada e removida.
- **Retenção de histórico e logs** — por quanto tempo manter os registros.
- **Dados do aplicativo** — abrir a pasta de dados, exportar ou limpar o histórico.
- **Sobre** — versão, licença e informações do build.

## 5. Privilégios de administrador

O Optimizer PC **inicia sem privilégios administrativos**. Isso é intencional: a maior
parte das funções (diagnóstico, monitoramento, limpeza de temporários do usuário,
relatórios, histórico) não precisa de elevação.

As funções que realmente precisam de permissão elevada mostram um aviso na própria tela,
explicando o motivo. Quando você as usa, o Windows exibe a solicitação de elevação
padrão (UAC). Se você recusar, o programa continua funcionando normalmente e apenas
aquela função fica indisponível.

O programa usa exclusivamente os mecanismos oficiais de elevação do Windows. Não há
instalação de serviço, driver ou qualquer artifício para contornar o UAC.

## 6. Segurança: o que o programa nunca faz

Estas são garantias de projeto, verificadas por testes automatizados:

- **Não apaga arquivos pessoais.** Documentos, fotos, vídeos e arquivos do usuário nunca
  entram na limpeza.
- **Não apaga arquivos críticos do sistema.** `C:\Windows`, `System32`, `SysWOW64`,
  `WinSxS`, `Fonts`, `Boot`, `System32\drivers`, `System32\config` e arquivos de
  inicialização (`bootmgr`, `bcd`, `pagefile.sys`, `swapfile.sys` e afins) são
  protegidos por lista explícita e por validação de caminho.
- **Não desativa o Windows Defender** nem qualquer mecanismo de segurança.
- **Não desativa o Firewall.**
- **Não altera BIOS, UEFI ou firmware.**
- **Não faz overclock** nem altera frequências, tensões ou limites de energia do
  hardware.
- **Não executa scripts ou programas baixados da internet.** O programa não faz conexões
  de rede.
- **Não apaga registro do Windows indiscriminadamente.** As chaves usadas são conhecidas,
  do usuário atual (`HKEY_CURRENT_USER`), com o valor anterior gravado antes da alteração
  e possibilidade de restauração.
- **Não executa comandos perigosos sem confirmação.** Os comandos ficam restritos a uma
  lista de utilitários do Windows com argumentos pré-aprovados, sempre a partir de
  `System32`, sem interpretador de comandos.
- **Não faz overclock, não mexe em BIOS e não mexe em firmware** — repetido de propósito,
  porque é a classe de alteração mais perigosa que existe.

## 7. Privacidade

- O programa funciona **localmente**. Não envia dados do seu computador para nenhum
  servidor — não existe código de rede no aplicativo.
- **Não coleta** senhas, arquivos pessoais, documentos nem histórico de navegação.
- Os dados que ele grava (histórico, logs, relatórios) ficam apenas no seu computador, em
  `%LOCALAPPDATA%\OptimizerPC`.
- Nenhum dado é compartilhado com terceiros, porque nada sai da máquina.

## 8. Onde ficam os seus dados

```
%LOCALAPPDATA%\OptimizerPC\
├── settings.json                    configurações (idioma, tema, preferências)
├── optimizerpc.db                   banco de dados: histórico, registros, relatórios
├── Logs\optimizerpc-AAAAMMDD.log    log de execução, um arquivo por dia
├── Relatorios\                      relatórios gerados
└── Backups\                         estado anterior das alterações, para restauração
```

Para abrir essa pasta rapidamente, use **Configurações → Dados do aplicativo**.

## 9. Perguntas frequentes

**O índice de saúde 92/100 significa que meu PC está 92% bom?**
Ele é uma estimativa interna, calculada a partir dos itens de diagnóstico encontrados e
dos pesos de cada fator. Serve para acompanhar a evolução antes e depois das
otimizações — não é um diagnóstico científico nem um laudo técnico.

**A limpeza vai apagar meus arquivos?**
Não. As categorias de limpeza são apenas de arquivos descartáveis (temporários, caches,
lixeira). Arquivos pessoais não são sequer varridos. E nada é apagado sem você marcar a
categoria e confirmar — a varredura mostra o que seria liberado antes.

**Posso desfazer uma otimização?**
Sim. Toda alteração é registrada em **Backup e Restauração**, com o valor anterior
guardado. Use **Desfazer** no registro correspondente.

**Preciso ser administrador?**
Para usar o programa, não. Algumas funções específicas pedem elevação na hora do uso e
avisam antes. Sem elevação, todo o resto continua funcionando.

**Isso é um antivírus?**
Não. O Optimizer PC não detecta nem remove malware e não substitui o Windows Defender.
Ele cuida de limpeza, desempenho e diagnóstico de configuração.

**O programa mexe na minha BIOS?**
Não, em nenhuma hipótese. Também não faz overclock e não altera firmware.

**Por que a temperatura não aparece no meu computador?**
Porque o firmware do seu equipamento não expõe esse dado de forma padronizada. O
programa mostra a limitação em vez de exibir um número inventado.

**O programa envia alguma informação para a internet?**
Não. Ele não faz nenhuma conexão de rede.

**Posso usar em várias máquinas?**
Sim, incluindo pela versão portátil, que roda direto de um pen drive.

## 10. Desinstalação

**Versão instalada:** use **Aplicativos instalados** (ou **Adicionar ou remover
programas**) do Windows e escolha Optimizer PC, ou o atalho de desinstalação no Menu
Iniciar.

**Versão portátil:** apague a pasta extraída.

Em ambos os casos, os dados em `%LOCALAPPDATA%\OptimizerPC` **permanecem**. Para removê-los
também (isto apaga histórico, logs, relatórios e backups de restauração):

1. Feche o Optimizer PC.
2. Abra o Explorador de Arquivos.
3. Na barra de endereço, digite `%LOCALAPPDATA%\OptimizerPC` e pressione Enter.
4. Volte um nível e apague a pasta `OptimizerPC`.

> **Atenção:** apagar a pasta `Backups` elimina os dados necessários para desfazer
> alterações feitas pelo programa. Se você pretende reverter alguma otimização, desfaça-a
> **antes** de apagar essa pasta.

## 11. Problemas conhecidos e limitações

- **Temperatura e algumas informações de hardware** dependem do firmware da máquina. Sem
  o dado, a tela informa a indisponibilidade em vez de preencher com estimativa.
- **Saúde do disco (SMART)** só aparece quando o controlador expõe o dado.
- **Algumas limpezas e ferramentas** exigem elevação; sem ela, ficam desativadas com o
  motivo visível.
- **Ponto de restauração** depende da Proteção do Sistema estar ativa no Windows. Se
  estiver desativada, o programa avisa e não força a ativação.
- **Criação de tarefa agendada** depende do serviço Agendador de Tarefas estar em
  execução.
- A interface foi desenhada para 1280x720 e se adapta a monitores maiores, incluindo
  ultrawide. Em resoluções menores, use o redimensionamento da janela.
