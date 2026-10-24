# LineRow

Uma linha da grade de tradução: número, status, personagem, original, tradução e indicador do QA. A grade é somente leitura; a edição acontece no inspetor.

- Altura fixa `row-height` (36px) para a virtualização não recalcular medidas.
- Selecionada: fundo `accent-soft` + barra de 3px em `accent` à esquerda.
- Narração e "sem tradução" em `text-secondary`, sem itálico.
- Indicador: ícone Warning em `signal-warning-fg` ou Error Circle em `signal-error-fg`, com `AutomationProperties.Name`.

## Colunas

`52px · 96px · 96px · 1* · 1* · 24px`, gap `space-3`. Original em `jp-row`; demais em `body-sm`; id em `mono`.

## Avalonia

`ListBox` com `ItemsPanel` = `VirtualizingStackPanel` e `ItemTemplate` com `Grid ColumnDefinitions="52,96,96,*,*,24"`. Seleção, hover e foco vêm do `ControlTheme` de `ListBoxItem` (`^:selected`, `^:pointerover`, `^:focus-visible`). Não usar `TreeDataGrid`: a versão mantida virou parte do Avalonia Accelerate (comercial); a grade aqui não precisa de edição em célula, ordenação por coluna nem hierarquia.

Atalhos da grade: `Alt+↑/↓` move a seleção mesmo com o foco no editor; `Ctrl+F` abre a busca; `Enter` foca o editor.
