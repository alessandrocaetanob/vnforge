# A suíte depois da maratona, 12/11/2026

Fecha a maratona da #620 (tarefa T8, #662). Compara a base de [base-2026-11-11.md](base-2026-11-11.md) com a suíte depois das tarefas T2 a T7, e traz o piso de cobertura que o job `linux` passa a conferir. Como medir: [04-testes.md](../04-testes.md).

## Resultado

A suíte foi de 9.729 para 4.212 testes. A cobertura de linhas e de ramos de cada projeto ficou igual ou maior que a base. O alvo de cerca de 1.500 testes não fechou: pela regra do plano a cobertura vence, e cada tarefa registrou quanto passou do alvo e por quê. O job `linux` caiu de 24,5 min (na main, antes da T2) para 5 min 54 s no runner, e o passo Testes de uns 18 min para 2 min 15 s. A prova pedia o job em até 5 min: a meta só fecha com o tempo do App abaixo (ver "Tempo do job linux").

## Por projeto, no Windows

Cobertura de `tools/cobertura.sh` (só os testes do próprio projeto, com as exclusões do Sonar), a mesma da base. O tempo é de um processo por projeto, sem cobertura, um de cada vez.

| Projeto | Testes antes | Testes depois | Tempo antes | Tempo depois | Linhas antes | Linhas depois | Ramos antes | Ramos depois |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Core | 3.441 | 1.562 | 7,4 s | 4,0 s | 90,62% | 90,63% (17.025 de 18.785) | 84,42% | 84,42% (11.079 de 13.123) |
| App | 3.118 | 1.334 | 1.023 s em 3 fatias | 80 a 86 s em 1 processo, 48 e 45 s em 2 | 94,18% | 94,46% (19.926 de 21.094) | 85,35% | 85,47% (10.851 de 12.696) |
| Cli | 1.308 | 566 | 15,0 s | 8,4 s | 93,84% | 93,84% (7.444 de 7.933) | 80,68% | 80,68% (3.202 de 3.969) |
| Engines.YuRis | 1.011 | 277 | 3,2 s | 2,1 s | 95,90% | 95,90% (2.481 de 2.587) | 93,36% | 93,36% (1.462 de 1.566) |
| AI | 571 | 304 | 1,6 s | 2,4 s | 94,01% | 94,01% (2.762 de 2.938) | 89,63% | 89,68% (1.582 de 1.764) |
| Vndb | 111 | 57 | 1,8 s | 1,3 s | 98,20% | 98,20% (545 de 555) | 89,75% | 89,75% (289 de 322) |
| Installer | 91 | 49 | 5,6 s | 7,3 s | 96,12% | 96,12% (1.411 de 1.468) | 83,41% | 83,41% (367 de 440) |
| Platform.Windows | 40 | 26 | 4,2 s | 4,3 s | 61,86% | 61,86% (279 de 451) | 58,15% | 58,15% (107 de 184) |
| Patching | 38 | 37 | 4,5 s | 8,7 s | 85,85% | 85,85% (1.262 de 1.470) | 79,60% | 79,60% (476 de 598) |
| Total | 9.729 | 4.212 | | | | | | |

Os números de depois são os que cada tarefa registrou em `docs/plans/2026-11-11-suite-de-testes.md` (T3 a T7), exceto o tempo do Core, medido de novo em 12/11 com `tools/medir-testes.ps1` (336 MB de pico). O AI tem um ramo a mais que a base (1.582 contra 1.581, base do dia da T7). A base do App é de um processo único com a suíte de antes da T2. Os projetos que ficaram mais lentos (AI, Installer, Patching) mantiveram testes que a cobertura não enxerga: ponta a ponta do instalador, alocação de 64 MB no Patching, oráculos de preço e formato de requisição no AI.

## Por projeto, no Linux (o piso do CI)

O passo Piso de cobertura mede o `coverage.xml` do `dotnet-coverage`, que junta os testes de todos os projetos, e conta só os arquivos de `src/VNForge.<projeto>/` que o Sonar considera (fora o `sonar.coverage.exclusions` e o código gerado em `obj/`). É outra medida que a da base: o Core, por exemplo, ganha cobertura dos testes do App e da Cli, e o código gerado do `System.Text.Json` não entra, então o total de linhas é menor. Por isso o piso do Linux não se compara com a base do Windows, e a prova de que a cobertura não caiu é a tabela acima.

Medido em duas rodadas num contêiner `mcr.microsoft.com/dotnet/sdk:10.0` com 4 CPUs e 6 GB (os limites do job) e numa rodada do job `linux` no runner (job `ccac61e3`, PR #680):

| Projeto | Contêiner (2 rodadas) | Runner | Piso | Windows, mesma medida |
|---|---:|---:|---:|---:|
| AI | 96,49% | 96,49% | 96,49% | 96,83% |
| App | 95,17% e 95,19% | 95,19% | 95,09% | 95,20% |
| Cli | 95,29% | 95,29% | 95,19% | 95,29% |
| Core | 96,93% | 96,97% | 96,87% | 96,96% |
| Engines.YuRis | 96,09% | 96,09% | 96,09% | 96,28% |
| Installer | 96,44% | 96,44% | 96,44% | 96,51% |
| Patching | 88,95% | 88,95% | 88,95% | 90,07% |
| Platform.Windows | 21,03% | 21,03% | 21,03% | 66,34% |
| Vndb | 96,39% | 96,39% | 96,39% | 98,37% |

O piso de App, Core e Cli fica 0,1 ponto abaixo do medido no runner, porque a cobertura deles varia algumas linhas entre rodadas (o App deu 4 linhas de diferença entre duas rodadas do contêiner, e o Core 96,93% no contêiner contra 96,97% no runner). Os outros seis estão exatamente no valor medido. No Linux os testes que só valem no Windows pulam (11 dos 26 do Platform.Windows, 1 do Vndb e os de cofre DPAPI do AI), e a diferença da última coluna é isso: o Platform.Windows cai de 66,34% para 21,03%.

## Tempo do job linux

| | Antes (main, antes da T2) | PR #677 (T5) | PR #680, runner, ccac61e3 | PR #680, ff382b2b | PR #680 final, cfd529dc |
|---|---:|---:|---:|---:|---:|
| Job | 24,5 min | 4 min 56 s | 5 min 54 s | 5 min 30 s | 5 min 13 s |
| Checkout | | | 10 s | | 11 s |
| Sonar begin | | | 27 s | 27 s | 38 s |
| Build | | 1 min 45 s | 1 min 44 s | 1 min 53 s | 1 min 57 s |
| Testes | uns 18 min (App em 3 fatias) | 1 min 23 s | 2 min 15 s | 1 min 47 s | 1 min 10 s |
| Piso de cobertura | | | 2 s | | 2 s |
| Sonar end | | 57 s | 1 min 7 s | 1 min 0 s | 1 min 4 s |

No PR #680 o App inteiro, num processo, levou 131 s e é o caminho crítico; Core e Cli levaram 15 s cada. O `.circleci/run-tests.sh` sem fatias, sem `tempos-app.txt`, sem `-class` e sem `VNFORGE_RENDER_DIR` tem 109 linhas, contra 327.

Para baixar o App, o `BuildTestViewModelTests` (41 s em 18 testes no Windows) passou a gravar só os 16 scripts que a tela Build usa, com 20 linhas cada, em vez dos 312 com 48 mil linhas de `DemoProject`: cada teste gravava e relia a pasta inteira. A classe foi de 41 para 10 s e o App, no Windows, de 108 s para 80 a 86 s (sem cobertura, mesma máquina), com 1.334 testes e a cobertura do App em processo único com linhas 94,45% (19.924 de 21.094) e ramos 85,50% (10.855 de 12.696), acima do piso do plano (94,26% e 85,35%). Cortar também o `ScreenSmokeTests` (28 a 35 s, 40 janelas) para 40 linhas por capítulo ganhava 5 s mas custava 27 linhas e 0,26 ponto de ramos do App, e foi desfeito.

O ff382b2b (PR #680) trouxe o passo Testes para 1 min 47 s com o `BuildTestViewModelTests` enxuto, mas o job ficou em 5 min 30 s e o App, em série num processo, seguia como caminho crítico. Depois dele o App passou a rodar em dois processos com `-trait Tipo=Tela` e `-trait- Tipo=Tela` (`ScreenSmokeTests`, `ClippedTextHeadlessTests` e `ReviewModeKeyTests` de um lado, 53 testes; os outros 1.281 do outro). No Windows, sem cobertura e um de cada vez: 48 s e 45 s, contra 80 s do processo único. Com o `dotnet-coverage` em volta de todo o script, os 10 processos juntos (4 por vez) levam 67 s, contra 139 s antes, com os mesmos 4.212 testes; a cobertura de linhas do App no `coverage.xml` fica em 95,20% e a de `bash tools/cobertura.sh App` (processo único, todos os testes) em 94,45% de linhas e 85,48% de ramos.

Rodada final, sozinha no runner (job `cfd529dc`, commit d22a8a37, com a main depois do #682 e do #683): o passo Testes levou 1 min 10 s, dentro do alvo de 3 min. O job levou 5 min 13 s e não fechou o alvo de 5 min por 13 s: só os passos fixos (checkout, Sonar begin com a instalação das ferramentas, build `--no-incremental`, envio dos artefatos e Sonar end) somam cerca de 4 min, e variam de 20 a 30 s entre rodadas. Baixar esses passos não é da suíte de testes. Na mesma rodada o piso passou em todos os projetos; o do Platform.Windows desceu de 21,03 para 20,90, porque a refatoração do #682 criou duas linhas em código que só roda no Windows. O #683 acrescentou 7 testes no AI e 16 no Patching para cobrir comportamento que nunca teve teste, depois que as extrações do #682 tiraram linhas cobertas do total.
