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
- Os marcadores do jogo nunca aparecem crus. `\p` vira "virar a página" e `\n` vira "quebra de linha", num selo dentro do texto. No campo da tradução a pessoa escreve como escreveria num editor de texto: Shift+Enter quebra a linha, uma linha em branco abre outra página e a virada de página do fim entra sozinha, como no original.
- A caixa do jogo fica ao lado de toda tradução que a pessoa edita, com as linhas vagas pontilhadas, o que passa da caixa abaixo de um traço e o triângulo onde o jogo espera o clique.
- Os números de exemplo são os mesmos em todas as telas. Se o Painel diz 39 problemas e 21 com um clique, a Conferência diz o mesmo.
- Rótulos em caixa normal. Nada de título em maiúsculas espaçadas.
- Toda tela é conferida renderizada antes de ir para o canvas.

## O que muda, tela a tela

Os números são os dos artboards no canvas.

**1 · Início.** A tela responde "o que eu faço agora". Com projetos, a lista mostra nome, linhas, a parte pronta (a mesma porcentagem do anel) e a barra de três estados de cada projeto, e abaixo dela uma área para soltar a pasta de outro jogo, com "Escolher a pasta"; o painel à direita traz o anel com a parte pronta, o cartão "Próximo passo" com um botão só ("Revisar o capítulo 1", com as horas que faltam no ritmo da pessoa), três linhas de fatos (linhas, gasto em reais contra o limite, último teste no jogo) e "Detalhes técnicos" fechado (motor, pasta, acentos, qualidade). Na primeira vez (propriedade `estado` no artboard): quatro passos numerados (apontar a pasta do jogo, escolher os acentos, traduzir, testar no jogo) e a lista "O que você precisa" (jogo instalado, chave do Claude, espaço para a cópia de segurança). A barra de status mostra só a versão e se a chave está guardada; a cotação sai daqui. Os itens do trilho ficam desabilitados enquanto não há projeto aberto.

**2 · Novo projeto.** Quatro passos: onde está o jogo, nome e idiomas, acentos, IA e gasto. O primeiro passo diz "Feito com o motor YU-RIS" e o que o app encontrou, em linguagem comum (texto para traduzir, arquivo que abre o jogo, cópia de segurança); o nome do pacote, a chave XOR e a página de código 932 ficam em "Detalhes técnicos", fechado. O passo de acentos tem três cartões com a frase "Você não vai acreditar no que aconteceu hoje à tarde!" desenhada na caixa de texto do jogo do jeito que vai sair: "Sem acentos", "Com acentos, fonte modificada" (Em breve) e "Com acentos, ajuste no jogo" (Recomendada para este jogo). O passo de IA mostra as três qualidades com o gasto do jogo inteiro em reais e o tempo estimado, pede a cotação do dólar e o limite de gasto, e diz onde a chave fica guardada. É a única tela de uso que mostra o dólar, porque é onde a pessoa informa a cotação.

**3 · Painel.** É a casa do projeto. No topo, o nome do projeto e um botão só, com o próximo passo ("Revisar o capítulo 1"). Abaixo, a jornada em cinco passos (ler o texto, traduzir com a IA, revisar, conferir, testar no jogo), cada um com uma frase. O centro é o mapa do jogo: um anel grande com a parte pronta, a parte traduzida pela IA que espera revisão e a parte ainda em japonês, uma frase com as horas de revisão que faltam no ritmo da pessoa, e o sumário dos capítulos: uma linha por capítulo na ordem da história, com o número, o nome, a barra de três estados, a parte pronta, as linhas e uma nota ("Você está aqui · 1 erro em Manhã de chuva", "1 erro trava o teste", "A IA parou no limite de gasto"). Menus e nomes fica na última linha, sem número, porque não faz parte da história. A grade de cartões com anel deixava um capítulo sozinho na última fileira e passava da altura da tela. Os capítulos são os grupos de scripts que o Core propõe pelas pastas do jogo e que a pessoa renomeia (ver Core e IA). À direita, a lista "Precisa de você" com no máximo quatro itens, cada um com o botão que o resolve (erros que travam o teste, personagens sem gênero, limite de gasto, avisos), o gasto com IA em uma linha com a barra do limite, e o estado do jogo com "Testar de novo". Saem daqui a tabela por script, a tabela por personagem, o gráfico de ritmo, os tokens, o caminho da pasta e a versão do motor. O stepper perde "Build" e "Injetar" e ganha "Testar no jogo".

**4, 5 · Editor.** O cabeçalho diz onde a pessoa está ("Capítulo 1 · cena 2 de 4" e o nome da cena) e o que falta nela ("4 linhas da IA para revisar · 3 ainda em japonês"), com os filtros Todas, Para revisar, Em japonês e Com aviso e dois botões: "Modo revisão" e "Traduzir com IA", este no lugar de "Traduzir pendentes". A lista de scripts chama-se "Cenas" e agrupa pelos capítulos propostos pelo Core, com dobra; o menu da cena move as cenas marcadas entre capítulos, cria um capítulo novo e volta ao agrupamento automático (#374). A grade mostra Linha, Situação (Pronta, Sua, IA, Em japonês), Quem fala, Original e Tradução; a barra de três estados da cena fica no rodapé da grade. O inspetor mostra quem fala junto do original, "Entender o japonês" fechado (a explicação da IA, cerca de R$ 0,01, "só para você ler, não entra na tradução", com o original quebrado por partes), "Na cena" (a fala anterior e a seguinte), o campo da tradução sem marcadores crus (ver Regras das telas) e, logo abaixo, a caixa do jogo com "Cabe · 2 de 3 linhas" ou "Passa 1 linha"; quando não cabe, as saídas "Dividir em 2 páginas" (insere uma virada de página no fim da última linha que cabe) e "Encurtar com a IA", como botões. O botão principal é "Aprovar e ir para a próxima" (`Enter`); a sugestão da IA vem abaixo com "Usar esta sugestão" e "Pedir outra" com o preço em reais; quando o mesmo original aparece em outras linhas, o inspetor oferece "Usar em todos". A barra de status mostra a tarefa da IA com o gasto em reais, as linhas revisadas hoje e a hora do último salvamento. O tema claro é o mesmo artboard com a paleta clara.

**6 · Editor em 1366×768.** Em tela pequena a lista de cenas vira um botão com flyout no cabeçalho, os botões secundários viram ícones e o inspetor, com 360 px, se divide em três abas: Tradução (original, texto, aprovar, caixa do jogo), Sugestão (diferenças, usar, pedir outra) e Contexto (explicação, cena, voz, glossário). A grade mantém as mesmas colunas e os mesmos nomes de situação.

**7 · Modo revisão.** Uma tela só para passar pelas linhas da IA, em duas colunas. À esquerda, de cima para baixo: a fala anterior em cinza, o cartão do original com quem fala e como fala, o cartão da tradução em destaque com "Outra sugestão" e o preço, a fala seguinte e os botões. À direita, "Como fica no jogo": a caixa do jogo em tamanho grande, com a placa do nome, as três linhas (as vagas pontilhadas), o triângulo de virar a página e "Cabe · 2 de 3 linhas" em verde ou "Passa 1 linha" em amarelo; abaixo dela, o aviso com "Dividir em 2 páginas" e "Encurtar com a IA" quando não cabe, ou com "Definir o gênero" quando o personagem não tem. A caixa é a resposta mais direta a "esta fala está boa?", por isso ganha a coluna inteira. A explicação do japonês fica fechada atrás de "Entender o japonês". Dois botões grandes, "Está boa" (`Enter`) e "Corrigir" (`E`); "Depois" (`M`) e "Pular" (seta) ficam discretos. Avisos só aparecem quando há problema: a fala não cabe na caixa, falta uma quebra do original ou o personagem está sem gênero. O topo mostra "Linha 1 de 6" com um bloco por linha e quantas foram revisadas hoje. No fim, a tela diz quantas foram aprovadas e qual é a próxima cena.

**8 · Traduzir com IA.** Uma janela sobre o editor no lugar do botão que disparava a tradução direto, com três perguntas numeradas. "O que traduzir": esta cena, o capítulo ou o jogo todo, com o número de linhas pendentes. "Com que qualidade": Rápida, Equilibrada e Caprichada, cada cartão com o preço em reais e o tempo para o escopo escolhido e o nome do modelo em letra pequena; abaixo, uma frase com o que vai junto no pedido (glossário, voz dos personagens, guia de estilo, falas em volta) e, quando faltam gêneros, o aviso com "Definir agora". "Quanto custa": o valor do pedido em número grande, a barra do gasto com a marca do limite e, quando o pedido passa do limite, o aviso com o botão que troca para a qualidade mais cara que ainda cabe ("Usar Equilibrada (R$ 5,92)"); o botão principal fica desligado até caber. Esforço, modelo exato e cotação ficam em Ajustes.

**9 · Conferir.** A tela chama-se "Conferir a tradução" e responde "o que me impede de testar no jogo". A faixa do topo diz que os erros "travam o teste no jogo" e que os avisos não travam nada. Os filtros são em linguagem comum: Todos, Travam o teste, Um clique, Não cabe na caixa, Palavras e nomes, Acentos. Cada problema diz onde está pelo nome da cena ("Manhã de chuva · linha 43") e o que é, em uma frase; o painel do problema traz "O que é isso" ("No fim de cada fala o jogo espera o clique para virar a página"), o original, a tradução de hoje e como vai ficar, com os marcadores em selos ("quebra de linha", "virar a página") e, no caso de "Não cabe na caixa", a caixa do jogo com as duas saídas do editor. Os botões são "Corrigir e ir para o próximo" (`Enter`), "Abrir no editor" e "Está certo assim"; "Corrigir N com um clique" resolve de uma vez os problemas mecânicos. Os totais dos filtros e do cabeçalho são os do projeto inteiro (39 problemas, 2 que travam, 21 com um clique), não os da amostra que a lista mostra. Duas regras novas: Honoríficos (segue a escolha da aba Estilo) e Gênero. O trilho mantém "Conferir" (cabe nos 72 px), como "Testar" para Testar no jogo; o título da página é "Conferir a tradução" (#334).

**10 · Testar no jogo.** Substitui Build e injeção. Um botão faz a sequência inteira: conferir, preparar os arquivos, guardar a cópia de segurança, copiar para o jogo, abrir o jogo. "Testar só os acentos" injeta uma frase com ç, ã, é, ô na primeira fala para a pessoa conferir no jogo antes de traduzir. A faixa de estado do jogo fica no topo ("O jogo está com a tradução de 22/10") com "Voltar ao jogo original" sempre visível, em botão neutro: a ação não apaga nada, a tradução continua no projeto. Quando a Conferência tem erros, o botão principal fica desligado e a faixa diz quais erros corrigir. Opções em linguagem comum: só a cena aberta no editor, mostrar em japonês o que não foi traduzido, testar mesmo com avisos; os acentos vêm de Ajustes. "Último teste" mostra os cinco passos com o tempo de cada um. O registro completo, o caminho do executável e as cópias de segurança ficam em "Detalhes técnicos", fechado.

**11 · Ajustes.** Cinco abas, "IA e gasto" aberta por padrão. Ela tem a chave do Claude (guardada no Windows, fora da pasta do projeto), o bloco Orçamento (cotação informada pela pessoa, limite de gasto, valor acima do qual o app pergunta antes) e a qualidade padrão em três cartões com o gasto em reais e o tempo para as linhas que faltam; o modelo exato (inclusive o Fable), o esforço, o tamanho do lote, as chamadas em paralelo e o cache ficam em "Detalhes técnicos". "Estilo" é nova: honoríficos (manter, remover, adaptar), tratamento (você ou tu), narrador (quem, pessoa, gênero), onomatopeias, ordem dos nomes, o guia de estilo em texto livre (`style.md`) e um exemplo que muda conforme as escolhas. "Formatação" lista as arrumações automáticas e a caixa de texto do jogo. "Acentos" repete os cartões do Novo projeto, com os mesmos nomes, e liga ao teste de acentuação. "Aparência" junta tema, tamanho do texto, fonte do japonês e os interruptores de explicação automática (com o preço por linha) e ritmo; o interruptor "mostrar valores em reais" sai, porque as telas de uso já são só em reais.

**12 · Glossário.** A aba Personagens abre primeiro, porque é o que trava a tradução: avisa quantos estão sem gênero, destaca o seletor vazio e oferece "Sugerir voz e gênero pelas falas" com o preço em reais. As colunas falam a língua da pessoa (Nome no jogo, Nome na tradução, Gênero, Como fala, Falas; Em japonês, Em português, Observação para a IA). Ao lado da tabela de termos, o painel "Termos que se repetem": palavras frequentes no jogo que ainda não estão no glossário, com a tradução sugerida pela IA e o motivo, cada uma com "Adicionar". Tudo salva sozinho; não há botão Salvar nem nome de arquivo na tela.

**Textos.** "Build" sai do trilho e vira "Testar"; "Extrair" vira "Ler o texto"; "Conferência (QA)" vira "Conferir a tradução"; "Problemas" ganham "O que é isso"; "Restaurar originais" vira "Voltar ao jogo original"; "Marcar como ok" vira "Está certo assim"; os marcadores `\p` e `
` viram "virar a página" e "quebra de linha", e "Marcadores diferentes do original" vira "Falta uma quebra do original"; a situação das linhas na grade é Pronta, Sua, IA ou Em japonês; as telas de uso mostram valores só em reais, e o dólar com a cotação fica em Ajustes e no Novo projeto, onde a pessoa informa a cotação. "Build", "injetar" e "restaurar" continuam no log, na CLI e na documentação técnica, porque são as palavras certas lá. O `docs/design/README-design-system.md` recebe esses termos na seção de conteúdo (U23).

## Como fica no Avalonia

A regra da v2 vale: tokens em `Vnf.*`, listas em `ListBox` com `VirtualizingStackPanel`, editor em AvaloniaEdit, um `TemplatedControl` por componente do canvas, galeria em Debug e teste de renderização sem janela em 1440×1000 nos dois temas. O que entra:

| Componente do canvas | Avalonia | Onde aparece |
|---|---|---|
| `TodoList` (`.todo`, "Precisa de você") | `ItemsControl` com `TodoItem` (ícone de gravidade, título, explicação, botão) | Painel, Início (primeira vez) |
| `ProgressRing` (`.ringwrap`) | `TemplatedControl` com `Done`, `Review`, `Total`; desenha três arcos e o número no centro | Painel (jogo inteiro), Início |
| `ChapterRow` (`.toc`, `.chrow`) | `ListBox` com `ItemTemplate` de grade (número, nome, `Seg3Bar`, parte pronta, linhas, nota); classe `here` no capítulo aberto, `extra` para Menus e nomes | Painel |
| `GameTextbox` (`.gbox`) | `TemplatedControl` com `Speaker`, `Pages`, `Columns`, `Rows`; desenha a placa do nome, as linhas vagas pontilhadas, o que passa abaixo de um traço e o triângulo de virar a página (animação desligada quando o Windows pede menos movimento); tamanhos `sm` e `lg` | Editor, Modo revisão, Conferir |
| `MarkerChip` (`.mk`) | No AvaloniaEdit, um `VisualLineElementGenerator` que troca `\p`, `
` e `{VAR}` por um selo com o nome em português; nos textos só de leitura, um `InlineUIContainer` | Editor, Modo revisão, Conferir |
| `CostLabel` (`.money`) | `TemplatedControl` com `Brl` e, opcionalmente, `Usd`; nas telas de uso mostra só o real | Início, Painel, Editor, Novo projeto, Ajustes |
| `BudgetBar` (`.budget`, `.track`) | `TemplatedControl` com `Spent`, `Ask`, `Limit`; marca do limite e classe `over` | Painel, Traduzir com IA |
| `QualityCard` (`.opt`) | `OptionCard` existente com preço e tempo em destaque e o modelo em letra pequena | Traduzir com IA, Novo projeto, Ajustes |
| `ContextLines` (`.ctx`) | `ItemsControl` de duas linhas (quem, original, tradução) | Editor, Modo revisão |
| `ExplainCard` (`.explain`) | `TemplatedControl` com cabeçalho do modelo e `ItemsControl` de partes | Editor, Modo revisão |
| `ScriptGroup` (`.grp`) | `Expander` com cabeçalho próprio; na lista de cenas do Editor ele é só o cabeçalho, desenhado acima da primeira cena de cada capítulo dentro do mesmo `ListBox` (uma seleção só, para marcar cenas de capítulos diferentes); os grupos vêm do Core (ver Capítulos) | Editor |
| `GameStateBanner` (`.gamestate`) | `TemplatedControl` com ponto de estado, título, texto e botão | Testar no jogo |
| Diálogo (`.scrim`, `.dialog`) | `OverlayLayer` com `ContentDialog` próprio, sem janela nova, `Escape` fecha | Traduzir com IA |
| Modo revisão | `UserControl` próprio ligado ao `EditorViewModel` (mesma lista, mesmo comando de aprovar); `KeyBinding` para as teclas | Editor |
| Editor compacto | `Flyout` com a lista de scripts e inspetor em `TabControl` abaixo de 1400 px de janela (a 1440 o artboard mostra o inspetor inteiro; ver `EditorBreakpoints`) | Editor |
| Cartões de acentuação (`.enc`) | `OptionCard` existente com um `TextboxPreview` dentro; os selos ficam numa linha própria abaixo do nome | Novo projeto, Ajustes |
| Detalhes técnicos (`.tech`) | `Expander` fechado por padrão, com título e conteúdo livre | Início, Novo projeto, Testar no jogo, Ajustes |
| Barra de três estados (`.seg3`) | `TemplatedControl` com `Done`, `Review` e `Total`, a mesma fonte do `ProgressRing` | Início, Editor |

O `TextboxPreview` que já existe dá lugar ao `GameTextbox`, que aceita várias páginas (lista de páginas, separador "página 2") para a prévia depois de "Dividir em 2 páginas". No campo da tradução, o `EditorViewModel` converte na ida e na volta: `
` vira quebra de linha, `\p` no meio vira linha em branco e os marcadores do fim da fala ficam guardados fora do texto editável e voltam ao salvar. A conversão tem teste de ida e volta, como os formatos binários.

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
- Capítulos: o jogo não diz o que é capítulo. O app conhece a ordem numérica dos scripts e, quando o jogo traz o `yst_list.ybn`, o caminho original de cada um (`userscript\01_cap1\manha.yst`), que já alimenta o nome legível em `scripts.json`. O Core agrupa pela pasta ou pelo prefixo numérico desse caminho e, sem ele, por faixas de scripts na ordem numérica ("Parte 1", "Parte 2"); a pessoa renomeia e reagrupa, e `scripts.json` guarda o grupo escolhido ao lado do nome. O mapa do Painel e a lista do editor usam esses grupos com os totais de pronto, para revisar e pendente. Um capítulo criado à mão fica na posição da primeira cena dele. Criar, mover e voltar ao automático são do Editor (#374); o Painel renomeia e leva ao Editor para criar. Ler o fluxo de chamadas entre scripts para propor a ordem real da história fica fora deste plano.
- Prontidão: função que lê chave, gêneros, narrador, guia de estilo e o último teste de acentuação e devolve a lista para o Painel e o Início.

## Dados de exemplo

Os números do canvas são de exemplo e seguem a v2: projeto "Minha VN · PT-BR" com 48.211 linhas em 312 scripts, 62% traduzido, R$ 142,56 gastos (US$ 26,40 a R$ 5,40). Os totais saem dos sete capítulos de exemplo do Painel (18.592 linhas prontas, 10.936 para revisar, 18.683 em japonês) e valem para todas as telas; a Conferência tem 39 problemas abertos, 2 que travam o teste e 21 que se corrigem com um clique. A cotação de R$ 5,40 por dólar é um valor informado pela pessoa no mock, não uma cotação real. Os preços por modelo são os de `docs/04-app-windows.md` (US$ 4/20, 2/10, 0,10/0,50 e 10/50 por milhão de tokens) e as estimativas por qualidade saem do custo por linha da v2 (US$ 0,53, 10,55 e 21,10 para 18.320 linhas), aplicado às 18.683 linhas em japonês: R$ 2,92, R$ 58,10 e R$ 116,19. O ritmo de 130 linhas por hora (as 10.936 linhas da IA levam umas 84 horas) e as velocidades da IA (480, 240 e 160 linhas por minuto) são chutes para dar forma à tela; o app mede os de verdade. Os nomes dos capítulos e das cenas também são de exemplo: no app eles vêm das pastas dos scripts e da pessoa.

## Issues

Uma issue por item. O plano foi aprovado em 25/10/2026 e as issues são as #302 a #315. U10 vem primeiro porque as outras usam os controles dele; U11 vem antes de U12 e U17 porque as duas mostram o orçamento.

| # | Issue | Depende de | Core e IA novos |
|---|---|---|---|
| U10 · #302 | Base v3: ícones, controles novos e textos do trilho | | |
| U11 · #303 | Custo em reais e limite de gasto (Ajustes, barra de status, cache) | U10 | cotação, `BudgetGuard` |
| U12 · #304 | Janela "Traduzir com IA": três perguntas, qualidade em vez de modelo | U11 | custo por escopo e qualidade |
| U13 · #305 | Editor: entender o japonês, "Na cena", copiar, dicionário, usar em todos | U10 | `LineExplainer`, índice por original |
| U14 · #306 | Editor: dividir em páginas, encurtar com a IA, página extra no QA | U10 | `PageSplitter`, regra de marcadores |
| U15 · #307 | Modo revisão em sequência | U13, U14 | registro de aprovações |
| U16 · #308 | Testar no jogo com um clique, estado do jogo, só a cena aberta, teste de acentuação | U10 | `vnf test`, `game-state.json` |
| U17 · #309 | Painel: mapa por capítulo, "Precisa de você" e gasto | U11, U15 | prontidão, ritmo, grupos de scripts e totais por capítulo |
| U18 · #310 | Estilo: honoríficos, tratamento, narrador, onomatopeias, guia; regras Honoríficos e Gênero | U10 | prompt, duas regras do QA |
| U19 · #311 | Glossário: termos que se repetem, voz e gênero sugeridos | U10 | frequência de termos, Haiku |
| U20 · #312 | Novo projeto: acentuação com prévia, custo e limite | U11 | |
| U21 · #313 | Início: primeira vez e próximo passo | U17 | |
| U22 · #314 | Editor compacto em 1366×768 | U13 | |
| U23 · #315 | Documentação: termos novos, cópia das telas em `docs/design/telas`, README do design system | U10 | |

## Fora deste plano

O remapeamento de glifos continua "em breve" até existir o encoder e a fonte modificada. Outros motores e outros provedores de IA seguem fora, como na v2. A cotação do dólar não é consultada na internet; se isso incomodar no uso, vira issue própria. A Conferência não ganha correção automática de honoríficos: a regra só avisa, porque trocar "Misaki-chan" por "Misaki" muda a fala e merece um olhar.
