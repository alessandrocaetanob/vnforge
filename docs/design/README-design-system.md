Sistema visual do VNForge v2, o aplicativo Windows de tradução de visual novels, implementado em Avalonia 12 sobre .NET 10. Os valores daqui viram recursos XAML (ver a seção **Implementação no Avalonia**); as prévias em HTML são a referência visual de cada controle.

## Conteúdo

- Interface em português do Brasil, caixa de frase em tudo: "Traduzir pendentes", "Build e injeção", "Conferência".
- Botões começam com verbo no infinitivo ("Aprovar e próxima", "Restaurar originais"). Itens de navegação são substantivos de uma palavra ("Editor", "Conferir", "Testar").
- Termos fixos do produto: **linha** (unidade de texto), **script** (arquivo `yst*.ybn`), **marcador** (`\p`, `\n`, `\c`, `{VAR:…}`), **perfil de formatação**, **build**, **injetar**, **restaurar**. Não alternar com sinônimos.
- Números e moeda no formato pt-BR: `48.211 linhas`, `US$ 0,84`, `62%`. Datas relativas até 7 dias ("há 2 min", "ontem"), depois `17/10/2026`.
- Nomes de personagem aparecem como original → tradução na primeira menção de uma tela (`美咲 → Misaki`) e só a tradução no resto.
- Mensagens dizem o fato e a consequência: "2 erros bloqueiam o build." Sem exclamação, sem emoji, sem "Ops".
- Atalhos escritos como no Windows: `Ctrl+Enter`, `Alt+↓`, `Ctrl+Espaço`.

## Cores

- O app segue o tema do Windows. **Escuro** é o tema de referência das telas; **Claro** tem os mesmos papéis.
- Camadas, de baixo para cima: `bg-base` (janela e reserva do Mica) → `bg-layer` (área de conteúdo) → `bg-card` (painéis, flyouts). Hover em `bg-card-hover`; campos em `bg-inset`.
- Painéis se separam por `stroke-subtle`, não por sombra. `shadow-flyout` só em menus, flyouts e diálogos.
- `accent` (azul ai, 藍) é a cor de interação: botão accent, seleção, foco, progresso, item ativo. Texto em destaque usa `accent-text`; fundos de seleção usam `accent-soft`.
- `brand-shu` (vermelhão shu, 朱) é só do selo 訳. Nunca em botões, status ou sinais.
- Status de linha usam os pares `status-*-bg` / `status-*-fg` e sempre levam o texto do status. IA é violeta para não competir com o âmbar dos avisos.
- Sinais do QA: aviso em `signal-warning-*` (âmbar), erro em `signal-error-*` (carmim, distinto do shu). Sucesso reaproveita `status-approved-*`.
- A prévia da caixa de texto usa `textbox-*`, iguais nos dois temas, porque representa o jogo.
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
- Estrutura da janela: `titlebar-height` 40, `navrail-width` 72, `statusbar-height` 28. No Editor, `scripts-width` 248 e `inspector-width` 400, ambos redimensionáveis com `GridSplitter`.
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

## Uso em canvas de design

Nas telas desenhadas em canvas, carregue `tokens.css` e depois `components/bundle.css`. As classes `vnf-*` do `bundle.css` reproduzem cada componente; o layout das telas fica em estilos inline com as variáveis dos tokens. O tema Claro é aplicado com `data-theme="light"` no elemento raiz.
