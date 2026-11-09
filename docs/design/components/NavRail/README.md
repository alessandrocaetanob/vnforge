# NavRail

Navegação principal à esquerda, desde o shell novo (#544): ícone de 22px com rótulo curto de 11px embaixo, largura `navrail-width` (80px), item de 64px. O item atual fica em `accent-soft` com texto `accent-text`, sem barra lateral.

Dois itens: Biblioteca no alto e Ajustes fixo no rodapé. A Biblioteca continua marcada com um jogo aberto, porque o jogo é um item dela; clicar nela dentro do jogo volta à lista. Ajustes tira a marca da Biblioteca. O item Personagens do quadro entra com a página dele (#558).

O que antes eram itens do trilho (Painel, Editor, Conferir com o contador de erros, Testar, Glossário) vive na segunda linha da aba Tradução do jogo, e o Editor ganhou ao lado a Revisão. O cabeçalho do jogo (volta à Biblioteca, capa, nome, engine, andamento e o próximo passo) e as abas Ficha, Tradução e Jogar ficam acima da página, na área de conteúdo. Em janelas de menos de 820 px de altura o cabeçalho cabe numa linha só, sem a capa.

## Avalonia

`ListBox` com `ControlTheme` próprio dentro da coluna 0 do layout raiz (não `SplitView`: o trilho não recolhe). `Themes/Controls/NavRail.axaml` tem o estilo do trilho; `Themes/Controls/GameShell.axaml` tem o das abas (`ListBox.gametabs`), o da segunda linha (`ListBox.sections`) e a etiqueta e a pílula do cabeçalho. O cabeçalho é a `GameHeaderView`, ligada ao `MainWindowViewModel` (`InGame`, `GameTabs`, `Sections`). A página abaixo continua sendo o `ContentControl` com um `DataTemplate` por ViewModel.
