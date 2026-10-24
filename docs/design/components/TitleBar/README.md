# TitleBar

Barra de título própria: selo, nome do app, projeto aberto, busca global no centro e os botões de janela do sistema.

- Fundo `bg-base` (sobre o Mica, transparente).
- A busca abre a paleta de comandos (`Ctrl+K`): linhas por texto ou id, scripts, personagens e comandos ("Build", "Traduzir pendentes").
- Altura `titlebar-height` (40px).

## Avalonia

```xml
<Window ExtendClientAreaToDecorationsHint="True"
        TransparencyLevelHint="Mica" Background="Transparent">
  <Grid RowDefinitions="40,*,28">
    <Border Grid.Row="0" WindowDecorationProperties.ElementRole="TitleBar">…</Border>
```

Os botões minimizar, maximizar e fechar são os do sistema (`WindowDecorations="Full"`); a barra só reserva a área à direita. No Windows 11 a barra nativa acompanha o `RequestedThemeVariant`.
