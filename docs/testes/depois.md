# A suíte depois da maratona, 12/11/2026

Fecha a maratona da #620 (tarefa T8, #662). Compara a base de [base-2026-11-11.md](base-2026-11-11.md) com a suíte depois das tarefas T2 a T7, e traz o piso de cobertura que o job `linux` passa a conferir. Como medir: [04-testes.md](../04-testes.md).

## Resultado

A suíte foi de 9.729 para 4.212 testes. A cobertura de linhas e de ramos de cada projeto ficou igual ou maior que a base. O alvo de cerca de 1.500 testes não fechou: pela regra do plano a cobertura vence, e cada tarefa registrou quanto passou do alvo e por quê. O job `linux` caiu de 24,5 min (na main, antes da T2) para menos de 5 min, e o passo Testes de uns 18 min para 2 min 12 s em contêiner com os limites do job.

## Por projeto, no Windows

Cobertura de `tools/cobertura.sh` (só os testes do próprio projeto, com as exclusões do Sonar), a mesma da base. O tempo é de um processo por projeto, sem cobertura, um de cada vez.

| Projeto | Testes antes | Testes depois | Tempo antes | Tempo depois | Linhas antes | Linhas depois | Ramos antes | Ramos depois |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Core | 3.441 | 1.562 | 7,4 s | 4,0 s | 90,62% | 90,63% (17.025 de 18.785) | 84,42% | 84,42% (11.079 de 13.123) |
| App | 3.118 | 1.334 | 1.023 s em 3 fatias | 133 s em 1 processo | 94,18% | 94,46% (19.926 de 21.094) | 85,35% | 85,47% (10.851 de 12.696) |
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

Medido duas vezes num contêiner `mcr.microsoft.com/dotnet/sdk:10.0` com 4 CPUs e 6 GB (os limites do job `linux`), com `dotnet-coverage collect "bash .circleci/run-tests.sh"` e `bash .circleci/piso-cobertura.sh`:

| Projeto | Linhas no Linux (2 rodadas) | Piso | Linhas no Windows, mesma medida |
|---|---:|---:|---:|
| AI | 96,49% (2.835 de 2.938) | 96,49% | 96,83% |
| App | 95,17% e 95,19% (19.849 e 19.853 de 20.855) | 95,15% | 95,20% |
| Cli | 95,29% (6.941 de 7.284) | 95,29% | 95,29% |
| Core | 96,93% (14.274 de 14.725) | 96,93% | 96,96% |
| Engines.YuRis | 96,09% (2.486 de 2.587) | 96,09% | 96,28% |
| Installer | 96,44% (1.411 de 1.463) | 96,44% | 96,51% |
| Patching | 88,95% (475 de 534) | 88,95% | 90,07% |
| Platform.Windows | 21,03% (65 de 309) | 21,03% | 66,34% |
| Vndb | 96,39% (535 de 555) | 96,39% | 98,37% |

No Linux os testes que só valem no Windows pulam (11 dos 26 do Platform.Windows, 1 do Vndb e os de cofre DPAPI do AI), e a diferença da última coluna é isso: o Platform.Windows cai de 66,34% para 21,03%. O App variou 4 linhas entre as duas rodadas; o piso dele deixa 0,02 ponto de margem.

## Tempo do job linux

| | Antes (main, antes da T2) | PR #677 (T5) | Contêiner com os limites do job, após a T8 |
|---|---:|---:|---:|
| Job | 24,5 min | 4 min 56 s | |
| Build | | 1 min 45 s | 80 s |
| Testes | uns 18 min (App em 3 fatias) | 1 min 23 s | 132 s |
| Sonar end | | 57 s | |

O passo Testes ficou em 2 min 12 s com o App inteiro num processo (130 s), os outros oito projetos em paralelo ao lado dele e 4.212 testes, todos verdes. O `.circleci/run-tests.sh` sem fatias, sem `tempos-app.txt`, sem `-class` e sem `VNFORGE_RENDER_DIR` tem 109 linhas, contra 327.
