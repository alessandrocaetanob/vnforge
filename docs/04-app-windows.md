# 04 — Aplicativo Windows

**Design:** as telas e o design system estão copiados em [design/](design/README.md) (canvas "VNForge v2 · App Windows" e design system "VNForge v2", no Claude.ai). São seis telas (Início, Painel, Editor, Conferir, Build, Ajustes). Os desenhos em texto abaixo são o rascunho que originou o design; quando divergirem, vale o design. A implementação segue [design/avalonia.md](design/avalonia.md).

## Ideia geral

O app é uma "mesa de tradução": abre um projeto VNForge, mostra o pipeline como uma sequência de etapas e passa a maior parte do tempo no editor de linhas. Usa a mesma biblioteca da CLI, então um projeto pode ser extraído pela CLI e revisado no app, ou o contrário.

- Avalonia 12 em .NET 10, tema `FluentTheme` (claro ou escuro, seguindo o Windows).
- MVVM com CommunityToolkit.Mvvm; nenhuma regra de negócio nas telas.
- Tarefas longas (extrair, traduzir, build) rodam em segundo plano com barra de progresso e botão cancelar. Dá para continuar editando enquanto a IA traduz outro arquivo.
- Salvamento automático a cada edição (arquivo `text/*.json` do script atual).
- Atalhos de teclado para o fluxo de revisão: `Ctrl+Enter` aprova e vai para a próxima, `Ctrl+Espaço` pede sugestão da IA, `Alt+↑/↓` navega.

## Moldura

A janela principal (#241) segue o canvas v2:

- **Barra de título**: selo 訳 em `brand-shu`, nome do app, migalha do projeto (o nome, como "Minha VN — PT-BR") e a busca central "Buscar linhas, scripts e comandos" com `Ctrl+K`. Os botões de janela são os do sistema.
- **Paleta de comandos** (`Ctrl+K` ou clique na busca): sem texto lista comandos e telas; com texto acha comandos ("Traduzir pendentes", "Novo build", "Abrir pasta do projeto"), telas, scripts por id e linhas pelo texto do original ou da tradução. Setas navegam, `Enter` executa, `Esc` fecha. "Traduzir pendentes" leva ao Editor com o filtro Pendentes; a tradução em lote pela IA entra com o cabeçalho do Editor. A página Busca saiu do trilho: a paleta a substitui.
- **Trilho**: Início, Painel, Editor, Conferir (com o contador de problemas do QA) e Testar no alto, Ajustes no pé. O Glossário só aparece com projeto aberto. O contador vem da última conferência; antes da primeira, e depois de o Editor contar de novo (uma edição gravada ou uma releitura), vem das ocorrências do Editor, que é o número mais recente.
- **Barra de status**: à esquerda a tarefa em andamento (nome, barra, contagem, custo em reais, na cotação que a pessoa informou nos Ajustes) e "Tarefas (n)", que abre a lista; à direita a hora da última gravação do Editor (zerada ao abrir outro projeto), a acentuação do projeto e os limites da caixa (colunas e linhas do `formatting.json`); salvar os Ajustes do projeto atualiza os dois últimos.

## Telas

### 1. Início

O Início é a porta de entrada: lista os projetos recentes à esquerda e mostra o projeto selecionado à direita (artboard "Início" do canvas v2).

**Lista.** Cada linha traz nome, motor e caminho, porcentagem traduzida com a barra de distribuição e quando o projeto foi aberto. O rodapé diz quantos projetos há e a pasta padrão, que é a pasta que contém o projeto aberto por último (o app não tem uma configuração de pasta padrão). Botão direito numa linha oferece "Remover da lista"; a pasta do projeto não é tocada.

**Detalhe.** Motor e formato, nome, caminho, a barra com a legenda (aprovadas, revisadas, traduzidas, IA, pendentes), linhas, scripts, acentuação, modelo com esforço (o Haiku aparece sem esforço, como no artboard) e último build. O último build vem da pasta `build/` do projeto: a data é a do arquivo mais novo e a quantidade de scripts, a que o `.cache/build-state.json` registrou (só vale se os pacotes de `build/` ainda conferem em tamanho); sem build, o campo diz "Nenhum build ainda".

**Estados.**

- *Motor sem suporte* (por exemplo KiriKiri/KAG): a linha mostra "Sem suporte" em cor de alerta e o detalhe, um aviso e o botão "Abrir projeto" desabilitado.
- *Indisponível* (pasta que sumiu ou não abre): a linha mostra "Indisponível"; o detalhe explica o motivo e oferece "Remover da lista".
- *Lista de recentes ilegível* (`recent.json` corrompido): um aviso no alto diz o caminho do arquivo e que a lista aparece vazia até um projeto ser aberto.

#### Fluxo de referência

O fluxo é o do canvas v2: **Início, abrir ou continuar, Painel com as cinco etapas**.

1. Sem projeto, o Início mostra "Abrir projeto" e "Novo projeto". O trilho só libera Painel, Editor, Conferir e Testar depois que um projeto é aberto.
2. "Abrir projeto" da barra de ações escolhe uma pasta, registra o projeto nos recentes e o deixa selecionado no Início, com o detalhe pronto. Nada além disso muda de tela.
3. "Abrir projeto" do detalhe abre o selecionado de novo (relê a pasta e sobe o projeto na lista) e leva ao **Painel**, onde as cinco etapas (extrair, traduzir, conferir, build, injetar) dizem o que falta.
4. "Continuar em `<script>`" leva ao **Editor** direto no último script editado: o `text/*.json` gravado por último entre os que já têm alguma linha traduzida; sem tradução nenhuma, o primeiro script. Sem scripts extraídos, o botão diz "Continuar no Editor".
5. "Novo projeto" abre o assistente; ao terminar, o projeto criado fica selecionado no Início, como no passo 2.

### 2. Assistente de novo projeto

Quatro passos: (1) escolher a pasta do jogo → detecção automática mostra engine, versão e arquivos encontrados; (2) idiomas e nome; (3) estratégia de acentuação, com explicação curta de cada opção; (4) provedor de IA (pode pular). No fim, oferece rodar a extração.

Os passos aparecem numerados no alto, no estilo do `PipelineStepper` do Painel: o passo concluído mostra o que ficou decidido ("YU-RIS · YPF v500 detectada", "Japonês → Português do Brasil", "Remover acentos") e clicar nele volta; o pendente diz o que falta. A engine detectada vira um cartão de resultado (engine e versão, arquivo de scripts, chave XOR, executável do jogo), não uma lista de linhas. Acentuação e modelo são cartões de opção; "Usar o modelo padrão" é um interruptor. O desenho está em `docs/design/telas/NovoProjeto.dc.html`.

O assistente chama o mesmo `ProjectInitializer` do `vnf init` (`vnf init` ganhou `--source`, `--target`, `--encoding` e `--model` para as mesmas escolhas), então o `vnforge.json` e as pastas saem iguais; um teste compara os dois com um jogo sintético. O passo 1 usa `ProjectInitializer.Inspect`, que só lê a pasta do jogo. "Pular" no passo 4 grava o padrão do `vnf init` (Claude Opus 5.5). A extração do passo final roda pela barra de tarefas (#101), com andamento e cancelar.

### 3. Painel do projeto

O layout é o do artboard `docs/design/telas/Painel.dc.html`, de cima para baixo:

1. **Cabeçalho** (`PageHeader`): a pasta do projeto e a engine em mono, o nome do projeto, "Abrir pasta" (abre a pasta no gerenciador de arquivos) e "Continuar em yst00013", que abre o Editor no primeiro script com linhas pendentes (sem pendentes, o primeiro com linhas por conferir, ou o primeiro script).
2. **Etapas** (`PipelineStepper`): Extrair, Traduzir, Conferir, Build e Injetar, cada uma com o resumo sob o nome. A concluída tem ✓ verde, a primeira não concluída é a ativa, com sublinhado de destaque. Cada etapa abre a sua tela: Traduzir o Editor, Conferir a Conferência, Build e Injetar a tela Build, concluídas ou não; Extrair não tem tela.
3. **Três cartões** (`StatCard`): *Linhas* (total, % concluída e pendentes, barra de distribuição com legenda, "Continuar no editor"); *Uso da IA* (gasto, tokens de entrada e de saída, modelo, barra do gasto sobre o gasto mais o restante estimado, "Ajustes de IA"); *Conferência* (problemas, barra de erros e avisos, "Abrir a conferência").
4. **Tabelas**: "Por script" (id, nome, linhas, barra, %; "Ver todos no editor") e "Por personagem" (original → tradução, linhas, barra, %; "Editar nomes", que abre o Glossário). Cada uma mostra as 8 primeiras linhas e diz "8 de 312 scripts". Os scripts com nome vêm primeiro, cada grupo na ordem do jogo; os personagens, por número de linhas, com as linhas sem falante como "Narração". O % das tabelas e a descrição do cartão de Linhas contam aprovadas e revisadas (arredondado; só chega a 100% quando tudo está concluído); o "% com tradução" do `vnf status` conta tudo que saiu de pendente.
5. **Legenda de progresso** no rodapé.

A página lê `ProjectDashboard` (Core): o progresso é o `ProjectStatusReporter` que o `vnf status` imprime, os problemas vêm do mesmo QA do `vnf check` (sem contar as linhas ainda sem tradução, que já aparecem como pendentes em Traduzir), e os nomes vêm de `scripts.json` e `characters.json`. O nome do script é o do usuário ou, sem ele, o que a engine leu do jogo; o `vnf status` mostra o mesmo nome. Um `scripts.json`, `characters.json` ou glossário inválido não derruba o Painel: os nomes somem ou a conferência fica indisponível ("—"). Etapas:

- **Extrair**: concluída quando o `.cache/state.json` registra a extração. Resumo: "48.211 linhas em 312 scripts".
- **Traduzir**: pendentes são as linhas sem tradução. Resumo: "62% · 18.320 pendentes".
- **Conferir**: pendentes são as linhas de máquina e traduzidas, que ninguém revisou. Resumo: "2 erros · 37 avisos".
- **Build**: concluída quando os pacotes de `build/` conferem em tamanho e o último build foi feito com os mesmos textos, perfil de formatação e estratégia de hoje. O `vnf build` grava no `.cache/build-state.json` uma impressão digital do que ele lê de `text/*.json` (só o que entra nos bytes: original, tradução, tipo e posição das linhas que o build grava); mudar só status ou notas, ou copiar o projeto sem preservar datas, não a altera. Um build de versão anterior, sem impressão digital, aparece como pendente até o próximo build. Resumo: "Build em dia", "Os textos mudaram desde o último" ou "Nenhum build ainda" (o artboard mostra "14 scripts alterados desde o último"; o build ainda não guarda a lista por script, então o Painel não a conta).
- **Injetar**: concluída quando o Build está concluído e o backup mais recente lista arquivos que estão no jogo com o tamanho do arquivo de `build/`. A comparação é por tamanho, porque hashear os pacotes do jogo a cada abertura do Painel seria lento; depois de um `vnf restore` o jogo volta aos originais, o tamanho muda e a etapa volta a pendente. Dois arquivos de mesmo tamanho e conteúdo diferente passariam, o que o `vnf inject` (que compara hash) corrige. Resumo: "backup de 22/10 pronto", "Injetado · backup de 22/10" ou "Nenhuma injeção ainda".

O custo de IA é o total de `.cache/ai-usage.json` (tokens e dólares), que `vnf translate` e `vnf review` atualizam ao fim de cada execução; sem o arquivo, o Painel diz que não há chamada registrada. Os tokens de entrada somam os de cache. O restante estimado é o gasto médio por linha que já saiu de pendente vezes as linhas pendentes; sem gasto, sem linha traduzida ou com execução sem preço, o Painel não estima. Execução de modelo sem preço na tabela não soma dólar: o Painel mostra o custo como parcial ou desconhecido, nunca US$ 0,00. Um `ai-usage.json` inválido vai para `ai-usage.json.bad` e a conta recomeça; um arquivo que não abre naquele instante deixa a execução sem registro, sem apagar o histórico. A página recarrega a cada vez que é aberta.

### 4. Editor de tradução (tela principal)

```
┌───────────────┬──────────────────────────────────────────┬─────────────────────────┐
│ Scripts       │ #    Personagem  Original        Tradução │ Linha yst00013:0042     │
│ 🔍 filtrar     │ 40   美咲        おはよう！      Bom dia! │                         │
│               │ 41   —           窓の外は…       Lá fora… │ Original                │
│ ▸ yst00012 ✔  │▶42   美咲        今日は、いい…   Hoje est…│ 今日は、いい天気だね。\p │
│ ▸ yst00013 74%│ 43   健太        そうだな。      É mesmo. │                         │
│ ▸ yst00014 0% │ 44   —           ……             …        │ Tradução                │
│               │                                          │ ┌─────────────────────┐ │
│ Filtros       │                                          │ │Hoje está um dia     │ │
│ ☐ só pendentes│                                          │ │bonito, né?\p        │ │
│ ☐ com aviso   │                                          │ └─────────────────────┘ │
│ ☐ personagem  │                                          │ [Sugestão IA] [Aprovar] │
│               │                                          │                         │
│               │                                          │ Prévia da caixa de texto│
│               │                                          │ ┌─────────────────────┐ │
│               │                                          │ │Misaki               │ │
│               │                                          │ │Hoje está um dia     │ │
│               │                                          │ │bonito, né?          │ │
│               │                                          │ └─────────────────────┘ │
│               │                                          │ 2/3 linhas · 22/54 col. │
│               │                                          │ ⚠ nenhum aviso          │
└───────────────┴──────────────────────────────────────────┴─────────────────────────┘
```

- Grade virtualizada (aguenta dezenas de milhares de linhas).
- Cores por status; ícone de aviso do QA ao lado da linha.
- Busca e substituição em todo o projeto, com regex e prévia (#234). Ficam num painel dentro do Editor, aberto pelo botão **Buscar** do cabeçalho, por `Ctrl+F` (busca) ou por `Ctrl+H` (busca com o campo de substituição); `Esc` ou o X fecham e o foco volta ao texto da linha. O painel usa o serviço `ProjectSearch` do Core; a página **Busca** do trilho deixou de existir. A paleta de comandos (`Ctrl+K`) segue achando linhas por texto e levando a elas. Clicar num resultado da lista seleciona a linha na grade (troca de script se preciso); se a linha sumiu desde a busca, o Editor avisa e pede nova busca. Depois de **Aplicar**, o Editor relê as linhas. A busca procura na tradução ou no original; a substituição só altera a tradução. A prévia lista cada linha com o antes e o depois e não grava nada; **Aplicar** grava exatamente a prévia mostrada, e mudar qualquer campo a descarta. Ocorrência que encosta num marcador de controle (`\n`, `\p`, `{...}`) não conta, nem na busca nem na troca, e a linha cuja troca deixaria um marcador malformado (ou que já tem um) não é alterada: a prévia lista essas linhas com o motivo. "Palavra inteira" trata o marcador vizinho como separador e só vale na tradução (no original em japonês kana e kanji são todos letras). Cada busca tem limite de 2 s por linha, para uma regex catastrófica dar erro em vez de travar, e o botão Cancelar interrompe busca e prévia. Um padrão que só casa texto vazio (`^`, `$`) é avisado e ignorado. A linha gravada vira `translated` com origem `manual`, como uma edição no Editor; a prévia diz quantas linhas aprovadas ou revisadas perdem o status.
- **Cabeçalho** (design v2, #244): caminho do script em mono e, como título, o nome do capítulo; enquanto o nome do script não existe no Core (#243), o título mostra o id. Chips Todas, Pendentes, IA e Com aviso com a contagem do script aberto, e os botões Buscar, **Conferir arquivo** (abre a Conferência) e **Traduzir pendentes** (o app ainda não tem consumidor para esse pedido; o botão dispara o evento `TranslateRequested` e a tradução em lote segue pela CLI até uma etapa própria).
- **Lista de scripts**: filtro por texto (esconde os outros sem tirar o script aberto), nome do capítulo, barra de progresso e, no script que está traduzindo, o ícone girando.
- **Grade**: número da linha com zeros à esquerda em mono, selo de status com ponto, nome traduzido do personagem, original e tradução cortados com reticências e ícone de aviso do QA. No rodapé, a barra de distribuição por status e a legenda com os percentuais do script.
- **Inspetor** (420 px a 1440): cabeçalho com o id da linha, selo, tipo da fala, histórico e menu (copiar original); original em fundo rebaixado, com os marcadores de controle como chips; tradução com borda inferior de destaque; medidas (caracteres, linhas e colunas contra o perfil) e o estado dos marcadores ("Marcadores iguais ao original", em verde, ou a lista do que falta). **Aprovar e próxima** (`Ctrl+Enter`) e **Usar sugestão** ficam lado a lado sem quebra de linha a partir de 400 px; abaixo de 360 px o painel some com a dica de atalho e o ícone para caberem. O cartão de sugestão da IA mostra modelo, esforço, idade e a diferença palavra a palavra; abaixo vêm a prévia da caixa de texto e o contexto (voz do personagem e termos do glossário que aparecem na linha).
- O filtro por personagem saiu da tela (o ViewModel ainda o mantém); o design v2 filtra por chip e pela busca.
- **Prévia da caixa de texto**: renderiza com a fonte, a largura em colunas e o limite de linhas do perfil de formatação. É aqui que ajustes de espaçamento ficam visíveis antes do build.
- Marcadores de controle (`\p`, `\n`) aparecem como "chips" não editáveis por engano.

### 5. Conferência (QA)

Três colunas, como o artboard "Conferir" do canvas v2. O cabeçalho diz quando foi a última conferência, quantas linhas e quantas regras ativas, tem os chips Todos, Erros e Avisos com a contagem e os botões "Corrigir N automaticamente" e "Conferir de novo". Com erro aberto, uma faixa vermelha avisa "N erros bloqueiam o build". A coluna **Regras** lista as categorias de `QaCategories` (Marcadores, Caixa de texto, Espaços, Pontuação, Acentuação, Glossário, Consistência; Tradução e Outras só quando têm problema) com a contagem e o ícone do pior sinal. A lista de **problemas** mostra linha em mono, título da regra, o trecho que a correção muda e "Corrigível"; erros primeiro, depois por script e linha, e "não traduzida" por último, resumida numa linha por script. O **detalhe** do selecionado traz o `SeverityTag`, a explicação da regra, o original, a tradução atual, a correção proposta, "Abrir no editor", "Aplicar correção" e "Marcar como ok" (só aviso de uma linha).

Implementação (#106, #245): a tela usa o mesmo `QaEngine` do `vnf check`, então os números batem com a CLI. As contagens dos chips e das regras são do relatório inteiro; o filtro de gravidade e a categoria só mudam a lista. A correção proposta vem de `QaFixer.Propose`, que roda o `IQaFixer` da regra sobre a tradução gravada de cada linha apontada (uma proposta por linha e regra, sem encadear regras); "Aplicar correção" chama `QaFixer.ApplyOne` e grava só aquela linha, e "Corrigir N automaticamente" chama `QaFixer.Apply`. Só `espaco-duplo` e `espaco-antes-pontuacao` têm corretor hoje. "Marcar como ok" chama `QaFlags.MarkOk`: erro não se silencia. Duplo clique na ocorrência (ou "Abrir no editor") leva à linha no Editor. A lista é lida ao abrir a página, e o Editor relê ao voltar, na mesma linha, se a Conferência mudou algum texto. Enquanto a Conferência lê ou grava, ela fica ocupada: não troca de página e desabilita os botões.

### 6. Glossário e personagens

Duas tabelas editáveis, em abas (Termos e Personagens, cada uma com a contagem). Personagens têm nome original, tradução, gênero e notas de estilo de fala (vai para o prompt da IA). O glossário avisa no editor quando um termo aparece na linha atual.

A caixa de busca, na linha das abas, filtra a tabela aberta por original, tradução e observação (ou tom de fala); ela só esconde linhas da tela, e o salvamento grava todas. Cada linha mostra quantas linhas do projeto a usam: para um termo, as linhas cujo original contém o termo (o mesmo critério do QA e do lote de IA); para um personagem, as linhas em que ele é o falante. A contagem vem do `GlossaryUsage` do Core, lida em segundo plano, e aparece como "…" até terminar. O botão de remover tem tooltip, e `Ctrl+S` salva (só o arquivo que mudou é reescrito). O desenho está em `docs/design/telas/Glossario.dc.html`.

### 7. Configurações

- **IA**: provedor fixo em Claude na primeira versão (outros aparecem como "em breve"), escolha do modelo com preço e estimativa de custo, chave (guardada com DPAPI no perfil do usuário, nunca no projeto), tamanho do lote, concorrência, guia de estilo.
- **Formatação**: cada regra de espaçamento com liga/desliga, largura máxima, linhas por caixa, substituições personalizadas; prévia ao vivo com uma linha de exemplo.
- **Acentuação**: estratégia (`fold` / `remap` / `cp1252`), tabela de mapeamento editável no modo `remap`.

### 8. Build e injeção

Log em tempo real, resumo do que mudou (scripts reconstruídos, tamanho do YPF novo), botões **Injetar**, **Abrir jogo** e **Restaurar originais**. Mostra onde está o backup.

Com a acentuação `cp1252` e uma engine que oferece patch de executável (o YU-RIS), a página ganha o cartão **Patch da fonte no executável**, com o mesmo fluxo do `vnf patch-exe`. O cartão mostra o estado: *disponível* (o executável ainda tem o nome `ＭＳ ゴシック` em Shift-JIS), *aplicado* (nada a fazer, sem backup novo) ou *recusado*, com o motivo que o Core deu (não é um executável do Windows, o nome da fonte não aparece, aparece mais de uma vez). **Aplicar patch da fonte** pede confirmação, lista o executável que será trocado e o local do backup, faz o backup do original, grava o executável e confere o SHA-256. O patch entra no mesmo histórico de backups da injeção: **Restaurar originais** devolve o executável byte a byte, e a lista de confirmação da restauração mostra "Devolve o executável original" na linha dele.

**Linhas sem tradução não bloqueiam o build** (decisão de 2026-10-24, #232). O build copia o original nelas e o resumo mostra a contagem ("6.853 linhas sem tradução; o build copiou o original nelas."), então um projeto recém-extraído constrói sem ligar nada. Os outros avisos do QA continuam exigindo "Permitir avisos do QA"; erros bloqueiam sempre.

## Como fica rodando no dia a dia

1. Você abre o projeto; o app lembra o último script e a última linha.
2. Seleciona alguns scripts → "Traduzir com IA" → a tarefa aparece na barra inferior com progresso e custo; você continua revisando outro arquivo.
3. Revisa linha a linha com a prévia da caixa; `Ctrl+Enter` aprova.
4. "Conferir" antes do build; corrige os erros.
5. "Build + Injetar + Abrir jogo" num clique para testar no jogo. Achou algo estranho no jogo? Volta, corrige, repete.
6. Tudo que o app fez está nos arquivos do projeto: `git commit` a qualquer momento.

## Em aberto

- [x] Telas da primeira versão (D4, #97, 2026-10-24): **Início e Editor** entram no M3 (#98, #99, #100). Painel (#102), Conferência (#106), Build e injeção (#103) e Ajustes (#110) ficam no M4; até lá, o resto vai pela CLI. O trilho de navegação mostra só Início e Editor no M3.
- [x] Ajustes (#110, 2026-10-24): provedor Claude com os outros em "em breve", modelo com preço, chave só no cofre DPAPI (`apikey.dat` em `%APPDATA%/VNForge`), esforço, lote, concorrência (`app.json`, do usuário), guia de estilo (`style.md`), acentuação e as regras de normalização, espaçamento, aspas, reticências e quebra de linha com prévia ao vivo. Modelo, esforço, lote e acentuação gravam em `vnforge.json`; as regras em `formatting.json`. Ficam para depois: substituições personalizadas e a tabela do `remap`.
- [x] Ajustes em abas (#247, 2026-10-24): abas IA, Formatação e Acentuação como no artboard. Na IA: provedores em chip, cartão da chave com "Trocar chave" e "Testar conexão" (um pedido mínimo ao Haiku 5.5, com a chave de uso e a mensagem de erro em português), modelos como cartões com preço por milhão de tokens e a estimativa para as linhas pendentes (`CostEstimator.EstimateForModels`, a mesma heurística do `--dry-run`, sem cache), esforço em controle segmentado e lote e paralelismo em campos numéricos. O guia de estilo continua na aba IA, fora do artboard. Tudo é gravado a cada mudança, sem botão de salvar: o `SettingsViewModel` dispara `ProjectSaved` e expõe `AutosaveNote` ("Alterações salvas automaticamente em vnforge.json") para a barra de status da moldura. A seção de aparência (tema) entra como mais uma aba.
- [x] Build e injeção (#103, 2026-10-24): a página **Build** do trilho usa o `ProjectBuilder`, o `GameInjector` e o `GameLauncher` do Core, os mesmos de `vnf build`, `vnf inject`, `vnf restore` e `vnf run`. Cada operação é uma tarefa da barra inferior. Injetar e Restaurar originais pedem confirmação na própria página antes de escrever no jogo, mostram o plano (arquivos que substituem original ou são novos) e o local do backup. O log do Core hoje só tem as etapas, não uma linha por script, porque o `ProjectBuilder` não reporta progresso. Fica para depois: "Build + Injetar + Abrir jogo" num clique.
- [x] Testar no jogo (#308, 2026-10-25): a página do trilho (antes Build e injeção) vira **Testar no jogo**. No alto, a faixa `GameStateBanner` diz o que o jogo tem ("O jogo está com a tradução de 22/10", "O jogo está como veio", "N erros travam o teste no jogo" com os tipos de erro) e traz "Voltar ao jogo original", sempre visível e neutro: devolve os originais sem pedir confirmação, porque a tradução continua no projeto. O botão principal "Testar no jogo" (`Ctrl+B`) roda o `GameTester` do Core (o do `vnf test`): conferir, preparar os arquivos, guardar a cópia de segurança, copiar para o jogo e abrir o jogo; fica desligado com erros da Conferência. Opções em linguagem comum: "Só a cena aberta no editor", "Testar mesmo com avisos" e "Testar só os acentos". "Último teste" lista os cinco passos com o tempo de cada. O build, a injeção, o patch do executável, o registro e as cópias de segurança continuam na tela, em "Detalhes técnicos" (fechado), com os botões e as confirmações de antes. A barra de status mostra "Jogo com a tradução de 22/10" ou "Jogo original", lido de `.cache/game-state.json`. O estado só vale se for tão novo quanto o backup mais novo; senão a faixa descreve o jogo pelo backup (injeção de uma versão antiga, patch do executável). Se o estado diz que o jogo tem a tradução e o backup sumiu ou não lê, a faixa vira aviso ("O jogo está com a tradução, mas a cópia de segurança não foi encontrada"), com o motivo e como voltar ao original por fora. Teste, Injetar, Restaurar e Patch seguram a mesma trava do `vnf` e dizem "outra operação em andamento" quando ela está ocupada. A barra da tela mostra só frases fixas em linguagem comum; a mensagem do Core vai para o registro. Fica de fora: "mostrar em japonês o que não foi traduzido" do canvas, porque o build já deixa o japonês nas linhas sem tradução e não há o que ligar.
- [x] Custo em reais e limite de gasto (#303, 2026-10-25): a aba **IA e gasto** dos Ajustes ganha o bloco Orçamento (cotação do dólar, limite de gasto e "avisar acima de") e a qualidade padrão em três cartões (Rápida, Equilibrada, Caprichada) com o gasto em reais e o tempo para as linhas que faltam; o modelo exato (inclusive o Fable), o esforço, o lote, o paralelo, o cache e os provedores ficam em "Detalhes técnicos". A cotação é digitada pela pessoa e vai para o `app.json` com a data em que foi informada; o app não consulta a internet (sem cotação informada, usa R$ 5,40 de exemplo e diz isso). Limite e aviso também ficam no `app.json` (`spendLimit`, `warnAbove`, em reais, por projeto), e a qualidade em `quality`; o gasto acumulado do projeto continua em `.cache/ai-usage.json`. No Core de IA, `ExchangeRate` converte dólar em real, `CostEstimator.Quote`/`Brl`/`QuoteForModels` devolvem dólar e real de uma estimativa, `QualityProfiles` liga cada qualidade a um modelo e a um ritmo de linhas por minuto (um chute de ordem de grandeza), e o `BudgetGuard` entra no `TranslationRunner` por `TranslationRunOptions.Budget`: antes de começar ele pergunta se o pedido inteiro custa mais que o valor de aviso (sem quem responda, recusa) e, durante a execução, deixa de começar lotes quando o gasto chega ao limite; os lotes em andamento terminam e o resultado diz o motivo em `BudgetStop`. A barra de status mostra o custo da tarefa em reais (`BackgroundTaskService.Rate`). A CLI ainda não usa o `BudgetGuard`, e as telas Painel e Novo projeto trocam o dólar pelo real nas issues deles (#309 e #312).
- [x] Patch da fonte no App (#299, 2026-10-25): a página **Build** aplica o patch do executável pelo Core (`GameInjector.PlanPatch`, `InjectPatch` e `DiscardPatch`, com o `IGamePatchProvider` da engine), sem lógica de patch no App. O estado do patch é lido junto com o resto da página, planejando e apagando a cópia de trabalho de `.cache/patch/` na hora, porque só o Core sabe dizer se o executável já está com o patch. Ao clicar em **Aplicar patch da fonte** a cópia fica guardada até o usuário confirmar ou cancelar, e uma releitura da página nesse meio tempo não mexe nela. Os textos do `cp1252` em Ajustes, no assistente de novo projeto e no rótulo da estratégia descrevem o patch atual (troca do nome da fonte para MS Gothic) e mandam aplicá-lo na tela Build, em vez de falar da tabela de lead bytes do patch descartado na #62.
- [x] Fonte da prévia (D6, #105, 2026-10-24): o jogo pede a fonte pelo nome ＭＳ ゴシック, a face 0 de `msgothic.ttc` (MS Gothic, largura fixa: latim e acentos em 0,5 em, japonês em 1 em). A prévia da caixa de texto usa a MS Gothic instalada quando existir e, sem ela (Linux, CI, Windows sem o pacote japonês), a BIZ UDGothic embutida em `src/VNForge.App/Assets/Fonts`. A Microsoft não permite redistribuir nem modificar a MS Gothic ([FAQ de fontes da Microsoft](https://learn.microsoft.com/en-us/typography/fonts/font-faq)), então ela nunca entra no repositório. A BIZ UDGothic é da Morisawa, SIL OFL 1.1, de largura fixa com o latim em meia largura ([FAQ da Morisawa](https://www.morisawa.co.jp/support/faq/3839)), e tem as mesmas colunas da MS Gothic. O `GameTextbox` (o antigo `TextboxPreview`, renomeado na v3) traz a lista `MS Gothic, BIZ UDGothic` como `FontFamily` padrão, sem regra na view; a contagem de colunas do Core (`charWidth: halfWidthLatin`) não muda. A BIZ UDGothic também é a candidata para a fonte modificada do `remap` (#66, ainda aberta).
