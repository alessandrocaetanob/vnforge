# LineRow

Uma linha da grade de tradução: número, status, quem fala, original, tradução e indicador do QA. A grade é somente leitura; a edição acontece no inspetor.

- Altura fixa `row-height` (36px) para a virtualização não recalcular medidas.
- Selecionada: fundo `accent-soft` + barra de 3px em `accent` à esquerda.
- Narração e "sem tradução" em `text-secondary`, sem itálico.
- Indicador: ícone Warning em `signal-warning-fg` ou Error Circle em `signal-error-fg`, com `AutomationProperties.Name`.

## Colunas

`56px · 92px · 132px · 1* · 1.2* · 32px`, gap 8 e recuo de 12 nas bordas (v4: Linha, Situação, Quem fala e o aviso têm largura fixa e pequena; Original e Tradução dividem o resto e a tradução leva um pouco mais, porque o português é mais longo). Original em `jp-row`; demais em `body-sm`; id em `mono`.

## Avalonia

`ListBox` com `ItemsPanel` = `VirtualizingStackPanel` e `ItemTemplate` com `Grid ColumnDefinitions="56,92,132,*,1.2*,32"`. Seleção, hover e foco vêm do `ControlTheme` de `ListBoxItem` (`^:selected`, `^:pointerover`, `^:focus-visible`). Não usar `TreeDataGrid`: a versão mantida virou parte do Avalonia Accelerate (comercial); a grade aqui não precisa de edição em célula, ordenação por coluna nem hierarquia.

Atalhos da grade: `Alt+↑/↓` move a seleção mesmo com o foco no editor; `Ctrl+F` abre a busca; `Enter` foca o editor.
