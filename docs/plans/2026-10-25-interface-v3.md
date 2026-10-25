# Interface v3: a primeira tradução de quem não sabe japonês

A v2 deixou o app no nível do design: cada tela igual ao seu artboard, paleta de comandos, movimento, tema. A v3 muda a pergunta. Em vez de "o app está igual ao desenho?", pergunta "um brasileiro que nunca traduziu uma visual novel consegue ir do zero ao jogo traduzido sem ajuda externa?". O desenho está no canvas "VNForge v3 · App Windows" (https://claude.ai/artifact/X4z2JK2pfKH9zZSeBGkfUW), doze artboards no mesmo design system "VNForge v2" (https://claude.ai/artifact/TvsKGRUc1BFYezZdfydrh6). Os componentes novos estão descritos aqui e no `project/v3.css` do canvas; o design system ganha um registro deles quando a implementação começar (U10).

Tudo abaixo cabe na pilha atual: Avalonia 12 com FluentTheme, CommunityToolkit.Mvvm, os controles de `Themes/Controls` e o Core sem dependência de engine ou de provedor. Nenhuma tela pede biblioteca nova.

## Quem usa

A pessoa do plano traduz do japonês para o português do Brasil, pela primeira vez, por hobby. Lê um pouco de japonês com ajuda (dicionário, IA), mas não o suficiente para julgar nuance sozinha. Paga a API em dólar com cartão brasileiro e pensa em reais. Não programa: "build", "injetar", "YPF" e "CP1252" são palavras do app, não dela. Tem um monitor de 1366×768 ou de 1920×1080 e algumas horas por semana.

O app existe para essa pessoa. Quem programa usa a CLI, que expõe tudo o que o Core faz e serve de base para integrações e extensões; o app não precisa mostrar o que a CLI mostra.

Os pontos em que a v2 trava essa pessoa, na ordem em que ela os encontra:

1. O vocabulário. O trilho diz "Build"; o Painel diz "Extrair"; a Conferência diz "Marcador \p ausente" sem dizer o que é um marcador.
2. O custo. Aparece em dólar, só depois de gasto, e nada impede um pedido de US$ 50 por engano.
3. O japonês. O editor mostra o original e a sugestão, mas não a fala anterior, a seguinte, nem o que cada pedaço significa.
4. A revisão. As 8.196 linhas que a IA traduziu são revisadas uma a uma no inspetor estreito, com o mouse, no meio da grade.
5. O jogo. Build e injeção são duas telas e quatro cliques; o estado do jogo (traduzido, original, de quando) não aparece em lugar nenhum.

A v3 responde a cada uma. O resto da v2 fica como está.

## Regras das telas

A revisão do canvas em 25/10 mostrou que responder aos cinco pontos não basta: o Painel, o modo revisão e a janela de IA tinham saído como painéis densos, com tabelas e números demais. As telas passam a seguir estas regras, e qualquer tela nova é medida por elas:

- Uma tela responde a uma pergunta e tem um botão principal. O Painel responde "onde estou e o que faço agora"; o modo revisão, "esta fala está boa?"; a janela de IA, "o que, com que qualidade e por quanto".
- Linguagem comum. YPF, tokens, nome de modelo, página de código e caminho de pasta ficam em "Detalhes técnicos", em Ajustes ou na CLI.
- Dinheiro só em reais nas telas de uso. O dólar e a cotação aparecem em Ajustes, onde a pessoa informa a cotação.
- No máximo três números em destaque por tela. Tabela por script e por personagem, tokens e gráfico de ritmo saem do Painel; o editor e o glossário já mostram isso onde faz sentido.
- Progresso em três estados: pronto, traduzido pela IA esperando revisão, ainda em japonês. Os cinco estados internos continuam no Core e na grade do editor.
- Qualidade em vez de modelo: Rápida (Haiku), Equilibrada (Sonnet) e Caprichada (Opus, padrão), com o nome do modelo em letra pequena. O modelo exato, o Fable para cenas difíceis e o esforço ficam em Ajustes e na CLI.
- Toda tela é conferida renderizada antes de ir para o canvas.

## O que muda, tela a tela

Os números são os dos artboards no canvas.

**1 · Início.** A tela ganha um estado de primeira vez (propriedade `estado` no artboard): quatro passos numerados (apontar a pasta do jogo, escolher os acentos, traduzir, testar no jogo) e a lista "O que você precisa" (jogo instalado, chave do Claude com US$ 5, espaço para o backup). Com projetos, o detalhe do projeto ganha o cartão "Próximo passo" ("Revisar as 8.196 linhas que a IA traduziu", com a estimativa de horas) e a linha "Gasto com IA" em dólar e em reais. A barra de status mostra a cotação em uso. Os itens do trilho ficam desabilitados enquanto não há projeto aberto.

**2 · Novo projeto.** O passo de acentuação deixa de ser uma lista de três rádios com nomes técnicos. Cada opção vira um cartão com a frase "Você não vai acreditar no que aconteceu hoje à tarde!" desenhada na caixa de texto do jogo do jeito que vai sair: "Voce nao vai acreditar" na opção que remove acentos, com acentos na CP1252. A opção que o jogo aceitou no teste do patch leva o selo "Recomendada para este jogo"; o remapeamento de glifos leva "Em breve". Chave XOR, Shift-JIS e página de código 932 ficam em "Detalhes técnicos", fechado. O passo de IA mostra o custo do jogo inteiro por modelo, em dólar e em reais, e pede um limite de gasto.

**3 · Painel.** É a casa do projeto. No topo, o nome do projeto e um botão só, com o próximo passo ("Revisar o capítulo 1"). Abaixo, a jornada em cinco passos (ler o texto, traduzir com a IA, revisar, conferir, testar no jogo), cada um com uma frase. O centro é o mapa do jogo: um anel grande com a parte pronta, a parte traduzida pela IA que espera revisão e a parte ainda em japonês, uma frase com as horas de revisão que faltam no ritmo da pessoa, e um cartão por capítulo (Prólogo, capítulos, Final, Sistema) com o mesmo anel em miniatura, o número de linhas e uma nota ("Você está aqui", "2 erros travam o teste", "A IA parou no limite de gasto"). À direita, a lista "Precisa de você" com no máximo quatro itens, cada um com o botão que o resolve (erros que travam o teste, personagens sem gênero, limite de gasto, avisos), o gasto com IA em uma linha com a barra do limite, e o estado do jogo com "Testar de novo". Saem daqui a tabela por script, a tabela por personagem, o gráfico de ritmo, os tokens, o caminho da pasta e a versão do motor. O stepper perde "Build" e "Injetar" e ganha "Testar no jogo".

**4, 5 · Editor.** O inspetor ganha três blocos: ações sobre o original (copiar, pesquisar no Jisho, explicar o japonês), o cartão de explicação (Claude Haiku 5.5, US$ 0,002, "só para você ler, não entra na tradução", o original quebrado por partes com o significado de cada uma) e "Na cena" (a fala anterior e a seguinte, com quem fala). Quando a tradução não cabe na caixa, o aviso traz duas saídas: "Dividir em 2 páginas" (insere um `\p` no fim da terceira linha) e "Encurtar com a IA". Quando o mesmo original aparece em outras linhas, o inspetor oferece "Aplicar às N". A lista de scripts agrupa por capítulo, com dobra. O cabeçalho mostra o que falta no script ("17 linhas da IA para revisar · 6 pendentes") e dois botões novos: "Modo revisão" e "Traduzir com IA", este no lugar de "Traduzir pendentes". A barra de status ganha o ritmo do dia e o custo da tarefa em reais. O tema claro é o mesmo artboard com a paleta clara.

**6 · Editor em 1366×768.** Em tela pequena a lista de scripts vira um botão com flyout no cabeçalho, os botões secundários viram ícones e o inspetor, com 360 px, se divide em três abas: Tradução (original, texto, caixa do jogo), Sugestão (diff, usar, pedir outra) e Contexto (explicação, cena, voz, glossário). A grade mantém as mesmas colunas.

**7 · Modo revisão.** Uma tela só para passar pelas linhas da IA, lida de cima para baixo em uma coluna: a fala anterior em cinza, o cartão do original com quem fala e a voz do personagem, o cartão da tradução em destaque com a caixa do jogo logo abaixo, a fala seguinte e os botões. A explicação do japonês fica fechada atrás de "Entender o japonês". Dois botões grandes, "Está boa" (`Enter`) e "Corrigir" (`E`); "Outra sugestão" com o preço, "Deixar para depois" (`M`) e "Pular" (seta) ficam discretos. Contadores e avisos só aparecem quando há problema: a fala não cabe na caixa, os marcadores diferem do original ou o personagem está sem gênero. O topo mostra "Linha 1 de 6" com um bloco por linha e quantas foram revisadas hoje. No fim, a tela diz quantas foram aprovadas e qual é o próximo script.

**8 · Traduzir com IA.** Uma janela sobre o editor no lugar do botão que disparava a tradução direto, com três perguntas numeradas. "O que traduzir": este script, o capítulo ou tudo o que falta, com o número de linhas pendentes. "Com que qualidade": Rápida, Equilibrada e Caprichada, cada cartão com o preço em reais e o tempo para o escopo escolhido e o nome do modelo em letra pequena; abaixo, uma frase com o que vai junto no pedido (glossário, voz dos personagens, guia de estilo, falas em volta) e, quando faltam gêneros, o aviso com "Definir agora". "Quanto custa": o valor do pedido em número grande, a barra do gasto com a marca do limite e, quando o pedido passa do limite, o aviso com o botão que troca para a qualidade mais cara que ainda cabe ("Usar Equilibrada (R$ 5,92)"); o botão principal fica desligado até caber. Esforço, modelo exato e cotação ficam em Ajustes.

**9 · Conferência.** Cada problema ganha o bloco "O que é isso", em linguagem comum ("O jogo usa \p para esperar o clique e virar a página"). A faixa de erros diz que eles "travam o teste no jogo", não "o build". O problema "Não cabe na caixa" mostra a caixa do jogo e as duas saídas do editor. Duas regras novas: Honoríficos (segue a escolha da aba Estilo) e Gênero. "Corrigir e próxima" com `Ctrl+Enter`; "Marcar como ok" vira "Está certo assim".

**10 · Testar no jogo.** Substitui Build e injeção. Um botão, `Ctrl+T`, faz a sequência inteira: conferir, gerar o build, guardar o backup, copiar para o jogo, abrir o jogo. A faixa de estado do jogo fica no topo ("O jogo está com a tradução de 22/10 · originais em backup\2026-10-22_2316") com "Restaurar originais" sempre visível. Opções em linguagem comum: só o script aberto, linhas sem tradução ficam em japonês, permitir avisos, acentos CP1252 (travado, vem de Ajustes). "Teste de acentuação" injeta uma frase com ç, ã, é no primeiro diálogo para a pessoa conferir no jogo antes de traduzir. Log e backups continuam.

**11 · Ajustes.** Cinco abas. "IA e custo" ganha o bloco Orçamento (cotação informada pela pessoa, limite de gasto, valor acima do qual o app pergunta antes). "Estilo" é nova: honoríficos (manter, remover, adaptar), tratamento (você ou tu), narrador (quem, pessoa, gênero), onomatopeias, ordem dos nomes, o guia de estilo em texto livre (`style.md`) e um exemplo que muda conforme as escolhas. "Acentos" repete os cartões com prévia do Novo projeto e liga ao teste de acentuação. "Aparência" junta tema, tamanho do texto, fonte do japonês e os interruptores de explicação automática, reais e ritmo.

**12 · Glossário.** Ao lado da tabela de termos, o painel "Termos que se repetem": palavras frequentes no jogo que ainda não estão no glossário, com a tradução sugerida pelo Haiku e o motivo, cada uma com "Adicionar". A aba Personagens avisa quantos estão sem gênero, destaca o seletor vazio e oferece "Sugerir voz e gênero pelas falas".

**Textos.** "Build" sai do trilho e vira "Testar"; "Extrair" vira "Ler o texto"; "Problemas" ganham "O que é isso"; as telas de uso mostram valores só em reais, e o dólar com a cotação fica em Ajustes e no Novo projeto, onde a pessoa informa a cotação. "Build", "injetar" e "restaurar" continuam no log, na CLI e na documentação técnica, porque são as palavras certas lá. O `docs/design/README-design-system.md` recebe esses termos na seção de conteúdo (U23).

## Como fica no Avalonia

A regra da v2 vale: tokens em `Vnf.*`, listas em `ListBox` com `VirtualizingStackPanel`, editor em AvaloniaEdit, um `TemplatedControl` por componente do canvas, galeria em Debug e teste de renderização sem janela em 1440×1000 nos dois temas. O que entra:

| Componente do canvas | Avalonia | Onde aparece |
|---|---|---|
| `TodoList` (`.todo`, "Precisa de você") | `ItemsControl` com `TodoItem` (ícone de gravidade, título, explicação, botão) | Painel, Início (primeira vez) |
| `ProgressRing` (`.ringwrap`) | `TemplatedControl` com `Done`, `Review`, `Total`; desenha três arcos e o número no centro | Painel (jogo inteiro e por capítulo) |
| `ChapterCard` (`.chap`) | `Button` com `ProgressRing`, nome, linhas e nota; classe `here` | Painel |
| `CostLabel` (`.money`) | `TemplatedControl` com `Usd` e `Brl`; esconde o real quando não há cotação | Início, Novo projeto, Ajustes |
| `BudgetBar` (`.budget`, `.track`) | `TemplatedControl` com `Spent`, `Ask`, `Limit`; marca do limite e classe `over` | Painel, Traduzir com IA |
| `QualityCard` (`.opt`) | `OptionCard` existente com preço e tempo em destaque e o modelo em letra pequena | Traduzir com IA |
| `ContextLines` (`.ctx`) | `ItemsControl` de duas linhas (quem, original, tradução) | Editor, Modo revisão |
| `ExplainCard` (`.explain`) | `TemplatedControl` com cabeçalho do modelo e `ItemsControl` de partes | Editor, Modo revisão |
| `ScriptGroup` (`.grp`) | `Expander` com cabeçalho próprio dentro do `ListBox` de scripts | Editor |
| `GameStateBanner` (`.gamestate`) | `TemplatedControl` com ponto de estado, título, texto e botão | Testar no jogo |
| Diálogo (`.scrim`, `.dialog`) | `OverlayLayer` com `ContentDialog` próprio, sem janela nova, `Escape` fecha | Traduzir com IA |
| Modo revisão | `UserControl` próprio ligado ao `EditorViewModel` (mesma lista, mesmo comando de aprovar); `KeyBinding` para as teclas | Editor |
| Editor compacto | `SplitView` com a lista de scripts como `Flyout` abaixo de 1400 px; inspetor em `TabControl` abaixo de 1500 px | Editor |
| Cartões de acentuação (`.enc`) | `OptionCard` existente com um `TextboxPreview` dentro; os selos ficam numa linha própria abaixo do nome | Novo projeto, Ajustes |

O `TextboxPreview` que já existe passa a aceitar várias páginas (lista de páginas, separador `· · ·`) para a prévia depois de "Dividir em 2 páginas".

## Core e IA

O Core ganha o que as telas precisam, sempre testável com o provedor falso:

- `CostEstimator` passa a receber a cotação (decimal, informada pela pessoa, em `app.json`) e devolve dólar e real. `BudgetGuard` no runner de lotes: para quando o gasto acumulado chega ao limite, pergunta antes de pedidos acima do valor de aviso. Gasto, limite e cotação ficam em `.cache/ai-usage.json` e `app.json`; nada de rede para cotação, por decisão: a pessoa informa e o app diz quando informou.
- `LineExplainer` em `VNForge.AI`: pede ao Claude Haiku 5.5 a quebra do original em partes com significado, em JSON (`output_config.format`), e guarda em `.cache/explain.json` por hash do original. Chamado por botão ou, se a pessoa ligar em Aparência, ao selecionar a linha.
- O construtor de prompt passa a incluir duas linhas antes e duas depois de cada fala, o guia de estilo (`style.md`) e as escolhas de estilo de `vnforge.json` (honoríficos, tratamento, narrador, onomatopeias, ordem dos nomes). Conferir o que a V1 já envia antes de duplicar.
- Agrupamento por original: índice de hash do texto original por projeto, para "Aplicar às N" e para a regra de consistência que já existe.
- `PageSplitter`: usa o algoritmo de quebra do `LinePreviewer` para inserir `\p` no fim da última linha que cabe. A regra de marcadores do QA passa a tratar um `\p` a mais como aviso de "página extra", não como erro.
- Regras novas do QA: `Honorifics` (sufixos -san, -kun, -chan, -sama e "senpai" na tradução, conforme a escolha de estilo) e `Gender` (fala de personagem com gênero indefinido em `characters.json`).
- Sugestão de termos: contagem de sequências de kanji e katakana (2 a 6 caracteres) nos originais, fora do glossário, com frequência mínima de 5; o Haiku traduz o lote e justifica em uma frase. Sugestão de voz e gênero: amostra de até 20 falas por personagem para o Haiku.
- `vnf test`: comando único que encadeia `qa`, `build`, backup, injeção e abertura do executável, com `--only-script` e `--accent-test` (injeta uma frase fixa com ç, ã, é, ô na primeira fala do primeiro script). Estado do jogo em `.cache/game-state.json` (qual build está no jogo, pasta do backup, data) para a faixa da tela e para a barra de status.
- Ritmo: registro de aprovações com hora em `.cache/sessions.json`; o Core devolve linhas de hoje, média por hora dos últimos sete dias e horas que faltam para revisar as linhas da IA. O Painel usa só a frase com as horas; o gráfico por dia fica para a CLI.
- Capítulos: o agrupamento de scripts por capítulo que o `ScriptGroup` já usa alimenta o mapa do Painel, com os totais de pronto, para revisar e pendente por capítulo.
- Prontidão: função que lê chave, gêneros, narrador, guia de estilo e o último teste de acentuação e devolve a lista para o Painel e o Início.

## Dados de exemplo

Os números do canvas são de exemplo e seguem a v2: projeto "Minha VN · PT-BR" com 48.211 linhas em 312 scripts, 62% traduzido, US$ 26,40 gastos. O Painel calcula os totais a partir dos sete capítulos de exemplo (18.592 linhas prontas, 10.936 para revisar, 18.683 em japonês), então os percentuais dele diferem um pouco dos das outras telas. A cotação de R$ 5,40 por dólar é um valor informado pela pessoa no mock, não uma cotação real. Os preços por modelo são os de `docs/04-app-windows.md` (US$ 4/20, 2/10, 0,10/0,50 e 10/50 por milhão de tokens) e as estimativas por escopo saem da estimativa da v2 para as 18.320 linhas pendentes (US$ 21,10 com Opus), divididas por linha. O ritmo de 130 linhas por hora e os tempos por modelo são chutes para dar forma à tela; o app mede os de verdade.

## Issues

Uma issue por item, criadas antes de qualquer branch, depois de o plano ser aprovado. U10 vem primeiro porque as outras usam os controles dele; U11 vem antes de U12 e U17 porque as duas mostram o orçamento.

| # | Issue | Depende de | Core e IA novos |
|---|---|---|---|
| U10 | Base v3: ícones, controles novos e textos do trilho | | |
| U11 | Custo em reais e limite de gasto (Ajustes, barra de status, cache) | U10 | cotação, `BudgetGuard` |
| U12 | Janela "Traduzir com IA": três perguntas, qualidade em vez de modelo | U11 | custo por escopo e qualidade |
| U13 | Editor: explicar o japonês, "Na cena", copiar, Jisho, aplicar às iguais | U10 | `LineExplainer`, índice por original |
| U14 | Editor: dividir em páginas, encurtar com a IA, página extra no QA | U10 | `PageSplitter`, regra de marcadores |
| U15 | Modo revisão em sequência | U13, U14 | registro de aprovações |
| U16 | Testar no jogo com um clique, estado do jogo, só o script aberto, teste de acentuação | U10 | `vnf test`, `game-state.json` |
| U17 | Painel: mapa por capítulo, "Precisa de você" e gasto | U11, U15 | prontidão, ritmo, totais por capítulo |
| U18 | Estilo: honoríficos, tratamento, narrador, onomatopeias, guia; regras Honoríficos e Gênero | U10 | prompt, duas regras do QA |
| U19 | Glossário: termos que se repetem, voz e gênero sugeridos | U10 | frequência de termos, Haiku |
| U20 | Novo projeto: acentuação com prévia, custo e limite | U11 | |
| U21 | Início: primeira vez e próximo passo | U17 | |
| U22 | Editor compacto em 1366×768 | U13 | |
| U23 | Documentação: termos novos, cópia das telas em `docs/design/telas`, README do design system | U10 | |

## Fora deste plano

O remapeamento de glifos continua "em breve" até existir o encoder e a fonte modificada. Outros motores e outros provedores de IA seguem fora, como na v2. A cotação do dólar não é consultada na internet; se isso incomodar no uso, vira issue própria. A Conferência não ganha correção automática de honoríficos: a regra só avisa, porque trocar "Misaki-chan" por "Misaki" muda a fala e merece um olhar.
