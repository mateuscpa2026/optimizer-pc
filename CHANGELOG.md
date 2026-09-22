# Changelog

Todas as mudanças relevantes do Optimizer PC. O formato segue
[Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/); a versão é controlada
manualmente e não é incrementada automaticamente pelo build.

## [1.0] — 2026-09-22

Primeira versão.

### Adicionado

**Aplicativo**

- 16 telas: Dashboard, Diagnóstico, Otimização, Limpeza, Inicialização, Processos,
  Serviços, Desempenho, Armazenamento, PC Fraco, Gamer, Ferramentas, Backup e
  Restauração, Relatórios, Histórico e Configurações.
- Assistente de primeira execução em 5 etapas, com leitura inicial do hardware e
  encerramento em "Seu PC está pronto para análise.".
- Tema escuro e claro, trocáveis em tempo de execução sem reiniciar.
- Interface em português, inglês e espanhol, com troca de idioma em tempo de execução.
- Shell com navegação lateral em 6 seções, responsivo de 1280x720 a ultrawide.
- Controles próprios: medidor em arco com animação, cartões e gráficos.
- Ícone da bandeja com notificações.

**Diagnóstico e monitoramento**

- Diagnóstico de SO, processador, memória, volumes, discos (SMART), temperatura,
  inicialização, serviços, processos, espaço recuperável, lixeira, reinicialização
  pendente, atualizações, armazenamento, Secure Boot e SmartScreen.
- Índice de saúde de 0 a 100 com fatores ponderados, rotulado como estimativa interna.
- Monitoramento em tempo real de processador, memória, disco e rede.
- Listagem de processos e serviços com marcação do que é crítico e protegido.

**Limpeza**

- Varredura de 14 categorias de arquivos descartáveis com espaço potencialmente
  recuperável calculado **antes** da exclusão.
- Fluxo em duas etapas (varrer → selecionar → confirmar → limpar), com progresso e
  cancelamento.

**Otimização**

- Motor de recomendações com motivo e ação sugerida por item.
- Ações reversíveis: plano de energia, efeitos visuais, inicialização, serviços,
  integridade do volume e perfil visual de alto desempenho.
- Perfil **PC Fraco** com avaliação do equipamento e plano compatível.
- Perfil **Gamer** com sessão de jogo que aplica alterações e as reverte ao encerrar.

**Armazenamento**

- Análise de ocupação por pasta, arquivos grandes e duplicados por **conteúdo** (hash).

**Ferramentas**

- Atalhos para SFC, DISM, CHKDSK, Desfragmentador, PowerCfg, IPConfig, Limpeza de Disco e
  Agendador de Tarefas, executados a partir de `System32` com argumentos pré-aprovados.
- Agendamento de manutenção periódica no Agendador de Tarefas do Windows.

**Relatórios e histórico**

- Relatórios em HTML, PDF, JSON, CSV e TXT.
- Histórico completo com filtro por categoria e data, e exportação.
- Backup e Restauração com registro de toda alteração, valor anterior guardado e desfazer
  individual; criação de ponto de restauração do Windows quando a Proteção do Sistema
  permite.

**Segurança**

- `ProtectedPaths` bloqueia `C:\Windows`, `System32`, `SysWOW64`, `WinSxS`, `drivers`,
  `config`, `Boot`, `Fonts`, `assembly`, `Microsoft.NET`, `servicing` e arquivos de
  inicialização (`bootmgr`, `bcd`, `pagefile.sys`, `swapfile.sys` e afins).
- `SafePathValidator` canonicaliza o caminho antes de qualquer comparação, fechando
  escapes por `..`, links simbólicos, junções e nomes curtos 8.3.
- `CommandAllowList` restringe a execução a oito utilitários do Windows, sempre de
  `System32`, com regras de validação por argumento e sem interpretador de comandos.
- Elevação apenas pelos mecanismos oficiais do Windows; o programa inicia sem privilégios
  e continua utilizável quando a elevação é recusada.
- Nenhum acesso de rede: o aplicativo não contém cliente HTTP nem qualquer código de
  comunicação externa.

**Persistência e infraestrutura**

- Banco SQLite com as tabelas `History`, `Logs`, `RestoreRecords` e `Reports`, com
  comandos parametrizados.
- Configurações em JSON.
- Log diário em arquivo (`Logs\optimizerpc-AAAAMMDD.log`) e registro no banco, com falhas
  não tratadas registradas na categoria `Crash`.
- Publicação `self-contained` `win-x64`, que dispensa o .NET na máquina do usuário.
- Instalador (Inno Setup 6) e pacote portátil.
- Scripts `tools\Build-Release.ps1` (gera todos os entregáveis) e
  `tools\Generate-Assets.ps1` (regenera ícone e logotipos).

**Testes**

- 275 testes automatizados, incluindo um conjunto dedicado a segurança, que falha se o
  programa passar a aceitar caminhos críticos, arquivos pessoais, comandos arbitrários ou
  alterações em Defender, Firewall, BIOS ou firmware.

### Corrigido

- **Crash ao abrir telas com barra de progresso.** `ProgressBar.Value` é um vínculo de
  duas vias por padrão no WPF; ligado a propriedades com setter privado
  (`ProgressPercent`, `UsedPercent`, `SharePercent`), causava
  `InvalidOperationException: A TwoWay or OneWayToSource binding cannot work on the
  read-only property` e derrubava a tela. Todos os 20 vínculos desse tipo passaram a
  declarar `Mode=OneWay`, e um teste automatizado impede a reintrodução do problema.

### Limitações conhecidas

- Temperatura, saúde do disco (SMART), Secure Boot e algumas informações de firmware só
  aparecem quando o firmware do equipamento expõe o dado. Sem ele, a interface informa a
  indisponibilidade em vez de estimar.
- Funções que exigem privilégio elevado ficam desativadas com o motivo visível quando a
  elevação é recusada ou indisponível.
- Ponto de restauração depende da Proteção do Sistema estar ativa.
- A criação de tarefa agendada depende do serviço Agendador de Tarefas estar em execução.
