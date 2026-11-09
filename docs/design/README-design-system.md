Sistema visual do VNForge, o aplicativo Windows que traduz visual novels, em Avalonia 12 sobre .NET 10. A v3 é a direção **A · Estante** do quadro da #542: a capa é a protagonista, a interface recua, o ar entre as coisas é generoso e o verde-água marca o que é interativo. A base é a v2 (mesmos nomes de token, mesmo gerador; o design da v2 segue registrado em [README.md](README.md)): `tokens.json` gera `Tokens.axaml` por `tools/Generate-Tokens.ps1`, sem mudança no script.

## Conteúdo

- Interface em português do Brasil, caixa de frase em tudo: "Traduzir com IA", "Apontar pasta", "Abrir jogo".
- Botão começa com verbo no infinitivo ("Aprovar e próxima", "Varrer de novo"). Item do trilho é substantivo de uma palavra ("Biblioteca", "Fila", "Ajustes").
- Termos fixos: **jogo** (uma VN na Biblioteca), **ficha** (capa, sinopse, duração, tags e personagens do VNDB), **linha**, **script**, **marcador**, **cena**. Não alternar com "título", "item" ou "entrada".
- Uma tela responde a uma pergunta e tem um botão principal. A Biblioteca responde "o que eu tenho e o que falta traduzir?"; o Editor, "essa linha está boa?".
- Números e moeda no formato pt-BR: `48.211 linhas`, `R$ 142,56`, `38%`. Duração do jogo como o VNDB: "Curta", "Média", "Longa" seguida das horas, em "Média · 18 h".
- Spoiler e conteúdo adulto ficam escondidos por padrão e dizem o que esconderam: "2 de spoiler escondidas", "Conteúdo adulto escondido". Nunca mostrar a arte adulta como miniatura.
- Mensagens dizem o fato e a consequência: "Corrija 2 erros antes de testar." Sem exclamação, sem emoji.

## Cores

- O app segue o tema do Windows. **Escuro** é a referência; **Claro** tem os mesmos papéis. As cores vêm de `tokens.json`, nunca de valor solto.
- Camadas, de baixo para cima: `bg-base` (janela e trilho), `bg-layer` (área de conteúdo), `bg-card` (painéis, gaveta, diálogos). Hover em `bg-card-hover`; campos, trilhos de barra e tags em `bg-inset`.
- Painéis se separam por `stroke-subtle`. Só o que flutua usa sombra (`shadow-flyout`), e só a capa usa `shadow-cover`.
- `accent` (verde-água) é a cor de interação: botão accent, aba ativa, chip de filtro ativo, anel da capa selecionada, progresso. Texto em destaque usa `accent-text`; fundo de seleção, `accent-soft`; texto sobre o acento, `on-accent`.
- `brand-shu` é só do selo 訳. `accent` e o verde de "Pronta" (`status-approved-*`) são parentes: por isso todo selo de estado leva uma palavra e a barra de progresso leva legenda.
- Estados da linha usam os pares `status-*-bg` e `status-*-fg`: Pronta (`approved`), Revisada (`reviewed`), Sua (`translated`), IA (`ai`), Em japonês (`pending`). Aviso e erro do QA usam `signal-warning-*` e `signal-error-*`.
- Barras e anel usam `seg-*`. A caixa do jogo usa `textbox-*` e o palco da revisão usa `stage-*`, iguais nos dois temas porque representam o jogo.
- Selo e dica sobre a capa usam `cover-chip-*`; o jogo adulto usa `cover-veil-bg`, opaco. Nome de personagem usa `speaker-1` a `speaker-8`: cada personagem guarda um índice e a cor sai do tema.
- Contraste: todo texto passa 4,5:1 sobre os fundos citados na nota do token; borda de controle, anel de foco, acento e barras passam 3:1. A tabela está em "Contraste".

## Tipografia

- Só as fontes que o app já embute sob SIL OFL: **Zen Kaku Gothic New** (Medium, Bold) nos títulos, **Noto Sans** (Regular, Medium, SemiBold) na interface, **Noto Sans JP** (Regular, Medium) no texto original e **JetBrains Mono** (Regular, Medium) em ids e na caixa do jogo. Nenhuma fonte nova entra com a v3; o Noto Sans Bold continua fora, então a interface usa no máximo o peso 600.
- Títulos em `title-xl` (34/40), `title-lg` (24/32), `title-md` (17/24) e `title-sm` (15/20), na família `display`, sempre Bold. Um `title-xl` por tela.
- Texto corrido em `body` (14/20); rótulo de botão em `body-strong`; linhas e listas em `body-sm`; metadados em `caption`. Cabeçalho de coluna em `column-header`; rótulo de campo em `overline` (caixa alta, 0,07 em).
- Japonês sempre na família `jp`: `jp-body` (19/30) no inspetor, `jp-row` (13/18) na grade.

## Espaço, tamanho e forma

- Grade de 4 px: `space-1` a `space-10`. Gap da grade de capas em `space-5` e `space-6`; padding de painel em `space-4`; de gaveta e diálogo em `space-6`; margem da página em `space-8`.
- Controles em `control-md` (32); barras de comando em `control-sm` (28); ação principal de diálogo e de tela vazia em `control-lg` (40). Linha da grade em `row-height` (48), fixa por causa da virtualização.
- Estrutura da janela: `titlebar-height` 40, `navrail-width` 80, `statusbar-height` 28. Gaveta da Biblioteca em `drawer-width` (396); inspetor em `inspector-width` (384); lista de cenas em `scripts-width` (220).
- Raios: `radius-sm` (8) em controles; `radius-md` (12) em blocos internos; `radius-lg` (16) em painéis, capas e retratos grandes; `radius-xl` (20) na gaveta; `radius-pill` em chip, tag e selo. Os painéis de hoje usam `radius-md` (era 8): passam para `radius-lg` na migração.

## Capa e personagem

- A **capa** é um elemento de primeira classe: proporção fixa 3:4, `radius-lg`, `shadow-cover`, imagem cortada por centro (`UniformToFill`). Tamanhos: `cover-card-min` (200 de largura mínima na grade), `cover-drawer-width` (150 na gaveta), `cover-thumb-width` (44 em cabeçalho e lista).
- Estados da capa: **selecionada** (anel de 3 px em `accent`, afastado 3 px), **sem suporte** (selo "Engine · sem suporte" em `cover-chip-warn`), **para confirmar** (ícone de aviso no canto), **sem casamento** (ícone de link e capa substituída pelo nome), **adulto** (véu opaco `cover-veil-bg`, ícone de olho riscado, "18+"), **em tradução** (faixa de 5 px de progresso na base). Spoiler não cobre a capa: cobre tags, sinopse e personagens, e a tag tracejada diz quantas há.
- O **retrato de personagem** tem três tamanhos (`portrait-sm` 28 em círculo, `portrait-md` 56 e `portrait-lg` 64 em quadrado de `radius-lg`). Sem imagem, vira silhueta sobre degradê do matiz do personagem; o narrador usa o ícone Book sobre `bg-inset`. Selecionado, ganha anel de 2 px em `accent`.

## Estados e movimento

- Foco de teclado: anel sólido de 2 px em `focus-ring`, afastado 1 px. Hover e pressed trocam o fundo em `duration-fast` (167 ms, ease-out). Painel, gaveta, diálogo e troca de aba entram em `duration-normal` (250 ms). Com "Efeitos de animação" desligado no Windows, sem transição.
- Desabilitado: texto `text-tertiary`, borda `stroke-subtle`, sem hover.
- Seleção em lista e grade: fundo `accent-soft` com barra de 3 px em `accent` à esquerda.

## Iconografia

- Fluent UI System Icons (Microsoft, MIT), estilo Regular, 20 px no trilho (22 no item do trilho), 16 px no resto. Entram na v3: Library, ArrowLeft, Eye, EyeOff, Link e Filter, pelo `tools/Update-Icons.ps1`. Sparkle = IA, Warning = aviso, Check = ok, EyeOff = escondido, Link = sem casamento, Error Circle = erro, Arrow Sync = tarefa em andamento, Arrow Undo = restaurar.

## Como usar nos previews

Os previews do artefato do design system carregam `tokens.css` e `bundle.css`, que têm só classes `v3-*` e as variáveis `--nome-do-token`. Troque o tema com `data-theme="dark"` ou `"light"` no elemento. A galeria **GaleriaV3** reúne todos os controles na mesma ordem da galeria de hoje, e a imagem de hoje fica em Assets, grupo Hoje. Esses arquivos ficam no artefato e não vêm para esta pasta.

## Contraste e mudanças em relação à v2

A razão de contraste dos 172 pares está em [contraste.md](contraste.md). A lista do que entrou, mudou de valor e ficou igual nos tokens está em [mudancas-v2-v3.md](mudancas-v2-v3.md). O que o Avalonia desenha diferente do HTML do design system está em [avalonia.md](avalonia.md#o-que-o-avalonia-desenha-diferente-do-html).

## Interface v3 de outubro de 2026, em uso no app

As seções abaixo vêm do plano [2026-10-25-interface-v3.md](../plans/2026-10-25-interface-v3.md) e descrevem o que o app já tem nas telas e nos controles. Os tamanhos, os raios e as cores citados nelas valem até a migração das telas para o design system v3 (e4 do plano da v6); onde divergirem das seções acima, vale `tokens.json`.

### Termos da v3

A interface v3 fala a língua de quem traduz pela primeira vez (plano, seção "Textos"). A coluna **Estado** diz onde o nome está hoje: "No app" quer dizer que o texto da tela ou a classe já usa esse nome; "Ainda não" aponta a issue que o traz. Os nomes em código continuam os do Core (`QaFlags.MarkOk`, `GameInjector.Restore`, `TranslationStatus`): só o texto que a pessoa lê muda.

| Termo | Substitui | Onde aparece | Estado |
|---|---|---|---|
| **Testar** | Build, no trilho | Item do trilho; a tela abre com o título "Testar no jogo" | No app (#308) |
| **Testar no jogo** | Build e injeção | Botão principal da tela (`Ctrl+B`), o mesmo passo a passo do `vnf test` | No app (#308) |
| **Ler o texto** | Extrair | Etapa da jornada do Painel e passo da primeira vez no Início | Ainda não: o Painel diz "Extrair" até a #309 e o Início v3 vem na #313 |
| **Conferir a tradução** | Conferência (QA) | Título da tela 9 do canvas | No app (#334): título da página, nome do primeiro passo do teste no jogo (`GameTestStep.Check`) e da mensagem "Corrija em Conferir a tradução" da tela Testar. O trilho diz só "Conferir" (72 px), como diz "Testar" para Testar no jogo |
| **Voltar ao jogo original** | Restaurar originais | Botão neutro, sempre visível, da faixa de estado do jogo | No app (#308); em "Detalhes técnicos" da mesma tela o botão ainda é "Restaurar originais" |
| **Está certo assim** | Marcar como ok | Botão da tela Conferir a tradução que silencia um aviso de uma linha (`QaFlags.MarkOk` no Core) | No app (#334) |
| **virar a página** | `\p` | Selo dentro do texto (`MarkerLabels.Name`) | No app (#302) |
| **quebra de linha** | `\n` | Selo dentro do texto, com o desenho ↵ | No app (#302) |
| **esperar o clique**, **nome do jogador**, **código do jogo** | `\c`, variável de nome, outros códigos | Selos do `MarkerChip` | No app (#302) |
| **Falta um marcador do original** | Marcadores diferentes do original | Título do erro de marcador na lista de problemas: "Falta um marcador do original", "Sobra um marcador na tradução" ou "Faltam e sobram marcadores", com o detalhe "Falta virar a página." | No app (#334); o inspetor do Editor diz "Falta ou sobra um marcador" |
| **Pronta** | Revisada ou aprovada | Coluna **Situação** da grade do Editor | No app (#302) |
| **Sua** | Traduzida | Coluna Situação | No app (#302) |
| **IA** | Traduzida por IA | Coluna Situação | No app (#302) |
| **Em japonês** | Pendente | Coluna Situação e filtro do Editor | No app (#302) |
| **Cenas** | Scripts, na lista do Editor | Lista à esquerda do Editor; o agrupamento por capítulo (`ScriptGroup`) espera os grupos do Core da #309 | No app (#305), sem os grupos |
| **Caixa do jogo** | Prévia da caixa de texto | `GameTextbox`, com "Cabe · 2 de 3 linhas" ou "Passa 1 linha" | No app (#305) |
| **Dividir em 2 páginas**, **Encurtar com a IA** | | Saídas do aviso "Não cabe na caixa do jogo" (`FitWarning`) | No app (#306) |
| **Entender o japonês**, **Na cena**, **Usar em todos** | | Inspetor do Editor e modo revisão | No app (#305) |
| **Modo revisão** | | Botão do cabeçalho do Editor, com **Está boa**, **Corrigir**, **Depois** e **Pular** | No app (#307) |
| **Traduzir com IA** | Traduzir pendentes | Botão do cabeçalho do Editor e a janela das três perguntas | No app (#304) |
| **Rápida**, **Equilibrada**, **Caprichada** | Nome do modelo | `QualityCard`, sem o nome do modelo (fica em Detalhes técnicos dos Ajustes, #406) | No app (#303, #304, #312) |
| **Detalhes técnicos** | | `TechnicalDetails`, fechado por padrão: motor, pacote, página de código, modelo exato, registro, os nomes técnicos das estratégias de acentos | No app (#302) |
| **Acentos**, **Sem acentos**, **Com acentos, fonte modificada**, **Com acentos, ajuste no jogo** | Acentuação, "Remover acentos (fold)", "Remapear para meia largura (remap)", "Latin-1 nativo (cp1252)" | Aba dos Ajustes e passo do Novo projeto, os mesmos três cartões (`AccentCards`); o `sjis` não é cartão | No app (#312, #335) |
| **Precisa de você** | | Lista de pendências do Painel (`TodoItem`) | Ainda não: Painel na #309 |

O plano chama o botão do inspetor de "Aprovar e ir para a próxima"; o app diz **Aprovar e próxima**, que cabe nos 360 px do editor compacto. Vale o do app.

### Regras das telas da v3

Toda tela nova é medida por estas regras (plano, seção "Regras das telas"):

- Uma tela responde a uma pergunta e tem um botão principal.
- Linguagem comum: YPF, tokens, nome de modelo, página de código e caminho de pasta ficam em "Detalhes técnicos", nos Ajustes ou na CLI.
- No máximo três números em destaque por tela.
- Progresso em três estados (pronto, esperando revisão, em japonês), pela mesma fonte no anel e na barra; os cinco estados internos ficam no Core e na grade do Editor.
- Qualidade no lugar de modelo: Rápida (Haiku), Equilibrada (Sonnet), Caprichada (Opus, padrão).
- Marcadores do jogo nunca aparecem crus, e a caixa do jogo fica ao lado de toda tradução que a pessoa edita.
- Os números de exemplo são os mesmos em todas as telas (48.211 linhas em 312 scripts, 62% traduzido, R$ 142,56 gastos).
- Toda tela é conferida renderizada, a 1440×1000 nos dois temas, antes de ir para o canvas.


## Marca

- O selo 訳 ("tradução") em quadrado inclinado -4° é a marca. Use `vnforge-seal-dark.svg` sobre os temas escuros e `vnforge-seal-light.svg` sobre os claros; `vnforge-appicon.svg` (selo cheio) é o ícone da janela e da barra de tarefas.
- O nome é escrito **VNForge**, em uma palavra; a CLI é `vnf`.

## Componentes da v3

Os controles da v3 ficam em `src/VNForge.App/Controls` (código) e `src/VNForge.App/Themes/Controls` (estilo e template), com tokens `Vnf.*`. A galeria `Gallery/ComponentGalleryV3.axaml` mostra todos nos dois temas (`telas/v3/galeria-v3-escuro.png` e `galeria-v3-claro.png`). A coluna **Canvas** diz a classe de `v3.css` que o desenho usa. Os componentes da v2 seguem em `components/`; o `TextboxPreview` virou `GameTextbox`.

| Componente | Canvas | Propriedades principais | O que faz | Onde aparece |
|---|---|---|---|---|
| `ProgressRing` | `.ringwrap` | `Done`, `Review`, `Total`, `Caption` | Anel de três arcos (pronto, esperando revisão, em japonês) com a parte pronta no centro (uma casa decimal abaixo de 1%, como "0,7%"); a classe `sm` dá 56 px e só o número | Painel, Início (#309/#313) |
| `Seg3Bar` | `.seg3` | `Done`, `Review`, `Total` | Barra de três estados de 8 px, da mesma fonte do anel (`ThreeStateProgress`) | Editor, Painel, Início (#309/#313) |
| `ChapterRow` | `.chrow` | `Number`, `Title`, `Note`, `IsHere`, `IsExtra` | Linha do sumário de capítulos: número, nome, `Seg3Bar`, parte pronta (ou "N a revisar" quando há linhas esperando revisão, para não mostrar 0% ao lado de uma barra roxa), linhas e nota (número e linhas na fonte da interface com algarismos tabulares, alinhados à direita; a nota quebra em até duas linhas, `MaxLines` 2); `IsExtra` é "Menus e nomes", sem número | Painel (#309) |
| `TodoItem` | `.ready` | `Severity`, `Title`, `Message`, `ActionText`, `Command` | Item de "Precisa de você": ícone de gravidade, frase e o botão que resolve | Painel, Início (#309/#313) |
| `GameTextbox` | `.gbox` | `Layout`, `Size` (`Small`, `Medium`, `Large`, `Stage`), `FontFamily` | Caixa do jogo com a placa do nome, as linhas vagas pontilhadas, o que passa abaixo de um traço e o triângulo de virar a página; aceita várias páginas | Inspetor do Editor, palco do modo revisão (`Stage`, fonte 24), cartões de acentos do Novo projeto |
| `ReviewStage` | (nenhum, nasceu no app) | `SceneName`, `Content` (a `GameTextbox`) | O palco 16:9 do modo revisão: degradê vertical (`Vnf.StageTop` a `Vnf.StageBottom`, igual nos dois temas), vinheta (`Vnf.StageVignette`), contorno `Vnf.StageStroke`, cartão de capítulo (`Vnf.StageCard`) com o nome da cena e o conteúdo encostado na base, que encolhe num `Viewbox` se for grande demais; `Fit` calcula o maior 16:9 que cabe | Modo revisão |
| `StageColumn` | (nenhum) | `Spacing`, `MinStageHeight` (180) | Painel que empilha o palco e o que vem abaixo: mede o de baixo primeiro (na mesma largura em que organiza) e dá ao palco a altura que sobra, sem passar do mínimo; filho vazio não deixa vão | Modo revisão |
| `FitWarning` | `.fitwarn` | `Message`, `SplitCommand`, `ShortenCommand`, `ShowShorten`, `ErrorText` | Aviso "Não cabe na caixa do jogo" com "Dividir em N páginas" e "Encurtar com a IA" | Inspetor do Editor, modo revisão, Conferência |
| `MarkerChip` | `.mk` | `Marker`, `Kind`, `IsMissing` | Selo com o nome em português no lugar do marcador; no AvaloniaEdit entra pelo `MarkerChipGenerator` e ocupa uma coluna do cursor | Campo da tradução e original do inspetor do Editor, modo revisão |
| `CostLabel` | `.money` | `Brl`, `Usd` | Valor em reais; o dólar em letra pequena só quando preenchido (Ajustes e Novo projeto) | Telas de uso e Ajustes (#309/#313) |
| `BudgetBar` | `.budget` | `Spent`, `Ask`, `Limit` | Barra do gasto com a marca do limite; estado `:over` quando o gasto mais o pedido (zero, sem pedido) passa dele | Traduzir com IA, Ajustes; Painel (#309) |
| `AccentCards` | `.enc` | `Options` (`ChoiceOption`), `GroupName` | Os três cartões de acentos, cada um um `RadioButton` com nome, selo ("Em breve", "Recomendada para este jogo"), explicação e a frase de teste num `GameTextbox` pequeno; a seleção única fica no `ChoiceGroup` da tela, montado por `AccentChoices` | Novo projeto, Ajustes (aba Acentos) |
| `QualityCard` | `.opt` | `Title`, `Price`, `Duration`, `ExampleRate` | `OptionCard` com preço em reais e tempo em destaque, sem nome de modelo; é um `RadioButton` de grupo | Traduzir com IA, Novo projeto, Ajustes |
| `ContextLines` | `.ctx` | itens `ContextLine` (quem, original, tradução) | "Na cena": as falas em volta da linha aberta | Inspetor do Editor, modo revisão |
| `ExplainCard` | `.explain` | `Model`, `Footnote`, itens `ExplainPart` | "Entender o japonês": cartão tracejado com o original em partes e o que cada uma significa | Inspetor do Editor, modo revisão |
| `ScriptGroup` | `.grp` | `Title`, `CountText` | `Expander` de um capítulo na lista de cenas | Editor (quando o Core entregar os grupos, #309) |
| `GameStateBanner` | `.gamestate` | `State` (`Original`, `Translated`, `Blocked`, `Warning`), `Title`, `Message`, `Footer` | Cartão do que o jogo tem hoje, com "Voltar ao jogo original" sempre visível; o `Footer`, sob uma divisória, leva os cinco passos do teste (v4, #407) | Testar no jogo |
| `ScreenDialog` | `.scrim`, `.dialog` | `IsOpen`, `Title`, `Footer`, `CloseCommand` | Diálogo sobre a tela, na camada de sobreposição e sem janela nova; `Esc` fecha | Traduzir com IA, "Usar em todos" no inspetor do Editor |
| `TechnicalDetails` | `.tech` | `Header`, `Key` | `Expander` fechado por padrão para o que a pessoa não precisa ver; com `Key`, o estado aberto ou fechado volta ao revisitar a página (#382) | Novo projeto, Testar no jogo, Ajustes; Início (#313) |

`ProgressRing`, `Seg3Bar`, `ChapterRow`, `TodoItem`, `ScriptGroup` e `CostLabel` só aparecem na galeria por enquanto: as telas que os usam vêm com o Painel (#309) e o Início (#313). O `PipelineStepper` da v2 troca "Build" e "Injetar" por "Testar no jogo" e "Extrair" por "Ler o texto" quando o Painel v3 chegar. O `OptionCard` é a base do `QualityCard` e dos cartões de acentos (`.enc`, com um `GameTextbox` pequeno dentro).

## Telas copiadas

As capturas de `telas/v3/` saem do app renderizado sem janela (Avalonia.Headless com Skia), com o projeto de demonstração, nos dois temas, a 1440×1000 (o Editor e o modo revisão compactos, a 1366×768). Cada arquivo termina em `-escuro` ou `-claro`. O índice, tela a tela, está em [README.md](README.md#telas-da-v3).
