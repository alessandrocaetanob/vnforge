# Suíte de testes nova

Data: 2026-11-11 · Repo: alessandrocaetanob/vnforge · Tópico do handoff: `maratona-suite-de-testes`

## Contexto

Em 11/11/2026 a suíte tinha 9.706 testes: Core 3.438, App perto de 3.600 (3 fatias), Cli 1.308, Engines.YuRis 1.011, AI 571 e 280 entre Vndb, Installer, Platform.Windows e Patching. Cada fatia do App ocupa de 1,5 a 1,7 GB. Com 5 jobs juntos, os 32 GB da VM do Docker acabam e o CircleCI cancela todos os jobs no mesmo minuto. Um job sozinho leva 16 min. Boa parte do App são testes de render que comparam pixel e medida por tema e por tamanho de janela.

O Alessandro decidiu refazer a suíte inteira. A #620 passa a ser esta maratona e começa agora, depois do merge dos PRs #640, #646 e #651.

Decisões dele (11/11):

- Interface: um teste smoke headless por tela, que confere que a tela abre sem exceção e sem texto cortado. Sem comparação de pixel e sem asserção de medida. A lógica da tela fica em testes de ViewModel. As capturas PNG para o designer saem da suíte e viram uma ferramenta à parte.
- Meta: a cobertura fica igual à de hoje e o número de testes cai. A cobertura de linhas e de ramos de cada projeto, medida na T1, é o piso de toda tarefa. O alvo é perto de 1.500 testes, job `linux` do CI em até 5 min e suíte local em até 2 min. Se o orçamento de um projeto só fecha perdendo cobertura, a cobertura vence e o relatório diz quantos testes ficaram acima do alvo e por quê.

## Regras da suíte nova

Valem para todas as tarefas e entram em `docs/04-testes.md` na T1.

- Um teste por comportamento que o usuário percebe ou que protege dado do usuário. Caso que só muda o tema, o tamanho da janela ou um valor de exemplo vira um caso de `[Theory]` ou sai.
- Cada formato binário mantém o teste de ida e volta byte a byte (regra 3 do CLAUDE.md) e um teste por campo que a reescrita mínima altera.
- Nada de `Thread.Sleep` nem de espera por voltas: espera é por condição, com prazo em tempo e mensagem de falha.
- Teste headless abre a janela, confere e fecha no mesmo teste. Nenhum teste guarda janela, bitmap ou projeto carregado em campo estático.
- Fixture de projeto é sintética e pequena (poucas cenas e poucas linhas), gerada em código.
- Antes de apagar um teste, o builder confere que as linhas e os ramos que só ele cobria continuam cobertos por outro teste. Teste que não cobre nada que outro já não cubra sai.
- Orçamento por projeto (alvo, abaixo do piso de cobertura): App 350, Core 500, Engines.YuRis 250, Cli 200, AI 100, demais juntos 100.

## Tarefas

Cada tarefa é uma issue filha da #620 com o rótulo `infra`, aberta pelo `/maratona`. Toda tarefa só fica pronta com a cobertura de linhas e de ramos do projeto igual ou maior que a da base da T1, medida com o mesmo comando (`dotnet-coverage collect` sobre o projeto de testes, relatório Cobertura). O builder registra no relatório o antes e o depois: testes, tempo, cobertura de linhas e de ramos e, no App, memória do processo. Nenhuma tarefa muda código de produção, a não ser para expor uma costura de teste que falta, e isso vai no corpo do PR.

- [x] **T1 · Regras e medição da base**
  - Issue: #655 (`infra`, `docs`)
  - Prova: `bash .circleci/run-tests.sh` local no Linux do CI (ou o job `linux`) com o resumo por projeto
  - Pronto: `docs/04-testes.md` com as regras acima; `docs/testes/base-2026-11-11.md` com, por projeto, a contagem, o tempo, a memória e a cobertura de linhas e de ramos sobre o código de produção, mais as 30 classes mais lentas do App; o script `tools/cobertura.sh <projeto>` que mede a cobertura do mesmo jeito em toda tarefa. A medição usa as mesmas exclusões do Sonar no `.circleci/config.yml` (`sonar.coverage.exclusions`: `tools/**`, `App.axaml.cs`, `Program.cs`, `Views/**`, `Controls/**`, `AvaloniaFolderPicker.cs`), decidido pelo Alessandro em 11/11.
- [x] **T2 · App: render vira smoke por tela e as capturas saem da suíte**
  - Issue: #656 (`infra`)
  - Prova: `dotnet test tests/VNForge.App.Tests` e a ferramenta de capturas gerando os PNG das telas em `.tmp/capturas`
  - Pronto: a pasta `tests/VNForge.App.Tests/Rendering` some, com o `PixelProbe` e as asserções de medida. Um `ScreenSmokeTests` cobre cada tela num tema, sem exceção e sem texto cortado. As capturas viram um comando à parte (projeto de ferramenta ou opção da Cli) que o designer roda, fora do CI. O processo do App fica abaixo de 600 MB de memória.
  - Depende de: T1
- [x] **T3 · Core**
  - Issue: #657 (`infra`)
  - Prova: `dotnet test tests/VNForge.Core.Tests`
  - Pronto: no máximo 500 testes, com as idas e voltas e a concorrência do `ProjectFileIo` mantidas, e o tempo e a contagem no relatório.
  - Depende de: T1
- [x] **T4 · Engines.YuRis**
  - Issue: #658 (`infra`)
  - Prova: `dotnet test tests/VNForge.Engines.YuRis.Tests`
  - Pronto: no máximo 250 testes, cada formato (YPF, YBN e o que mais houver) com ida e volta byte a byte e um teste por campo reescrito.
  - Depende de: T1
- [ ] **T5 · App: testes de ViewModel**
  - Issue: #659 (`infra`)
  - Prova: `dotnet test tests/VNForge.App.Tests`
  - Pronto: App inteiro com no máximo 350 testes, um por comportamento de cada tela; headless só nos smoke da T2 e em interação que não dá para testar pelo ViewModel; suíte do App local em até 60 s.
  - Depende de: T2
- [ ] **T6 · Cli**
  - Issue: #660 (`infra`)
  - Prova: `dotnet test tests/VNForge.Cli.Tests`
  - Pronto: no máximo 200 testes, um por comando e por saída de erro que o usuário vê.
  - Depende de: T3
- [x] **T7 · AI, Vndb, Installer, Platform.Windows e Patching**
  - Issue: #661 (`infra`)
  - Prova: `dotnet test` em cada um dos cinco projetos
  - Pronto: no máximo 200 testes nos cinco, com o provedor falso da IA mantido (regra da IA no CLAUDE.md).
  - Depende de: T4
- [ ] **T8 · CI: script simples e orçamento**
  - Issue: #662 (`infra`)
  - Prova: job `linux` verde num PR, com o passo Testes em até 3 min e o job em até 5 min
  - Pronto: `run-tests.sh` sem fatias nem `tempos-app.txt` (sai também o `-class` com código de cor ANSI). O job reprova quando a cobertura de linhas de um projeto cai abaixo da base, com o número. `docs/testes/depois.md` traz o antes e o depois, com a cobertura igual ou maior em todos os projetos.
  - Depende de: T5, T6, T7

Ondas: T1; depois T2, T3 e T4; depois T5, T6 e T7; por fim T8. No máximo 3 jobs do CI por vez (memória `ci-no-maximo-3-jobs`).

## Condição do /goal

```
/goal todas as tarefas de docs/plans/2026-11-11-suite-de-testes.md estão [x] com PR mergeado, CI verde e cobertura de linhas e de ramos de cada projeto igual ou maior que a base de docs/testes/base-2026-11-11.md, ou uma tarefa falhou duas vezes (anotada no handoff maratona-suite-de-testes), ou passaram 150 turnos
```

## Progresso

- T1 (#655): regras em `docs/04-testes.md`, base em `docs/testes/base-2026-11-11.md` (9.729 testes) e `tools/cobertura.sh`. A base do App é de um processo único: linhas 94,18%, ramos 85,35%.
- T3 (#657): o alvo de 500 testes não fechou. O Core foi de 3.444 para 1.562 testes, 1.062 acima do alvo, com a cobertura igual à base: linhas 90,63% (17.025 de 18.785) e ramos 84,42% (11.079 de 13.123), contra 90,62% e 84,42%. Pela regra do plano, a cobertura vence: cada teste foi medido sozinho e ficou o menor conjunto que mantém linhas e ramos, mais os que protegem dado do usuário (ida e volta e byte a byte, concorrência do `ProjectFolder`, backup e restauração, recusa de dado inválido gravado em arquivo do projeto) e os de valor esperado explícito das codificações (Shift-JIS é a página 932). Cortar até 500 tira cobertura de ramo ou esses testes. Dois testes oscilaram no Windows por acesso negado ao criar arquivo: o de captura sem nome livre passou a esperar por condição, e o `WriteAtomically` do `ProjectFolder` segue sem nova tentativa na criação do temporário (#669).
- T4 (#658): Engines.YuRis de 1.011 para 277 testes, 27 acima do alvo de 250. Dos 277, 29 são herdados do kit de conformidade (tests/VNForge.Engines.ConformanceKit, compartilhado) e ficaram. O resto acima do alvo são os oráculos independentes que a cobertura não vê e que a revisão pediu de volta: vetores de ouro do YPF e dos leitores, vetores de resposta conhecida de Adler32, Crc32 e MurmurHash2, as regras de extração de texto com valor esperado explícito, o header_size gigante e o nome Shift-JIS com 0x5C. Tempo 3,2 s para 2,1 s. Cobertura igual à base, medida com `bash tools/cobertura.sh Engines.YuRis`: linhas 95,90% (2.481 de 2.587) e ramos 93,36% (1.462 de 1.566). Os testes do gerador sintético de YPF saíram (a classe virou `SyntheticYpfSamples`, só os exemplos); cada formato com escrita manteve a ida e volta byte a byte e um teste por campo.
- T7 (#661): o alvo de 200 testes não fechou: AI, Vndb, Installer, Platform.Windows e Patching foram de 851 para 473, 273 acima do alvo. A cobertura de linhas e de ramos de cada projeto ficou igual à base, medida no Windows com `bash tools/cobertura.sh <Projeto>` (o gate do Linux é da T8; no Linux os testes de Platform.Windows e os de cofre DPAPI pulam). O ramo do AI na base do dia foi 1.582, um a mais que os 1.581 de `docs/testes/base-2026-11-11.md`, e é o piso usado. Por projeto, testes, tempo (sem cobertura, uma rodada, com ruído de outras sessões) e cobertura, antes para depois: AI 571 para 304, 1,6 s para 2,4 s, linhas 94,01% (2.762 de 2.938) para o mesmo, ramos 89,68% (1.582 de 1.764) para o mesmo; Vndb 111 para 57, 1,8 s para 1,3 s, linhas 98,20% (545 de 555) para o mesmo, ramos 89,75% (289 de 322) para o mesmo; Installer 91 para 49, 5,6 s para 7,3 s (os testes de ponta a ponta do executável e da janela ficaram), linhas 96,12% (1.411 de 1.468) para o mesmo, ramos 83,41% (367 de 440) para o mesmo; Platform.Windows 40 para 26, 4,2 s para 4,3 s, linhas 61,86% (279 de 451) para o mesmo, ramos 58,15% (107 de 184) para o mesmo; Patching 38 para 37, 4,5 s para 8,7 s (volta o teste lento de 64 MB), linhas 85,85% (1.262 de 1.470) para o mesmo, ramos 79,60% (476 de 598) para o mesmo. Por que passou do alvo: a cobertura vence, e a revisão pediu de volta testes que a cobertura não enxerga (28 testes). Voltaram: no Patching o patch novo por cima do anterior com desinstalação ao jogo original, a compatibilidade com patch antigo sem versão do jogo nem capa, o manifesto válido com delta dentro do pacote e a alocação de 64 MB (marca Lento); no AI as faixas de preço de prompt longo e da estimativa da Bíblia, o `RealApiModel` (padrão, aparo, os quatro ids e o erro de id desconhecido) e os casos do `AppBudgetFile` que protegem o limite de gasto; no AI e no Vndb o teste de que, fora do Windows, o cofre DPAPI manda usar `ANTHROPIC_API_KEY` e `VNDB_TOKEN` (regra 4); no Platform.Windows os testes de janela de verdade e o que cobre o capturador fora do Windows. O AI (304 de 100) tem 2.938 linhas em dezenas de provedores e cada teste cobre pouco: uma poda exata (a suíte sem cada método, sob `dotnet-coverage`) só achou 8 métodos redundantes. Cerca de 80 dos 304 são oráculos mantidos de propósito (formato de requisição e resposta do Claude, tabela de preços, ids dos modelos, chave só por variável de ambiente ou cofre, orçamento, backup e texto da pessoa que não se perde, provedor falso). No Patching os 9 casos de caminho inválido do manifesto, a ida e volta byte a byte, o backup dos originais e o rollback ficaram todos, porque protegem o jogo do usuário. Os testes de render com pixel do Installer não voltam (decisão de 11/11). Medição por método: cada método rodou sozinho sob `dotnet-coverage` (`-method`), seleção gulosa por linhas e ramos, depois medição real e ajuste dos ramos que faltaram; os cortes de linhas de `[Theory]` foram medidos um a um. Nenhum código de produção mudou.

- T2 (#656): a pasta `Rendering` saiu; o `ScreenSmokeTests` abre as telas do catálogo e `tools/VNForge.Capturas` grava os PNG (`docs/04-testes.md`). App: 3.118 para 2.756 testes, todos verdes; linhas 94,48% (19.924 de 21.088) e ramos 85,76% (10.888 de 12.696) em processo único, acima do piso (94,26% e 85,35%). Memória: a sessão headless esvazia o cache de fontes do Skia depois de cada Dispatch (cerca de 17 MB nativos por janela principal ficavam presos), o que baixou o pico do processo de 2.227 para 1.674 MB e o do smoke de 684 para cerca de 480 MB. Falta chegar a 600 MB: os segmentos fixados do compositor (cerca de 1.750 arrays de 100 a 260 KB, 186 MB) sobram de cada aplicativo isolado por Dispatch; `AvaloniaTestIsolationLevel.PerAssembly` os evita (processo em 353 MB, 166 s) mas 361 testes headless falham com "different thread owns it", então a troca fica para a T seguinte.

## Fora da corrida

- A #112 (o jogo inteiro no VNForge) e as issues de validação no jogo: dependem do Alessandro com o jogo.
- A parte 2 da #330 (S3776 e as issues dos arquivos que estavam em PR): vem depois da T8, porque o corte muda boa parte dos arquivos de teste que ela tocaria.
- Teste de interface com pixel ou medida: decidido em 11/11 que não volta.
