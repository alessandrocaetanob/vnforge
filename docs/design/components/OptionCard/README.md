# OptionCard

Opção de escolha única em cartão: nome, valor à direita (preço, tamanho) e uma linha de descrição. Usado para modelo de IA, estratégia de acentuação e motor no assistente de novo projeto.

Preço por milhão de tokens, entrada / saída, em `mono`.

## Avalonia

`RadioButton` com `ControlTheme` próprio (`OptionCard`) cujo template é um `Border` com `Grid`; `:checked` aplica borda `accent` de 2px. Agrupar com `GroupName` e envolver em `ItemsControl` com `StackPanel Spacing="8"`.
