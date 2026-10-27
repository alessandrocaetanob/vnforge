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
│     ├─ SeverityTag.axaml
│     ├─ PipelineStepper.axaml
│     ├─ LineRow.axaml      # conteúdo da linha da grade e ListBox.lines
│     ├─ ScriptItem.axaml   # conteúdo do item, ListBox.scripts e ProgressBar.thin
│     ├─ ToggleRule.axaml
│     ├─ Segmented.axaml    # ListBox.seg e TabControl.tabs
│     ├─ FilterChip.axaml
│     ├─ AiSuggestion.axaml # com Run.ins e Run.del do diff
│     ├─ Legend.axaml
│     ├─ StatCard.axaml
│     ├─ PageHeader.axaml
│     ├─ NavRail.axaml
│     ├─ OptionCard.axaml
│     └─ InfoBar.axaml
└─ Assets/
   ├─ Fonts/                # NotoSans, NotoSansJP, ZenKakuGothicNew, JetBrainsMono (OFL)
   ├─ Icons.axaml           # StreamGeometry x:Key="Icon.Sparkle" …
   └─ VNForge.ico
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
        ExtendClientAreaTitleBarHeightHint="{StaticResource Vnf.TitlebarHeight}"
        WindowDecorations="Full"
        TransparencyLevelHint="Mica"
        Background="Transparent">
  <Panel>
    <Border Background="{DynamicResource Vnf.BgBase}"
            IsVisible="{Binding !IsMicaActive}"/>   <!-- reserva -->
    <Grid RowDefinitions="Auto,*,28" ColumnDefinitions="72,*">
      <Border Grid.ColumnSpan="2" Height="{StaticResource Vnf.TitlebarHeight}" WindowDecorationProperties.ElementRole="TitleBar"/>
      …
```

- A altura da barra vem de `Vnf.TitlebarHeight` (40) no hint da janela e na `Border` da barra. Sem o hint o Windows usa a altura padrão (~32 px) e a faixa do sistema termina no meio da busca (#366).
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

Cada controle fica em `src/VNForge.App/Controls` e a aparência em `Themes/Controls/<Nome>.axaml`, registrado no `App.axaml`. Os estados viram pseudo-classes (`:done`, `:error`…) que os estilos usam; as partes do template têm nome `PART_*` e são o que os testes procuram.

| Componente | Base | Mapeamento |
|---|---|---|
| `StatusBadge` | `TemplatedControl` | Propriedade `Status` (enum); estilos por `[Status=…]`; ponto vazado só no pendente |
| `GameTextbox` (era `TextboxPreview`) | `Control` com `Render` | Usa o `LinePreviewer` do `VNForge.Core` (o mesmo do build e do `vnf preview`); nada de quebra duplicada na UI. Desenha a placa do nome, todas as linhas que o perfil permite (as vagas só com o pontilhado), o que passa do limite abaixo de um traço, o triângulo de virar a página (oscila só sob a classe `motion` da janela, parado quando o Windows pede menos movimento) e, com várias caixas, o separador "página N". `Size`: `Small`, `Medium` (padrão) e `Large`. `GameTextbox.Describe(layout)` devolve a grade de linhas (`Text`, `Vacant`, `Overflow`, `PageBreak`); a redação "Cabe · 2 de 3 linhas" fica para a #305 |
| `MarkerChip` | `TemplatedControl` | `Marker` e `Kind`. Selo em pílula de 20 px com o nome em português (`\p` "virar a página", `\n` "quebra de linha", `{VAR:nome}` "nome do jogador"); `IsMissing` pinta de erro. No AvaloniaEdit entra pelo `MarkerChipGenerator`; nos textos só de leitura, num `InlineUIContainer` |
| `ProgressRing`, `Seg3Bar`, `ChapterRow` | `TemplatedControl` com `Done`, `Review`, `Total` | O progresso em três estados (pronto, esperando revisão, em japonês) a partir da mesma base (`ThreeStateProgress`). `ChapterRow` junta número, nome, `Seg3Bar`, porcentagem, linhas e nota; `:here` e `:extra` |
| `CostLabel`, `BudgetBar` | `TemplatedControl` | `CostLabel`: `Brl` e, opcional, `Usd`, em pt-BR fixo. `BudgetBar`: `Spent`, `Ask`, `Limit`, marca do limite e `:over` |
| `QualityCard` | `RadioButton` | O `OptionCard` com `Title`, `Description`, `Model`, `Price` (reais) e `Duration` |
| `ContextLines`, `ExplainCard` | `ItemsControl` | Itens `ContextLine(Who, Original, Translation)` e `ExplainPart(Source, Meaning)` |
| `ScriptGroup`, `TechnicalDetails` | `Expander` | Cabeçalho próprio; "Detalhes técnicos" começa fechado; com `Key` (`build`, `conference`, `start`) lembra aberto ou fechado entre visitas, em `technical-details.json` nas preferências do usuário (#382) |
| `GameStateBanner`, `TodoItem` | `ContentControl` e `TemplatedControl` | Estado do jogo (`:original`, `:translated`, `:blocked`) e item de "Precisa de você" (`:error`, `:warning`, `:ok`) |
| `ScreenDialog` | `ContentControl` | Diálogo na `OverlayLayer` do Avalonia, sobre a janela inteira: fundo escurecido, `IsOpen`, `Title`, `Footer`; Tab cicla dentro do cartão, `Escape` e o botão fechar fecham, e `Closed`/`CloseCommand` disparam também quando `IsOpen` vira falso por binding. O cartão sai da árvore da página, então estilos declarados só nela não alcançam o corpo |
| `InfoBar` | `Border.infobar` + classes `warning`, `error`, `success` | O Fluent do Avalonia não tem InfoBar |
| `OptionCard` | `RadioButton.option` | Template em `Border`; marcado, a borda vira accent de 2px |
| `PipelineStepper` | `ItemsControl` com `UniformGrid Rows=1`; itens `PipelineStepItem : Button` | Itens são `PipelineStep(Number, Title, Subtitle, State, Command?)`. `State` vira `:done` (círculo cheio em `seg-approved` com ✓) ou `:active` (número em `accent-text`, sublinhado de 2px). `PipelineStepper.Resolve(bool[])` marca a atual como a primeira não concluída. Cantos arredondados só na primeira e na última etapa (`:nth-child(1)`, `:nth-last-child(1)`) |
| `SeverityTag` | `TemplatedControl` | `Level` (`Error`, `Warning`, `Ok`) e `Count?`. Texto pelo `SeverityText.Format`: plural correto e milhar em pt-BR (`1.204 avisos`, `Sem problemas`). Ícone e cores pelas pseudo-classes `:error`, `:warning`, `:ok` |
| `LineRow` | `TemplatedControl` dentro de `ListBoxItem` | `Number`, `Status`, `Speaker`, `Original`, `Translation`, `Issue` (`Warning`, `Error`) e `IssueLabel`. Colunas `56 · 92 · 132 · 1* · 1.2* · 32` com gap de 8 e recuo de 12 (v4: o texto leva o que sobra), 36px. Narração e "sem tradução" em `text-secondary` (`:narration`, `:untranslated`). Seleção, hover e a barra de 3px vêm de `ListBox` com as classes `projects lines` |
| `ScriptItem` | `TemplatedControl` dentro de `ListBoxItem` | `Key`, `Label`, `Percent`, `IsRunning`. 100% pinta a barra em `seg-approved` (`:complete`); `IsRunning` mostra o ícone Arrow Sync em `status-ai-fg` (`:running`). O nome da cena (ou "Cena N") é o rótulo principal e o atual é o item selecionado de `ListBox.scripts`, com o nome em 600; o id fica pequeno, em mono e em `text-secondary`, embaixo (v4). A barra é `ProgressBar.thin` (4px). O ícone ainda não gira: a animação, desligada pela preferência de menos movimento, é do U9 |
| `ToggleRule` | `TemplatedControl` | `Title`, `Description` (um antes/depois real) e `IsChecked` em duas vias, num `ToggleSwitch` sem `OnContent` e `OffContent`. A última linha de um `Border.panel` perde a divisória |
| Controle segmentado | `ListBox.seg` | Seleção única, 30px, borda `stroke-control`; o segmento marcado fica em `accent` com `on-accent` em 600 |
| Abas | `TabControl.tabs` | Itens de 40px; a aba marcada fica em `text-primary` 600 com sublinhado de 3px em `accent` |
| Chip de filtro | `FilterChip : ToggleButton` | `Label`, `Count?` (milhar em pt-BR, em mono) e `IsChecked`. 28px, raio 4; marcado: `accent-soft`, borda `accent`, texto `accent-text` em 600 |
| `AiSuggestion` | `TemplatedControl` | `Model`, `Details`, `Current`, `Suggestion`, `IsBusy`, `UseCommand`, `CancelCommand`. O corpo é um `SelectableTextBlock` cujos `Inlines` saem de `WordDiff.Compute` (maior subsequência comum por palavra, sem os marcadores de pausa `\p`, `\c` e `\u`): `Run.ins` em `status-approved-*`, `Run.del` riscado em `text-secondary`. Sem tradução atual, o texto vai sem marcação. Gerando, aparece uma barra indeterminada e o botão vira "Cancelar" |
| Legenda | `Legend : ItemsControl` com `WrapPanel` | `ItemsSource` de `LegendEntry(Text, Tone)` ou `Value` com um `StatusDistribution` (Aprovadas, Revisadas, Traduzidas, IA e Pendentes, com os percentuais). `Tone` também cobre `Error`, `Warning` e `Accent`, para as legendas do Painel |
| Cartão de número | `StatCard : ContentControl` | `Title` (overline), `Value` (28/32 bold na fonte de exibição), `Description`, `Content` (a barra e a legenda) e `Footer` (o link de ação, num rodapé de 40px de borda a borda) |
| Cabeçalho de página | `PageHeader : ContentControl` | `Breadcrumb` (fonte da interface, caption 12/16), `Title` (`title-lg`) e `Content` (as ações, à direita, alinhadas à base) |

A distribuição de status é o `DistributionBar` (barra) mais o `Legend` (legenda). `Border.panel`, `Border.divider-*` e `ProgressBar.thin` completam o que o Painel usa.

## Galeria, projeto de demonstração e render das telas

- **Galeria** (`src/VNForge.App/Gallery`, só no build Debug): cada controle em todos os estados, escuro e claro lado a lado. Abre com `dotnet run --project src/VNForge.App -- --gallery`. No Release a pasta sai da compilação.
- **Projeto de demonstração** (`tests/VNForge.App.Tests/Demo/DemoProject.cs`): 312 scripts e 48.211 linhas com a distribuição do Painel (21% aprovadas, 18% revisadas, 6% traduzidas, 17% IA, 38% pendentes), os oito scripts da tabela "Por script" com as suas linhas, os 23 personagens com a contagem de linhas da tabela e o QA do artboard (2 erros e 37 avisos, em linhas que existem). `DemoProject.Create(pasta)` grava a pasta do projeto; `DemoProject.Lines()` entrega as linhas em memória. Nenhum arquivo de jogo: as falas são as do artboard. Os percentuais por script e por personagem do artboard, fora os oito scripts, não são reproduzidos.
- **Render das telas** (`tests/VNForge.App.Tests/Rendering`): o Avalonia.Headless com Skia desenha a janela principal a 1440×1000 nos dois temas e grava `inicio`, `painel`, `editor`, `conferir`, `build` e `ajustes` (mais `ajustes-ia`, `ajustes-formatacao` e `ajustes-acentos`, uma por aba) como `<tela>-escuro.png` e `<tela>-claro.png`. A pasta de saída é `VNFORGE_RENDER_DIR`; sem ela, uma pasta temporária. Para gerar os PNGs e pô-los ao lado do artboard num PR de tela:

```powershell
$env:VNFORGE_RENDER_DIR = "$PWD\artifacts\render"
dotnet test --project tests/VNForge.App.Tests --filter-class "*ScreenRenderTests"
```

  A sessão headless usa `UseSkia()` com desenho real, então os testes de layout medem texto de verdade. O teste `FontsHeadlessTests` confere que `title-lg` (26/38) e `title-md` resolvem para a Zen Kaku Gothic New embutida.

## Ícones

`Assets/Icons.axaml` com um `StreamGeometry` por ícone usado, copiado do `d` do SVG Regular 20 (viewBox 0 0 20 20) do Fluent UI System Icons. Uso: `<PathIcon Data="{StaticResource Icon.Sparkle}" Width="16" Height="16"/>`; a cor vem de `Foreground`.

## Acessibilidade

- `AutomationProperties.Name` em todo botão `icon`, no indicador de aviso da grade e nos chips de marcador.
- Ordem de tabulação: barra de título → trilho → lista de scripts → grade → inspetor.
- `Alt+↑/↓` move a linha selecionada a partir de qualquer lugar do Editor (`KeyBinding` na `UserControl`).
