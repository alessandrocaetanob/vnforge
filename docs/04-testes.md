# Testes

Regras da suíte de testes e o jeito de medi-la. A suíte foi refeita em 11/11/2026 (maratona da #620, [plano](plans/2026-11-11-suite-de-testes.md)); a medição de partida está em [testes/base-2026-11-11.md](testes/base-2026-11-11.md).

## Meta

A cobertura de linhas e de ramos de cada projeto não cai: o piso é a base medida em 11/11/2026 na main (commit d19f70d4), com as exclusões do Sonar. O número de testes cai. O alvo é perto de 1.500 testes, o job `linux` do CI em até 5 minutos e a suíte local em até 2 minutos.

Se o orçamento de um projeto só fecha perdendo cobertura, a cobertura vence. O relatório da tarefa diz quantos testes ficaram acima do alvo e por quê.

## Regras

1. Um teste por comportamento que o usuário percebe ou que protege dado do usuário. Um caso que só muda o tema, o tamanho da janela ou um valor de exemplo vira um caso de `[Theory]` ou sai.
2. Cada formato binário mantém o teste de ida e volta byte a byte (regra 3 do `CLAUDE.md`) e um teste por campo que a reescrita mínima altera.
3. Nada de `Thread.Sleep` nem de espera por voltas. A espera é por condição, com prazo em tempo e mensagem de falha.
4. Teste headless abre a janela, confere e fecha no mesmo teste. Nenhum teste guarda janela, bitmap ou projeto carregado em campo estático.
5. Fixture de projeto é sintética e pequena (poucas cenas e poucas linhas), gerada em código. Nenhum arquivo de jogo entra no repositório.
6. Antes de apagar um teste, confira que as linhas e os ramos que só ele cobria continuam cobertos por outro. Um teste que não cobre nada que outro já não cubra sai.
7. Interface: um teste smoke headless por tela, que confere que a tela abre sem exceção e sem texto cortado. Sem comparação de pixel e sem asserção de medida. A lógica da tela fica em testes de ViewModel. As capturas PNG para o designer são uma ferramenta à parte, fora da suíte.
8. Toda chamada à IA nos testes passa pelo provedor falso (regra da IA no `CLAUDE.md`).

### Orçamento por projeto

Alvo de testes por projeto, desde que a cobertura não fique abaixo do piso.

| Projeto | Testes |
|---|---|
| App | 350 |
| Core | 500 |
| Engines.YuRis | 250 |
| Cli | 200 |
| AI | 100 |
| Vndb, Installer, Platform.Windows e Patching juntos | 100 |

## Medir

Compile a solução em Release antes (`dotnet build VNForge.slnx --configuration Release`). Os scripts usam o dll já compilado, como o `run-tests.sh`.

### Cobertura

```bash
dotnet tool install --tool-path .tmp/tools dotnet-coverage --version 18.11.2   # uma vez; o CI usa a mesma versão
bash tools/cobertura.sh Core
```

A última linha da saída traz as linhas e os ramos: `VNForge.Core  linhas 90.62% (17009/18769)  ramos 84.42% (11060/13101)`. Os números só valem com saída 0.

O script roda o projeto de testes sob o `dotnet-coverage`, instrumenta só o dll de produção do projeto e conta as linhas e os ramos dos arquivos de `src/VNForge.<projeto>`. Ele aplica o `sonar.coverage.exclusions` de `.circleci/config.yml` (lido de lá a cada execução): `tools/**`, `App.axaml.cs`, `Program.cs`, `Views/**`, `Controls/**` e `AvaloniaFolderPicker.cs`. Uma linha que aparece em mais de um método (lambda, máquina de estado) conta uma vez.

O código de produção medido é o do projeto que o teste tem o mesmo nome. Os testes do Cli não somam à cobertura do Core, por exemplo. É mais exigente do que a soma que o Sonar mostra, e é o piso de cada tarefa.

O App leva uns 90 segundos com cobertura (`bash tools/cobertura.sh App`, em um processo só, na máquina de desenvolvimento) e cabe em um comando em primeiro plano; é essa medição, exata, que se compara com a base do App. As fatias só servem a quem medir numa máquina bem mais lenta, uma por chamada, somadas no fim:

```bash
VNFORGE_FATIAS=3 VNFORGE_FATIA=0 bash tools/cobertura.sh App
VNFORGE_FATIAS=3 VNFORGE_FATIA=1 bash tools/cobertura.sh App
VNFORGE_FATIAS=3 VNFORGE_FATIA=2 bash tools/cobertura.sh App
VNFORGE_FATIAS=3 VNFORGE_FATIA=juntar bash tools/cobertura.sh App
```

Com fatias, o `dotnet-coverage merge` descarta os ramos e o script só sabe quantos ramos cada fatia cobriu, não quais. A saída traz então um mínimo (o número principal) e um máximo. Um App que cabe em um processo só (`VNFORGE_FATIAS=1`) dá o valor exato, e é com ele que se compara a base do App (85,35% de ramos). Se o processo único passar do limite de uma chamada, rode-o como puder e resuma o XML gravado com `VNFORGE_SO_RESUMIR=.tmp/cobertura/VNForge.App.Tests.0.cobertura.xml bash tools/cobertura.sh App`, que não roda teste.

A base de `testes/base-2026-11-11.md` é do Windows e vale para as tarefas locais. Os testes que só valem no Windows pulam no Linux (Platform.Windows sobretudo), então meça no mesmo sistema da base. O piso do CI (`.circleci/piso-cobertura.txt`, conferido pelo passo Piso de cobertura) é uma medição do job `linux`, não a do Windows; ele conta a cobertura de todos os testes juntos e fica em `docs/testes/depois.md` ao lado da base.

As fatias de uma medição têm que ser da mesma partição (o número de fatias e a divisão das classes). O script grava a partição ao lado das medições, apaga as medições de outra partição e recusa o `juntar` se faltar fatia ou se a partição não bater.

### Tempo e memória

No Windows:

```powershell
pwsh tools/medir-testes.ps1 -Dll tests/VNForge.Core.Tests/bin/Release/net10.0/VNForge.Core.Tests.dll -Name VNForge.Core.Tests
```

Ele imprime a saída, o tempo de parede, o pico do conjunto de trabalho do processo e a contagem do JUnit. No Linux, `/usr/bin/time -v dotnet <dll> -noLogo` dá o pico em "Maximum resident set size". Meça sem cobertura (a cobertura aumenta a memória) e com a máquina parada: outros processos pesados distorcem o tempo.

O App roda inteiro num processo, com uns 2 minutos sem cobertura. Para medir um subconjunto de classes, use `-ClassesFile` (um nome de classe por linha) no `tools/medir-testes.ps1`.

### Prova da tarefa

`bash .circleci/run-tests.sh` roda todos os projetos como o job `linux` e imprime o resumo por projeto (testes, saída e tempo). Com cobertura, como o job: `dotnet-coverage collect "bash .circleci/run-tests.sh" -f xml -o coverage.xml`, e depois `bash .circleci/piso-cobertura.sh coverage.xml` confere o piso. No Windows, o `dotnet-coverage` precisa receber o Git Bash pelo caminho curto (`cygpath -w -s $(command -v bash)`), porque o `bash` do PATH do Windows pode ser o do WSL.

## Relatório de cada tarefa

O builder registra, no relatório da tarefa, o antes e o depois de cada projeto que mexeu: testes, tempo, cobertura de linhas e de ramos (do `tools/cobertura.sh`) e, no App, a memória do processo. A cobertura de linhas e de ramos depois tem que ser igual ou maior que a da base. Nenhuma tarefa muda código de produção, a não ser para expor uma costura de teste que falta, e isso vai no corpo do PR.

## Capturas das telas

Os PNG das telas para o designer não saem da suíte. A ferramenta `tools/VNForge.Capturas` grava cada tela a 1440×1000, nos dois temas, com o projeto de demonstração:

```bash
dotnet run --project tools/VNForge.Capturas -c Release -- [--saida pasta] [--tela nome] [--tema escuro|claro]
```

Sem argumentos ela grava as 50 capturas por tema em `.tmp/capturas` (uns 2 minutos por tema), no formato `<tela>-<escuro|claro>.png`. `--tela` grava uma só (`painel`, `editor`, `biblioteca-gaveta`, `novo-projeto-ia` e as outras do catálogo).

O catálogo de telas fica em `tests/VNForge.App.Tests/Screens/ScreenCatalog.cs` e é compilado nos dois lugares: o `ScreenSmokeTests` abre cada tela dele (num tema) e a ferramenta usa o mesmo projeto de demonstração reduzido (os oito scripts nomeados), numa pasta nova por tema. Tela nova entra no catálogo e ganha o smoke e a captura juntos.
