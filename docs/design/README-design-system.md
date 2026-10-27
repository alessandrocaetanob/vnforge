Sistema visual do VNForge, o aplicativo Windows de tradução de visual novels, implementado em Avalonia 12 sobre .NET 10. A base é a v2 (tokens, tipografia, componentes); a interface v3 (plano [2026-10-25-interface-v3.md](../plans/2026-10-25-interface-v3.md)) acrescenta termos, regras de tela e os componentes da seção **Componentes da v3**. Os valores daqui viram recursos XAML (ver `avalonia.md`); as prévias em HTML são a referência visual de cada controle e as capturas em `telas/v3/` mostram o app renderizado.

## Conteúdo

- Interface em português do Brasil, caixa de frase em tudo: "Traduzir com IA", "Testar no jogo", "Voltar ao jogo original".
- Botões começam com verbo no infinitivo ("Aprovar e próxima", "Restaurar originais"). Itens de navegação são substantivos de uma palavra ("Editor", "Conferir", "Testar").
- Termos fixos do produto: **linha** (unidade de texto), **script** (arquivo `yst*.ybn`), **marcador** (`\p`, `\n`, `\c`, `{VAR:…}`), **perfil de formatação**, **build**, **injetar**, **restaurar**. Não alternar com sinônimos. Na v3, build, injetar, restaurar e o marcador cru saem das telas de uso (ver **Termos da v3**) e seguem no log, na CLI e na documentação técnica, onde são as palavras certas.
- Números e moeda no formato pt-BR: `48.211 linhas`, `R$ 142,56`, `62%`. As telas de uso mostram dinheiro só em reais; o dólar e a cotação aparecem nos Ajustes e no Novo projeto, onde a pessoa informa a cotação. Valor abaixo de um centavo é "menos de R$ 0,01", nunca R$ 0,00. Datas relativas até 7 dias ("há 2 min", "ontem"), depois `17/10/2026`.
- Nomes de personagem aparecem como original → tradução na primeira menção de uma tela (`美咲 → Misaki`) e só a tradução no resto.
- Mensagens dizem o fato e a consequência: "2 erros bloqueiam o build." Sem exclamação, sem emoji, sem "Ops".
- Atalhos escritos como no Windows: `Ctrl+Enter`, `Alt+↓`, `Ctrl+Espaço`.

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

## Cores

- O app segue o tema do Windows. **Escuro** é o tema de referência das telas; **Claro** tem os mesmos papéis.
- Camadas, de baixo para cima: `bg-base` (janela e reserva do Mica) → `bg-layer` (área de conteúdo) → `bg-card` (painéis, flyouts). Hover em `bg-card-hover`; campos em `bg-inset`.
- Painéis se separam por `stroke-subtle`, não por sombra. `shadow-flyout` só em menus, flyouts e diálogos.
- `accent` (azul ai, 藍) é a cor de interação: botão accent, seleção, foco, progresso, item ativo. Texto em destaque usa `accent-text`; fundos de seleção usam `accent-soft`.
- `brand-shu` (vermelhão shu, 朱) é só do selo 訳. Nunca em botões, status ou sinais.
- Status de linha usam os pares `status-*-bg` / `status-*-fg` e sempre levam o texto do status. IA é violeta para não competir com o âmbar dos avisos.
- Sinais do QA: aviso em `signal-warning-*` (âmbar), erro em `signal-error-*` (carmim, distinto do shu). Sucesso reaproveita `status-approved-*`.
- A prévia da caixa de texto usa `textbox-*` e o palco do modo revisão usa `stage-*` (`stage-top`, `stage-bottom`, `stage-vignette`, `stage-card`, `stage-stroke`), todos iguais nos dois temas, porque representam o jogo.
- Todo texto em `text-primary` e `text-secondary` passa 4,5:1 sobre as superfícies citadas no uso de cada token, nos dois temas. `text-tertiary` só em desabilitado e placeholder.

## Tipografia

- Interface em `body` (Noto Sans 14/20), igual ao tamanho padrão do Fluent. Listas densas e grade em `body-sm` (13/18).
- Texto japonês sempre na família `jp` (Noto Sans JP): `jp-body` no inspetor, `jp-row` na grade. Não deixar o kanji cair no fallback da família `ui`.
- Títulos de página em `title-lg` e de painel em `title-md`, na família `display` (Zen Kaku Gothic New), um título de página por tela.
- Rótulos de campo e cabeçalhos de coluna em `overline` (12/600, caixa normal, sem espaçamento entre letras).
- Ids, caminhos e log em `mono` (JetBrains Mono); chips de marcador e teclas em `mono-xs`.

## Espaço, tamanho e forma

- Grade de 4px: `space-1` a `space-8`. Padding de painel `space-4`; gap entre painéis `space-3`; margem da área de conteúdo `space-6`.
- Densidade: controles padrão em `control-md` (32); barras de comando sobre a grade em `control-sm` (28); ação principal de tela vazia em `control-lg` (40).
- Linha da grade com altura fixa `row-height` (36) para virtualização estável.
- Estrutura da janela: `titlebar-height` 40, `navrail-width` 72, `statusbar-height` 28. No Editor, `scripts-width` 190 (teto da lista de cenas) e `inspector-width` 400, ambos redimensionáveis com `GridSplitter`.
- Painéis lado a lado na mesma linha têm a mesma altura: no Avalonia, `Grid` com colunas `*` ou fixas e o painel ocupando a célula inteira (nunca `StackPanel` horizontal, que deixa cada painel com a altura do próprio conteúdo). Cabeçalho de painel com 48px e rodapé com 40px ancorado embaixo, para o espaço livre ficar no corpo.
- Painéis-resumo com o mesmo papel (os indicadores do Painel, as tabelas por script e por personagem) dividem a largura em partes iguais.
- Tabelas-resumo usam linha de `row-height` e barra de 8px (`StatusDistribution`) numa coluna de 120 a 180px, com o percentual em coluna própria. A barra de 4px fica só em itens de navegação (`ScriptItem`) e na barra de status.
- Raios: `radius-sm` (4) em controles e chips, `radius-md` (8) em painéis e flyouts, `radius-pill` em badges e contadores.

## Estados e movimento

- Foco de teclado: anel sólido de 2px em `focus-ring`, afastado 1px do controle, em todo elemento focável.
- Hover e pressed trocam fundo em 167 ms (ease-out). Painéis e flyouts entram em 250 ms. Com "Efeitos de animação" desligado no Windows, sem transição.
- Desabilitado: texto `text-tertiary`, borda `stroke-subtle`, sem hover.
- Seleção em listas: fundo `accent-soft` e barra vertical de 3px em `accent` à esquerda.

## Iconografia

- Fluent UI System Icons (Microsoft, MIT), estilo **Regular**, 20px no trilho de navegação e 16px no resto. Os arquivos estão no grupo Icons; no Avalonia viram `StreamGeometry` em `PathIcon`.
- Ícone acompanha texto, exceto em botões `icon`, que exigem tooltip e nome acessível.
- Ícones de significado fixo: Sparkle = IA; Warning = aviso; Error Circle = erro; Checkmark Circle = ok; Arrow Sync = tarefa em andamento; Arrow Undo = restaurar.

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
| `GameStateBanner` | `.gamestate` | `State` (`Original`, `Translated`, `Blocked`, `Warning`), `Title`, `Message` | Faixa do que o jogo tem hoje, com "Voltar ao jogo original" sempre visível | Testar no jogo |
| `ScreenDialog` | `.scrim`, `.dialog` | `IsOpen`, `Title`, `Footer`, `CloseCommand` | Diálogo sobre a tela, na camada de sobreposição e sem janela nova; `Esc` fecha | Traduzir com IA, "Usar em todos" no inspetor do Editor |
| `TechnicalDetails` | `.tech` | `Header`, `Key` | `Expander` fechado por padrão para o que a pessoa não precisa ver; com `Key`, o estado aberto ou fechado volta ao revisitar a página (#382) | Novo projeto, Testar no jogo, Ajustes; Início (#313) |

`ProgressRing`, `Seg3Bar`, `ChapterRow`, `TodoItem`, `ScriptGroup` e `CostLabel` só aparecem na galeria por enquanto: as telas que os usam vêm com o Painel (#309) e o Início (#313). O `PipelineStepper` da v2 troca "Build" e "Injetar" por "Testar no jogo" e "Extrair" por "Ler o texto" quando o Painel v3 chegar. O `OptionCard` é a base do `QualityCard` e dos cartões de acentos (`.enc`, com um `GameTextbox` pequeno dentro).

## Telas copiadas

As capturas de `telas/v3/` saem do app renderizado sem janela (Avalonia.Headless com Skia), com o projeto de demonstração, nos dois temas, a 1440×1000 (o Editor e o modo revisão compactos, a 1366×768). Cada arquivo termina em `-escuro` ou `-claro`. O índice, tela a tela, está em [README.md](README.md#telas-da-v3).

## Uso em canvas de design

Nas telas desenhadas em canvas, carregue `tokens.css` e depois `components/bundle.css`. As classes `vnf-*` do `bundle.css` reproduzem cada componente; o layout das telas fica em estilos inline com as variáveis dos tokens. O tema Claro é aplicado com `data-theme="light"` no elemento raiz.
