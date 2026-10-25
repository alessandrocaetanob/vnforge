# NavRail

Navegação principal à esquerda: ícone de 20px com rótulo curto embaixo, largura `navrail-width` (72px). Item atual em `bg-card` com barra de 3px `accent` e texto `accent-text`.

Itens, nesta ordem: Início, Painel, Editor, Conferir (com contador de avisos + erros), Testar (era "Build" até a v3), Glossário; Ajustes fixo no rodapé. Rótulos de uma palavra. Sem projeto aberto, só Início e Ajustes ficam habilitados.

## Avalonia

`ListBox` com `ControlTheme` próprio dentro da coluna 0 do layout raiz (não `SplitView`: o trilho não recolhe). Cada item vincula a uma página por `ContentControl` + `DataTemplate` por ViewModel. Contador com `Border Classes="count"` usando `signal-warning-*`; se houver erro bloqueante, troca para `signal-error-*`.
