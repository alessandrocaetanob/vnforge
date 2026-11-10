# Button

Botão de comando. Padrão é o estilo neutro; **accent** marca a única ação principal da área (no inspetor, "Aprovar e próxima"; no Build, "Build e injetar").

## Variantes

| Variante | Classe Avalonia | Uso |
|---|---|---|
| Padrão | (nenhuma) | Ações secundárias: "Sugestão da IA", "Conferir arquivo" |
| accent | `accent` (já existe no Fluent) | Uma por região de tela |
| subtle | `subtle` | Ações em barras de ferramentas e itens de menu inline. Rótulo em semibold e text-secondary, text-primary no hover |
| danger | `danger` | "Restaurar originais", "Descartar tradução" — sempre com confirmação |
| sm | `sm` | Barras de comando sobre a grade (`control-sm`) |
| lg | `lg` | Tela vazia e diálogos (`control-lg`) |
| icon | `icon` | Só ícone; exige `ToolTip.Tip` e `AutomationProperties.Name` |

## O consumidor fornece

Rótulo com verbo no infinitivo, em caixa de frase ("Traduzir pendentes", não "Tradução"). Ícone opcional à esquerda (Fluent System Icons, Regular 20). Atalho opcional com `Kbd` à direita do rótulo.

## Avalonia

```xml
<Button Classes="accent" Command="{Binding ApproveAndNextCommand}" HotKey="Ctrl+Enter">
  <StackPanel Orientation="Horizontal" Spacing="8">
    <TextBlock Text="Aprovar e próxima"/>
    <Border Classes="kbd"><TextBlock Text="Ctrl+Enter"/></Border>
  </StackPanel>
</Button>
```

`subtle`, `danger`, `sm`, `lg` e `icon` são `Style Selector="Button.subtle"` etc. em `Themes/Controls/Buttons.axaml`, lendo os pincéis `Vnf.*`. Hover e pressed do botão comum e do `subtle` usam `bg-card-hover` no fundo e `text-primary` no texto; o `accent` usa `accent-hover`. Transição de 167 ms em `Background`.

O rótulo do `subtle` é semibold, como o do botão comum. Quando o botão carrega uma linha inteira (a linha da tabela do Eval, o capítulo do Painel), use `subtle evalrow` ou `subtle chapter`. Para qualquer outra linha, use `subtle row`: o texto dentro fica regular e em text-primary, e o hover e o disabled do botão continuam valendo. A classe `row` só vale junto de `subtle`.

O `Kbd` é um retângulo de raio 4, sem preenchimento, com contorno de 1px na cor do texto ao redor, mono 500 de 11px e opacidade 0,85.

## Não fazer

- Dois botões accent lado a lado.
- `danger` sem diálogo de confirmação.
- Botão só com ícone sem tooltip.
