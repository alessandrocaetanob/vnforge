# Implementação no Avalonia

Alvo: Avalonia 12 (lançado em 7/4/2026, compila para .NET 10, o mesmo runtime do VNForge), tema `FluentTheme`, MVVM com CommunityToolkit.Mvvm. Só Windows na primeira versão, mas nada abaixo depende do Windows além do Mica e da barra de título.

## Estrutura dos recursos

```
src/VNForge.App/
├─ App.axaml
├─ Themes/
│  ├─ Tokens.axaml          # cores (por tema), tamanhos, raios, espaçamentos, fontes
│  ├─ Typography.axaml      # Styles para TextBlock.title-lg, .body-sm, .overline…
│  └─ Controls/             # um ControlTheme por arquivo
│     ├─ Buttons.axaml      # Button.subtle, .danger, .sm, .lg, .icon, Border.kbd
│     ├─ StatusBadge.axaml
│     ├─ LineRow.axaml      # ControlTheme de ListBoxItem da grade
│     ├─ NavRail.axaml
│     ├─ OptionCard.axaml
│     └─ InfoBar.axaml
└─ Assets/
   ├─ Fonts/                # NotoSans, NotoSansJP, ZenKakuGothicNew, JetBrainsMono (OFL)
   ├─ Icons.axaml           # StreamGeometry x:Key="Icon.Sparkle" …
   └─ vnforge.ico
```

## Tokens → recursos

Cada token de cor vira um `Color` e um `SolidColorBrush` com prefixo `Vnf.`, dentro de `ThemeDictionaries` para o Escuro e o Claro. Controles sempre usam `{DynamicResource}` para trocar de tema sem reiniciar.

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <ResourceDictionary.ThemeDictionaries>
    <ResourceDictionary x:Key="Dark">
      <Color x:Key="Vnf.BgCardColor">#1B1F27</Color>
      <SolidColorBrush x:Key="Vnf.BgCard" Color="{StaticResource Vnf.BgCardColor}"/>
      <SolidColorBrush x:Key="Vnf.TextPrimary" Color="#E7EAF0"/>
      <SolidColorBrush x:Key="Vnf.StatusAiBg" Color="#2C2440"/>
      <!-- … um por token de tokens.json -->
    </ResourceDictionary>
    <ResourceDictionary x:Key="Light">
      <Color x:Key="Vnf.BgCardColor">#FFFFFF</Color>
      <!-- … -->
    </ResourceDictionary>
  </ResourceDictionary.ThemeDictionaries>

  <x:Double x:Key="Vnf.RowHeight">36</x:Double>
  <x:Double x:Key="Vnf.ControlSm">28</x:Double>
  <CornerRadius x:Key="Vnf.RadiusMd">8</CornerRadius>
  <Thickness x:Key="Vnf.PanelPadding">16</Thickness>
</ResourceDictionary>
```

Regra de nome: `bg-card` → `Vnf.BgCard`, `status-ai-fg` → `Vnf.StatusAiFg`. O arquivo é gerado a partir de `tokens.json` por um script em `tools/` para que o design system continue sendo a fonte.

## Fluent: paleta e densidade

O Fluent cobre os controles comuns (TextBox, ComboBox, ToggleSwitch, ScrollViewer, menus). Ele recebe a paleta do VNForge pelos `ColorPaletteResources`, um por variante:

```xml
<FluentTheme DensityStyle="Normal">
  <FluentTheme.Palettes>
    <ColorPaletteResources x:Key="Dark"  Accent="#8DB0EA" RegionColor="#15181E" ErrorText="#F59AB2"/>
    <ColorPaletteResources x:Key="Light" Accent="#2B4C7E" RegionColor="#F4F5F8" ErrorText="#9C1F45"/>
  </FluentTheme.Palettes>
</FluentTheme>
```

`Accent` fixo desliga a cor de destaque do Windows: o azul ai faz parte da identidade e os contrastes foram calculados para ele. Só `Accent` pode mudar em tempo de execução; os demais valores da paleta são lidos na inicialização.

`DensityStyle="Compact"` não é usado globalmente; a densidade compacta é aplicada por classe (`sm`) só nas barras de comando e na grade.

## Fontes

As quatro famílias vão embutidas como `AvaloniaResource` em `Assets/Fonts` e são referenciadas com fallback explícito para o japonês:

```xml
<FontFamily x:Key="Vnf.FontUi">avares://VNForge.App/Assets/Fonts#Noto Sans, avares://VNForge.App/Assets/Fonts#Noto Sans JP</FontFamily>
<FontFamily x:Key="Vnf.FontJp">avares://VNForge.App/Assets/Fonts#Noto Sans JP</FontFamily>
<FontFamily x:Key="Vnf.FontMono">avares://VNForge.App/Assets/Fonts#JetBrains Mono</FontFamily>
<FontFamily x:Key="Vnf.FontDisplay">avares://VNForge.App/Assets/Fonts#Zen Kaku Gothic New</FontFamily>
```

Noto Sans JP completa passa de 4 MB por peso; usar só os pesos 400 e 500. Subconjunto de glifos não é seguro: o texto vem do jogo.

## Janela: barra de título e Mica

```xml
<Window ExtendClientAreaToDecorationsHint="True"
        WindowDecorations="Full"
        TransparencyLevelHint="Mica"
        Background="Transparent">
  <Panel>
    <Border Background="{DynamicResource Vnf.BgBase}"
            IsVisible="{Binding !IsMicaActive}"/>   <!-- reserva -->
    <Grid RowDefinitions="40,*,28" ColumnDefinitions="72,*">
      <Border Grid.ColumnSpan="2" WindowDecorationProperties.ElementRole="TitleBar"/>
      …
```

- O Mica só aparece com `Background="Transparent"`. O Windows desliga a transparência em sessão remota e em economia de bateria; `IsMicaActive` vem de `ActualTransparencyLevel` e a reserva `bg-base` cobre o fundo.
- `bg-layer` (área de conteúdo) é sempre opaco: o Mica aparece só na barra de título, no trilho e na barra de status.
- No Avalonia 12, `SystemDecorations` passou a se chamar `WindowDecorations`.

## Grade de tradução

| Opção | Situação | Decisão |
|---|---|---|
| `ListBox` + `VirtualizingStackPanel` | Núcleo, MIT | **Usar.** A grade é somente leitura, linha de altura fixa, seleção única |
| `DataGrid` | Pacote separado `Avalonia.Controls.DataGrid` | Não precisa: edição em célula e ordenação não fazem parte do fluxo |
| `TreeDataGrid` | A versão mantida é um fork comercial do Avalonia Accelerate; a original MIT segue em repositório próprio, sem evolução | Não usar |

Filtros, busca e ordenação ficam no ViewModel; a grade recebe a lista já filtrada. O `EditorViewModel` troca `Lines` por inteiro a cada filtro ou script, em vez de usar uma coleção observável, porque `Add` e `Remove` em dezenas de milhares de linhas gerariam um evento por item e travariam a tela. Os `LineViewModel` do script são criados uma vez e o filtro só escolhe entre eles. O teste headless `EditorGridHeadlessTests` abre a tela com 50 mil linhas geradas em código e confere que a virtualização realiza só as linhas visíveis e que rolar até o fim e até o meio leva menos de 2 segundos.

A lista de scripts mostra a chave (`yst00013`) enquanto o nome legível do `yst_list.ybn` não é lido; quando existir, ele entra como segunda linha do item.

## Editor de tradução

`AvaloniaEdit.TextEditor` (pacote `Avalonia.AvaloniaEdit`, MIT) com um `VisualLineElementGenerator` que transforma marcadores em chips atômicos e um `IBackgroundRenderer` para sublinhar termos do glossário. Versão fixada: `Avalonia.AvaloniaEdit` **12.0.0**, a primeira da linha 12 (as 11.x declaram Avalonia 11); o `.nuspec` exige `Avalonia >= 12.0.0`, compatível com o 12.1.x do app. Fonte: NuGet (`api.nuget.org/v3-flatcontainer/avalonia.avaloniaedit`) e repositório AvaloniaUI/AvaloniaEdit.

O painel de detalhe (`LineDetailView`) usa o `TextEditor` com o `MarkerChipGenerator` (`Controls/`). A busca dos marcadores é a função pura `MarkerScanner`, com a mesma gramática do `ControlMarkers` do Core, mas tolerante a marcador incompleto enquanto o usuário digita. O chip é um `InlineObjectElement`: ocupa uma coluna visual, então o cursor o salta e Backspace o apaga inteiro. O `Border` não tem borda tracejada; o contorno é um `Rectangle` interno (tracejado para controle, contínuo para variável). O code-behind só liga `TextEditor.Text` ao ViewModel; a gravação com debounce de 400 ms (`TimeProvider`) fica no `LineDetailViewModel`, que grava o pendente ao trocar de linha, aprovar ou ser descartado (`Dispose`).

## Controles próprios

| Controle | Base | Observação |
|---|---|---|
| `StatusBadge` | `TemplatedControl` | Propriedade `Status` (enum); estilos por `^[Status=…]` |
| `TextboxPreview` | `Control` com `Render` | Usa o `LinePreviewer` do `VNForge.Core` (o mesmo do build e do `vnf preview`); nada de quebra duplicada na UI |
| `InfoBar` | `TemplatedControl` | O Fluent do Avalonia não tem InfoBar |
| `PipelineStepper` | `ItemsControl` + `ControlTheme` de `Button` | Pseudo-classes `:done`, `:active` |
| `OptionCard` | `ControlTheme` de `RadioButton` | Template em `Border` + `Grid` |

## Ícones

`Assets/Icons.axaml` com um `StreamGeometry` por ícone usado, copiado do `d` do SVG Regular 20 (viewBox 0 0 20 20) do Fluent UI System Icons. Uso: `<PathIcon Data="{StaticResource Icon.Sparkle}" Width="16" Height="16"/>`; a cor vem de `Foreground`.

## Acessibilidade

- `AutomationProperties.Name` em todo botão `icon`, no indicador de aviso da grade e nos chips de marcador.
- Ordem de tabulação: barra de título → trilho → lista de scripts → grade → inspetor.
- `Alt+↑/↓` move a linha selecionada a partir de qualquer lugar do Editor (`KeyBinding` na `UserControl`).
