# TitleBar

Barra de título própria: selo, nome do app, projeto aberto, busca global no centro e os botões de janela do sistema.

- Fundo `bg-base` (sobre o Mica, transparente).
- A busca abre a paleta de comandos (`Ctrl+K`): linhas por texto ou id, scripts, personagens e comandos ("Build", "Traduzir pendentes").
- Altura `titlebar-height` (40px).

## Avalonia

```xml
<Window ExtendClientAreaToDecorationsHint="True"
        ExtendClientAreaTitleBarHeightHint="{StaticResource Vnf.TitlebarHeight}"
        TransparencyLevelHint="Mica" Background="Transparent">
  <Grid ColumnDefinitions="72,*">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="28"/>
    </Grid.RowDefinitions>
    <Border Grid.Row="0" Height="{StaticResource Vnf.TitlebarHeight}" WindowDecorationProperties.ElementRole="TitleBar">…</Border>
```

A altura vem do token `Vnf.TitlebarHeight` nos dois lugares: o hint da janela e a `Border` da barra (a linha do `Grid` é `Auto`). Sem o hint o Windows pinta a faixa do sistema na altura padrão (~32 px), que termina no meio da busca (#366).

Os botões minimizar, maximizar e fechar são os do sistema (`WindowDecorations="Full"`); a barra só reserva a área à direita. No Windows 11 a barra nativa acompanha o `RequestedThemeVariant`.
