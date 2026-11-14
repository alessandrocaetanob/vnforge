# Testes

Regras da suíte de testes e o jeito de medi-la. A fonte das regras é o [plano de testes por risco](plans/2026-11-14-plano-de-testes.md), aprovado em 14/11/2026, que substitui as regras da maratona da #620. A medição de partida da maratona está em [testes/base-2026-11-11.md](testes/base-2026-11-11.md).

## O que um teste protege

Todo teste aponta um risco da tabela abaixo. A pergunta não é "que linha falta cobrir", é "que dano este teste evita". Teste que não aponta nenhum risco não entra, e o que já existe sem risco sai no corte.

| Id | Risco | Onde já aconteceu | Como se testa |
|---|---|---|---|
| R1 | Estragar a pasta do jogo: injeção sem backup, restauração que não volta ao original | #452 (testar no jogo por cima de uma injeção) | Cenário de ponta a ponta no jogo sintético; backup e restauração byte a byte |
| R2 | Perder trabalho do usuário ao gravar o projeto | #697 (36 arquivos com 0 byte), #669 e #678 (temporário com acesso negado) | Gravação conferida por releitura; falha de disco simulada; gravação concorrente |
| R3 | Gerar arquivo que o jogo não lê | Regra 3 do `CLAUDE.md` | Ida e volta byte a byte de cada formato; vetores de ouro; um teste por campo reescrito |
| R4 | Texto errado dentro do jogo: codificação, quebra, caixa estourada, falante | #434, #435, #448 (cp1252 e falante) | Tabela de casos com valor esperado explícito por regra de codificação e de quebra |
| R5 | Gastar dinheiro com IA sem querer, ou vazar a chave | Regra 4 do `CLAUDE.md` | Chave só por ambiente ou cofre; limite de gasto recusa antes de chamar; preço por modelo; ids dos modelos |
| R6 | Fluxo principal que trava o usuário | #688 (não liga projeto existente), #619 (erro ao abrir projeto), #689 (jogo mudou de pasta) | Uma jornada por fluxo principal, pelo ViewModel |
| R7 | Arquivo externo estragado ou serviço fora do ar apaga ou esconde dado | #652 (pasta de projeto ilegível escondia os outros) | Arquivo ilegível e rede fora: recusa com aviso em português, nada é sobrescrito |
| R8 | Tela diferente do quadro aprovado | #580 (reprovada) | Não é teste: render real ao lado do quadro antes do PR. Na suíte, só o smoke por tela (abre sem exceção e sem texto cortado, numa largura só: 1440, a do canvas do design; as outras larguras se conferem no render). Os testes smoke por tela são os únicos que apontam R8; nenhum outro teste aponta R8, e a fidelidade ao quadro se prova pelo render, fora da suíte |
| R9 | Conteúdo adulto aparece quando a pessoa escolheu esconder ou desfocar | Decisão de 14/11, sem bug registrado | A escolha (mostrar, desfocar, esconder) é gravada e respeitada; no modo Desfocar o cartão fica borrado até o Mostrar e volta a borrar com o Ocultar; arquivo de configuração travado mantém o modo gravado; troca feita pelo CLI chega à grade; imagem adulta já na tela some ao desligar o ajuste, inclusive com o download em andamento; só o modo Mostrar põe o jogo adulto no catálogo exportado |

## Camadas da suíte

**Oráculos (R3, R4).** Vetores de ouro do YPF e dos hashes, Shift-JIS na página 932, o 0x5C no nome, cp1252 com « », ida e volta byte a byte de cada formato. Ficam sempre, mesmo sem ganho de cobertura: a maratona da #620 mostrou que o corte por cobertura apaga esses testes se ninguém os listar.

**Gravação com falha de disco (R1, R2, R7). #699.** A costura é o `IProjectDisk` (`ProjectDisk.Use` no teste): troca do arquivo, escrita do temporário e cópia. O teste a troca por uma versão que falha do jeito que o mundo real falha: rename que devolve sucesso e deixa o destino vazio (o caso do rclone), arquivo travado por outro processo, disco cheio no meio da gravação, pasta somente leitura. Cada caminho que grava projeto, manifesto, backup ou biblioteca roda contra essas falhas. O esperado é erro em português e o arquivo anterior intacto, nunca sucesso com dado perdido. `DiskFailureTests` roda cada tipo de falha contra projeto, biblioteca, manifesto, backup e pasta do jogo; um teste de guarda (`R2_nenhum_caminho_do_core_grava...`) falha se um arquivo do Core grava sem passar por `AtomicFile`, a não ser que esteja na lista com o motivo. A conferência por releitura veio na #697.

**Cenários de ponta a ponta (R1 a R4). `CenarioDePontaAPontaTests`, #700.** Cinco testes no Cli (`dotnet test tests/VNForge.Cli.Tests --filter Cenario`), sobre um jogo YU-RIS sintético gerado em código, rodando os comandos do CLI com o provedor falso:

1. Extrair, traduzir com o provedor falso, injetar, restaurar: o jogo volta byte a byte ao original.
2. Injetar por cima de uma injeção anterior e restaurar: volta ao original, não à primeira injeção.
3. Mover o jogo de pasta e ligar o projeto (#689): a tradução continua aplicável e a restauração acha o backup.
4. Texto com os casos difíceis de codificação e de caixa (acento antes de código de controle, falante com « », linha que estoura a caixa): o que sai no YBN bate com o valor esperado.
5. Projeto gravado sobre o sistema de arquivos com falha: o comando sai com erro e nada fica com 0 byte.

**Jornadas do App (R6).** Um teste por fluxo que a pessoa faz de ponta a ponta, pelo ViewModel, sem janela: abrir o app pela primeira vez, adicionar pasta à Biblioteca, criar projeto a partir de um jogo, ligar jogo a projeto existente, abrir a Ficha sem projeto, traduzir uma cena, revisar e aprovar, aplicar no jogo, restaurar o original, pausar e retomar a tradução noturna. Cada jornada confere o resultado que importa (arquivo gravado, projeto aberto, jogo alterado), não o texto dos botões no caminho.

**Smoke por tela (R8).** O `ScreenSmokeTests` abre cada tela do catálogo e confere que não há exceção nem texto cortado. Roda numa largura só (1440); as outras se conferem no render ao lado do quadro. Sem pixel, sem medida e sem texto exato. A lógica da tela fica em teste de ViewModel.

**Teste no ambiente real (manual, antes de cada pré-release).** O Alessandro roda um roteiro curto, porque é onde os testes automáticos não chegam: projeto no H: e no W: (rclone), caminho com japonês, jogo real aberto depois da injeção. O roteiro está em [`docs/testes/roteiro-ambiente-real.md`](testes/roteiro-ambiente-real.md) (#703).

## O que sai e o que fica

Saem:

- Teste que não aponta nenhum risco da tabela.
- Teste de apresentação: texto exato, visibilidade de botão, cor, ordem de chips, conteúdo de cabeçalho. A tela se confere pelo render ao lado do quadro.
- Teste de getter, de construtor, de texto de ajuda e de contrato repetido por comando.
- Variação de valor, tema ou tamanho que não seja caso de `[Theory]`.

Ficam sempre, mesmo sem ganho de cobertura: os oráculos, os testes que protegem dado do usuário (R1, R2, R7), os de chave e gasto (R5). Teste que só roda numa plataforma fica quando aponta um risco; rodar numa plataforma só não o mantém.

A cobertura é medida e aparece no relatório, mas é número informativo. Nenhum job reprova por ela e ela não impede o corte de um teste. Antes de apagar um teste, confira que o risco que só ele protegia continua protegido e liste os oráculos que ficaram, por nome.

## Regras para issue nova

- Correção de bug: um teste de regressão só quando o bug cai em R1 a R7. Ele falha antes da correção e passa depois.
- Funcionalidade nova: os testes são as jornadas que ela cria, em geral de 1 a 3.
- Mudança de tela (rótulo `interface`): nenhum teste novo. A prova é o render real ao lado do quadro.
- Todo brief de builder traz `Testes: até N` e o risco de cada teste. O revisor recusa teste sem risco.
- Nada de `Thread.Sleep` nem de espera por voltas: a espera é por condição, com prazo em tempo e mensagem de falha.
- Nenhum teste guarda janela, bitmap ou projeto carregado em campo estático. Teste headless abre a janela, confere e fecha no mesmo teste.
- Fixture de projeto é sintética e pequena, gerada em código. Nenhum arquivo de jogo entra no repositório.
- Toda chamada à IA nos testes passa pelo provedor falso (regra da IA no `CLAUDE.md`).

## Orçamento

O alvo foi aprovado em 14/11. Os números de hoje são os da T8 da #620 (12/11), antes dos PRs #684 a #696, menos o App, que a #701 já cortou (14/11). O corte dos outros projetos entra na #702 e nas seguintes.

| Projeto | Hoje | Alvo | O que fica |
|---|---|---|---|
| App | 214 | 120 | 41 smoke de tela, uma jornada por fluxo principal, o resto lógica de risco (gasto, gravação, oráculos, R9); o alvo de 120 não fecha porque as regras de "fica sempre" já somam mais |
| Core | 1.562 | 300 | Gravação, backup, injeção, codificação, falha de disco |
| Engines.YuRis | 277 | 250 | Oráculos de formato, quase todos |
| Cli | 566 | 60 | Os 5 cenários, código de saída e erro em português por comando que grava |
| AI | 304 | 60 | Chave, gasto, preço, ids, formato da requisição |
| Vndb, Installer, Platform.Windows, Patching | 169 | 60 | Patch e desinstalação byte a byte, cofre, recusa de caminho inválido |
| Total | ~4.200 | ~850 | |

Metas de tempo: o job `linux` do CI em até 5 minutos, a suíte local em até 2 minutos e a do App em até 60 s.

## Medir

Compile a solução em Release antes (`dotnet build VNForge.slnx --configuration Release`). Os scripts usam o dll já compilado, como o `run-tests.sh`.

### Cobertura

```bash
dotnet tool install --tool-path .tmp/tools dotnet-coverage --version 18.11.2   # uma vez; o CI usa a mesma versão
bash tools/cobertura.sh Core
```

A última linha da saída traz as linhas e os ramos: `VNForge.Core  linhas 90.62% (17009/18769)  ramos 84.42% (11060/13101)`. Os números só valem com saída 0. São informativos: nenhum job reprova por eles.

O script roda o projeto de testes sob o `dotnet-coverage`, instrumenta só o dll de produção do projeto e conta as linhas e os ramos dos arquivos de `src/VNForge.<projeto>`. Ele aplica o `sonar.coverage.exclusions` de `.circleci/config.yml` (lido de lá a cada execução): `tools/**`, `App.axaml.cs`, `Program.cs`, `Views/**`, `Controls/**` e `AvaloniaFolderPicker.cs`. Uma linha que aparece em mais de um método (lambda, máquina de estado) conta uma vez.

O código de produção medido é o do projeto que o teste tem o mesmo nome. Os testes do Cli não somam à cobertura do Core, por exemplo. É mais exigente do que a soma que o Sonar mostra.

O App leva uns 90 segundos com cobertura (`bash tools/cobertura.sh App`, em um processo só, na máquina de desenvolvimento) e cabe em um comando em primeiro plano; é essa medição, exata, que se compara com a base do App, quando a tarefa pede a comparação. As fatias só servem a quem medir numa máquina bem mais lenta, uma por chamada, somadas no fim:

```bash
VNFORGE_FATIAS=3 VNFORGE_FATIA=0 bash tools/cobertura.sh App
VNFORGE_FATIAS=3 VNFORGE_FATIA=1 bash tools/cobertura.sh App
VNFORGE_FATIAS=3 VNFORGE_FATIA=2 bash tools/cobertura.sh App
VNFORGE_FATIAS=3 VNFORGE_FATIA=juntar bash tools/cobertura.sh App
```

Com fatias, o `dotnet-coverage merge` descarta os ramos e o script só sabe quantos ramos cada fatia cobriu, não quais. A saída traz então um mínimo (o número principal) e um máximo. Um App que cabe em um processo só (`VNFORGE_FATIAS=1`) dá o valor exato, e é com ele que se compara a base do App (85,35% de ramos). Se o processo único passar do limite de uma chamada, rode-o como puder e resuma o XML gravado com `VNFORGE_SO_RESUMIR=.tmp/cobertura/VNForge.App.Tests.0.cobertura.xml bash tools/cobertura.sh App`, que não roda teste.

A base de `testes/base-2026-11-11.md` é do Windows. Os testes que só valem no Windows pulam no Linux (Platform.Windows sobretudo), então para comparar com a base meça no mesmo sistema. O `coverage.xml` do job `linux` conta a cobertura de todos os testes juntos e vai só para o Sonar.

As fatias de uma medição têm que ser da mesma partição (o número de fatias e a divisão das classes). O script grava a partição ao lado das medições, apaga as medições de outra partição e recusa o `juntar` se faltar fatia ou se a partição não bater.

### Tempo e memória

No Windows:

```powershell
pwsh tools/medir-testes.ps1 -Dll tests/VNForge.Core.Tests/bin/Release/net10.0/VNForge.Core.Tests.dll -Name VNForge.Core.Tests
```

Ele imprime a saída, o tempo de parede, o pico do conjunto de trabalho do processo e a contagem do JUnit. No Linux, `/usr/bin/time -v dotnet <dll> -noLogo` dá o pico em "Maximum resident set size". Meça sem cobertura (a cobertura aumenta a memória) e com a máquina parada: outros processos pesados distorcem o tempo.

O App roda inteiro num processo, com uns 55 s sem cobertura (214 testes, uns 600 MB). Para medir um subconjunto de classes, use `-ClassesFile` (um nome de classe por linha) no `tools/medir-testes.ps1`.

### O trait Risco

Cada classe de teste do App leva um ou mais `[Trait("Risco", "R5")]` com o id da tabela de riscos (R1 a R9); o primeiro é o risco principal. Para rodar só um risco: `dotnet tests/VNForge.App.Tests/bin/Release/net10.0/VNForge.App.Tests.dll -trait "Risco=R5"`. Classe nova sem trait de risco não entra: o `RiskTraitTests` falha se uma classe com teste não leva `Risco` de R1 a R9.

### O trait Tipo=Tela

O job `linux` roda o App em dois processos, com `-trait Tipo=Tela` e `-trait- Tipo=Tela` (`.circleci/run-tests.sh`). Hoje o trait está só em `ScreenSmokeTests`, a classe que mais gasta tempo de janela; só serve para equilibrar as metades. Teste novo não precisa do trait: sem ele roda no segundo processo, e a sessão headless sobe nos dois. Se uma metade passar muito da outra, marque ou desmarque classes, sem mudar o script.

### Prova da tarefa

`bash .circleci/run-tests.sh` roda todos os projetos como o job `linux` e imprime o resumo por projeto (testes, saída e tempo). Com cobertura, como o job: `dotnet-coverage collect "bash .circleci/run-tests.sh" -f xml -o coverage.xml`. No Windows, o `dotnet-coverage` precisa receber o Git Bash pelo caminho curto (`cygpath -w -s $(command -v bash)`), porque o `bash` do PATH do Windows pode ser o do WSL.

## Relatório de cada tarefa

O builder registra, no relatório da tarefa, o antes e o depois de cada projeto que mexeu: testes, tempo e, no App, a memória do processo. A cobertura de linhas e de ramos (do `tools/cobertura.sh`) entra como informação, sem meta. Para cada teste novo ou mantido, o relatório diz o risco (R1 a R9) que ele protege; teste sem risco sai. Nenhuma tarefa muda código de produção, a não ser para expor uma costura de teste que falta, e isso vai no corpo do PR.

## Capturas das telas

Os PNG das telas para o designer não saem da suíte. A ferramenta `tools/VNForge.Capturas` grava cada tela a 1440×1000, nos dois temas, com o projeto de demonstração:

```bash
dotnet run --project tools/VNForge.Capturas -c Release -- [--saida pasta] [--tela nome] [--tema escuro|claro]
```

Sem argumentos ela grava as 50 capturas por tema em `.tmp/capturas` (uns 2 minutos por tema), no formato `<tela>-<escuro|claro>.png`. `--tela` grava uma só (`painel`, `editor`, `biblioteca-gaveta`, `novo-projeto-ia` e as outras do catálogo).

O catálogo de telas fica em `tests/VNForge.App.Tests/Screens/ScreenCatalog.cs` e é compilado nos dois lugares: o `ScreenSmokeTests` abre cada tela dele (num tema) e a ferramenta usa o mesmo projeto de demonstração reduzido (os oito scripts nomeados), numa pasta nova por tema. Tela nova entra no catálogo e ganha o smoke e a captura juntos.
