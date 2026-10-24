# Button

Botão de comando. Padrão é o estilo neutro; **accent** marca a única ação principal da área (no inspetor, "Aprovar e próxima"; no Build, "Build e injetar").

## Variantes

| Variante | Classe Avalonia | Uso |
|---|---|---|
| Padrão | (nenhuma) | Ações secundárias: "Sugestão da IA", "Conferir arquivo" |
| accent | `accent` (já existe no Fluent) | Uma por região de tela |
| subtle | `subtle` | Ações em barras de ferramentas e itens de menu inline |
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

`subtle`, `danger`, `sm`, `lg` e `icon` são `Style Selector="Button.subtle"` etc. em `Styles/Buttons.axaml`, lendo os pincéis `Vnf.*`. Hover e pressed usam `accent-hover` e `bg-card-hover`; transição de 167 ms em `Background`.

## Não fazer

- Dois botões accent lado a lado.
- `danger` sem diálogo de confirmação.
- Botão só com ícone sem tooltip.
