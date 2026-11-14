# Plano de testes do VNForge

Data: 2026-11-14 · Repo: alessandrocaetanob/vnforge · Estado: aprovado pelo Alessandro em 14/11 ("os testes precisam ser úteis de verdade, e não medir se uma letra deslocou 4 pixels")

## Por que mudar

A maratona da #620 levou a suíte de 9.729 para cerca de 4.200 testes, mas o critério de corte foi a cobertura: cada projeto tinha um piso de linhas e ramos, e um teste só saía se a cobertura não caísse. Por isso todos os projetos ficaram acima do orçamento (App 1.334 de 350, Core 1.562 de 500, Cli 566 de 200), e o piso entrou no CI como trava (`.circleci/piso-cobertura.txt`).

Cobertura mede se uma linha rodou, não se o teste confere algo que importa. Na suíte de hoje:

- No App, 360 dos 1.256 métodos de teste têm no nome "mostra", "botão", "texto", "aviso", "cabeçalho", "chip" ou parecido: conferem apresentação, que muda a cada quadro aprovado e que o render ao lado do quadro já confere melhor.
- Em 13/11 o `vnf library link` zerou 36 arquivos do projeto num disco rclone (#697) e terminou com sucesso. Nenhum dos mais de mil testes que gravam projeto pegou isso, porque todos gravam numa pasta temporária do NTFS local e nenhum confere o arquivo depois de gravar.
- Não existe um teste que faça o caminho inteiro num jogo sintético: extrair, traduzir com o provedor falso, injetar, restaurar e conferir o jogo byte a byte. Os testes de formato e os de injeção existem, separados.

A proposta troca a pergunta "que linha falta cobrir" por "que dano este teste evita". Todo teste passa a apontar um risco da lista abaixo; o que não aponta nenhum sai.

## Riscos

Os riscos saem das regras inegociáveis do `CLAUDE.md` e dos bugs que já chegaram ao Alessandro.

| Id | Risco | Onde já aconteceu | Como se testa |
|---|---|---|---|
| R1 | Estragar a pasta do jogo: injeção sem backup, restauração que não volta ao original | #452 (testar no jogo por cima de uma injeção) | Cenário de ponta a ponta no jogo sintético; backup e restauração byte a byte |
| R2 | Perder trabalho do usuário ao gravar o projeto | #697 (36 arquivos com 0 byte), #669 e #678 (temporário com acesso negado) | Gravação conferida por releitura; falha de disco simulada; gravação concorrente |
| R3 | Gerar arquivo que o jogo não lê | Regra 3 do `CLAUDE.md` | Ida e volta byte a byte de cada formato; vetores de ouro; um teste por campo reescrito |
| R4 | Texto errado dentro do jogo: codificação, quebra, caixa estourada, falante | #434, #435, #448 (cp1252 e falante) | Tabela de casos com valor esperado explícito por regra de codificação e de quebra |
| R5 | Gastar dinheiro com IA sem querer, ou vazar a chave | Regra 4 do `CLAUDE.md` | Chave só por ambiente ou cofre; limite de gasto recusa antes de chamar; preço por modelo; ids dos modelos |
| R6 | Fluxo principal que trava o usuário | #688 (não liga projeto existente), #619 (erro ao abrir projeto), #689 (jogo mudou de pasta) | Uma jornada por fluxo principal, pelo ViewModel |
| R7 | Arquivo externo estragado ou serviço fora do ar apaga ou esconde dado | #652 (pasta de projeto ilegível escondia os outros) | Arquivo ilegível e rede fora: recusa com aviso em português, nada é sobrescrito |
| R8 | Tela diferente do quadro aprovado | #580 (reprovada) | Não é teste: render real ao lado do quadro antes do PR. Na suíte, só o smoke por tela (abre sem exceção e sem texto cortado) |

## Camadas da suíte

**Oráculos (R3, R4).** Ficam como estão: vetores de ouro do YPF e dos hashes, Shift-JIS na página 932, o 0x5C no nome, cp1252 com « », ida e volta de cada formato. É a parte da suíte que já protege o que importa, e a maratona da #620 mostrou que o corte por cobertura apaga esses testes se ninguém os listar.

**Gravação com falha de disco (R1, R2, R7).** Nova. Uma costura de sistema de arquivos no Core, que o teste troca por uma versão que falha do jeito que o mundo real falha: rename que devolve sucesso e deixa o destino vazio (o caso do rclone), arquivo travado por outro processo, disco cheio no meio da gravação, pasta somente leitura. Cada caminho que grava projeto, manifesto, backup ou biblioteca roda contra essas falhas. Resultado esperado: erro em português e o arquivo anterior intacto, nunca sucesso com dado perdido. A #697 cria a primeira parte (a conferência por releitura).

**Cenários de ponta a ponta (R1 a R4).** Novos, poucos, no Cli, sobre um jogo YU-RIS sintético gerado em código:

1. Extrair, traduzir com o provedor falso, injetar, restaurar: o jogo volta byte a byte ao original.
2. Injetar por cima de uma injeção anterior e restaurar: volta ao original, não à primeira injeção.
3. Mover o jogo de pasta e ligar o projeto (#689): a tradução continua aplicável e a restauração acha o backup.
4. Texto com os casos difíceis de codificação e de caixa (acento antes de código de controle, falante com « », linha que estoura a caixa): o que sai no YBN bate com o valor esperado.
5. Projeto gravado sobre o sistema de arquivos com falha: o comando sai com erro e nada fica com 0 byte.

**Jornadas do App (R6).** Um teste por fluxo que a pessoa faz de ponta a ponta, pelo ViewModel, sem janela: abrir o app pela primeira vez, adicionar pasta à Biblioteca, criar projeto a partir de um jogo, ligar jogo a projeto existente, abrir a Ficha sem projeto, traduzir uma cena, revisar e aprovar, aplicar no jogo, restaurar o original, pausar e retomar a tradução noturna. Cada jornada confere o resultado que importa (arquivo gravado, projeto aberto, jogo alterado), não o texto dos botões no caminho.

**Smoke por tela (R8).** Fica o `ScreenSmokeTests` atual: cada tela do catálogo abre sem exceção e sem texto cortado. Nada de pixel, medida ou texto exato.

**Teste no ambiente real (manual, antes de cada pré-release).** Um roteiro curto que o Alessandro roda, porque é onde os testes automáticos não chegam: projeto no H: e no W: (rclone), caminho com japonês, jogo real aberto depois da injeção (#112 e seguintes). O roteiro fica em `docs/testes/roteiro-ambiente-real.md`.

## O que sai

- Teste que não aponta nenhum risco da tabela.
- Teste de apresentação: texto exato, visibilidade de botão, cor, ordem de chips, conteúdo de cabeçalho. A tela se confere pelo render ao lado do quadro.
- Teste de getter, de construtor, de texto de ajuda e de contrato repetido por comando.
- Variação de valor, tema ou tamanho que não seja caso de `[Theory]`.
- O piso de cobertura como trava do CI. A cobertura continua medida e aparece no relatório, mas não reprova o job nem impede corte de teste.

Ficam sempre, mesmo sem ganho de cobertura: os oráculos, os testes que protegem dado do usuário (R1, R2, R7), os de chave e gasto (R5) e os que só rodam numa plataforma.

## Regras para issue nova

Valem desde a aprovação deste plano, inclusive para os builders em andamento.

- Correção de bug: um teste de regressão só quando o bug cai em R1 a R7. Ele falha antes da correção e passa depois.
- Funcionalidade nova: os testes são as jornadas que ela cria, em geral de 1 a 3.
- Mudança de tela (rótulo `interface`): nenhum teste novo. A prova é o render real ao lado do quadro.
- Todo brief de builder traz `Testes: até N` e o risco de cada teste. O revisor recusa teste sem risco.

## Orçamento

Alvo aprovado em 14/11. Os números de hoje são os da T8 da #620 (12/11), antes dos PRs #684 a #696.

| Projeto | Hoje | Alvo | O que fica |
|---|---|---|---|
| App | 1.334 | 120 | ~50 smoke de tela, ~20 jornadas, o resto lógica de risco (gasto, gravação) |
| Core | 1.562 | 300 | Gravação, backup, injeção, codificação, falha de disco |
| Engines.YuRis | 277 | 250 | Oráculos de formato, quase todos |
| Cli | 566 | 60 | Os 5 cenários, código de saída e erro em português por comando que grava |
| AI | 304 | 60 | Chave, gasto, preço, ids, formato da requisição |
| Vndb, Installer, Platform.Windows, Patching | 169 | 60 | Patch e desinstalação byte a byte, cofre, recusa de caminho inválido |
| Total | ~4.200 | ~850 | |

## Tarefas

Cada tarefa vira uma issue com o rótulo `infra`. O corte usa o `tester` em `modo=auditar` para classificar cada teste por risco antes de um `builder` apagar; a lista do que ficou e do que saiu vai no corpo do PR.

- [ ] **T1 · Regras e registro de riscos**
  - Issue: #698 (`infra`)
  - Nível: leve
  - Testes: nenhum
  - Prova: `docs/04-testes.md` reescrito com a tabela de riscos, as camadas e as regras para issue nova; o passo Piso de cobertura sai de `.circleci/config.yml` e a cobertura fica só no relatório
  - Pronto: o documento substitui as regras da #620; nenhum job reprova por cobertura
- [ ] **T2 · Sistema de arquivos com falha**
  - Issue: #699 (`infra`)
  - Testes: builder · até 15
  - Prova: `dotnet test tests/VNForge.Core.Tests`
  - Pronto: a costura de sistema de arquivos cobre todo caminho de gravação do Core; os quatro tipos de falha (rename que zera, arquivo travado, disco cheio, somente leitura) rodam contra projeto, manifesto, backup e biblioteca
  - Depende de: #697
- [ ] **T3 · Cenários de ponta a ponta**
  - Issue: #700 (`infra`)
  - Testes: builder · até 5
  - Prova: `dotnet test tests/VNForge.Cli.Tests --filter Cenario`
  - Pronto: os cinco cenários desta página passam no Windows e no job `linux`
  - Depende de: T2
- [ ] **T4 · App por risco**
  - Issue: #701 (`infra`)
  - Testes: tester · modo=auditar, depois builder
  - Prova: `dotnet test tests/VNForge.App.Tests`
  - Pronto: perto de 120 testes, cada um com o risco no nome da classe ou num trait; as jornadas da lista existem; suíte local em até 60 s
  - Depende de: T1
- [ ] **T5 · Core, Cli, AI e os menores por risco**
  - Issue: #702 (`infra`)
  - Testes: tester · modo=auditar, depois builder
  - Prova: `bash .circleci/run-tests.sh`
  - Pronto: cada projeto perto do alvo da tabela, com os oráculos listados por nome no PR
  - Depende de: T1, T3
- [ ] **T6 · Roteiro do ambiente real**
  - Issue: #703 (`infra`)
  - Nível: leve
  - Testes: nenhum
  - Prova: `docs/testes/roteiro-ambiente-real.md` existe e o Alessandro roda uma vez com a próxima pré-release
  - Pronto: o roteiro cobre o H:, o W: (rclone), caminho com japonês e o jogo real

Ondas: T1 e T2; depois T3 e T4; depois T5 e T6. No máximo 3 jobs do CI por vez.

## Decisões

Aprovadas pelo Alessandro em 14/11: as reduções da tabela de orçamento e o fim do piso de cobertura como trava do CI. As regras para issue nova valem já; T1 começa agora, T2 depois do merge da #697, e o corte (T4 e T5) entra depois do merge das issues #688, #690 a #692 e #697, que mexem nos mesmos projetos.
