using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Images;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Core.Images;
using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Engines.YuRis;

namespace VNForge.App.ViewModels;

/// <summary>Os filtros da faixa de imagens do editor (#535).</summary>
public enum ImageFilter
{
    All,
    WithJapanese,
    Edited,
    Untranslated,
    WithoutText,
}

/// <summary>Um chip de filtro da faixa de imagens: o rótulo, quantas imagens passam e se é o filtro ativo.</summary>
public sealed partial class ImageFilterChipViewModel(ImageFilter filter, string label) : ObservableObject
{
    public ImageFilter Filter { get; } = filter;

    public string Label { get; } = label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountLabel))]
    public partial int Count { get; set; }

    public string CountLabel => ProjectLabels.Number(Count);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>As ferramentas da barra acima das prévias: escolher a área, apagar e escrever o texto.</summary>
public enum ImageTool
{
    SelectArea,
    Erase,
    Text,
}

/// <summary>Uma amostra de cor do texto: a cor em <c>#rrggbb</c> e se é a que está valendo.</summary>
public sealed partial class ColorSwatchViewModel(string hex) : ObservableObject
{
    public string Hex { get; } = hex;

    /// <summary>Cor clara demais para se destacar do fundo da tela: ganha um fio de contorno.</summary>
    public bool IsLight { get; } = ImageEditSerializer.IsColor(hex) && Luminance(hex) > 0.8;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    private static double Luminance(string hex) =>
        ((0.2126 * Convert.ToInt32(hex.Substring(1, 2), 16)) + (0.7152 * Convert.ToInt32(hex.Substring(3, 2), 16)) + (0.0722 * Convert.ToInt32(hex.Substring(5, 2), 16))) / 255;
}

/// <summary>Onde a imagem está no caminho até o jogo.</summary>
public enum ImageState
{
    Pending,
    Draft,
    Marked,
}

/// <summary>Uma imagem do inventário na faixa de miniaturas.</summary>
public sealed partial class ImageItemViewModel(ImageTextEntry entry, Func<ImageItemViewModel, Task>? loadThumbnail = null) : ObservableObject
{
    private byte[]? thumbnail;
    private bool thumbnailRequested;
    private string? thumbnailError;

    public ImageTextEntry Entry { get; } = entry;

    public string Archive => Entry.Archive;

    public string Path => Entry.Path;

    public string Name => System.IO.Path.GetFileName(Entry.Path);

    public bool HasText => Entry.HasText;

    public string JapaneseText => Entry.Text;

    /// <summary>Nome, tamanho e pacote da imagem, numa linha.</summary>
    public string Summary => $"{Name} · {Entry.Width}×{Entry.Height} · {Entry.Archive}";

    /// <summary>A tradução que o modelo sugeriu junto com a leitura; vazia nos inventários de antes dela.</summary>
    public string Translation => Entry.Translation ?? string.Empty;

    public bool HasTranslation => Translation.Length > 0;

    public string Caption => Summary + (ThumbnailError is null ? string.Empty : $"\nSem miniatura: {ThumbnailError}");

    /// <summary>Por que a miniatura não saiu; aparece na dica da imagem na faixa.</summary>
    public string? ThumbnailError
    {
        get => thumbnailError;
        set
        {
            if (SetProperty(ref thumbnailError, value))
            {
                OnPropertyChanged(nameof(Caption));
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State), nameof(StateText), nameof(IsMarkedState), nameof(IsDraftState), nameof(IsPendingState))]
    public partial ImageEdit? Edit { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>A miniatura em PNG. Quem lê pela primeira vez (a faixa, ao mostrar o item) dispara a leitura em segundo plano; até lá é nula.</summary>
    public byte[]? ThumbnailPng
    {
        get
        {
            if (!thumbnailRequested)
            {
                thumbnailRequested = true;
                ThumbnailLoading = loadThumbnail?.Invoke(this) ?? Task.CompletedTask;
            }

            return thumbnail;
        }
        set => SetProperty(ref thumbnail, value);
    }

    /// <summary>A leitura da miniatura, para os testes esperarem.</summary>
    public Task ThumbnailLoading { get; private set; } = Task.CompletedTask;

    public bool IsMarkedState => State == ImageState.Marked;

    public bool IsDraftState => State == ImageState.Draft;

    public bool IsPendingState => State == ImageState.Pending;

    public ImageState State => Edit is null ? ImageState.Pending : Edit.Marked ? ImageState.Marked : ImageState.Draft;

    public string StateText => State switch
    {
        ImageState.Marked => "Editada e marcada para o build",
        ImageState.Draft => "Editada, ainda fora do build",
        _ => HasText ? "Com japonês, sem tradução" : "Sem texto",
    };
}

/// <summary>Um arquivo que o diálogo "Regravar no YPF" vai trocar.</summary>
public sealed record RewriteFile(string Path, string Detail);

/// <summary>
/// O editor de imagens com texto (#535): a pessoa escolhe uma imagem do inventário (<c>images.json</c>), apaga a área do texto, escreve a
/// tradução com a fonte que quiser e vê original e traduzida lado a lado. "Marcar para o build" grava a imagem pronta em <c>images/</c> e a edição
/// em <c>image-edits.json</c>; o build (<see cref="ImageBuild"/>) troca só essas imagens no pacote do jogo.
/// </summary>
public sealed partial class ImagesViewModel : ObservableObject, IDisposable
{
    private static readonly string[] Editable = ["image/png"];
    private static readonly string[] SwatchPresets = ["#1d1d2a", "#f1eee6", "#5a4630"];
    private const string NoOutline = "Nenhum";

    private readonly IReadOnlyList<IEngine> engines;
    private readonly IImageComposer composer;
    private ProjectFolder? folder;
    private string gameDirectory = string.Empty;
    private Func<string, string>? originalOf;
    private List<ImageEdit> edits = [];
    private RasterPicture? source;
    private byte[] sourceHash = [];
    private ImageEdit? saved;
    private bool settingFields;
    private readonly TimeSpan previewDelay;
    private readonly SemaphoreSlim thumbnailGate = new(3);
    private CancellationTokenSource? previewCts;
    private bool syncingOutline;
    private ImageScanPlan? scanPlan;
    private CancellationTokenSource? scanCts;

    public ImagesViewModel(IReadOnlyList<IEngine>? engines = null, IImageComposer? composer = null, TimeSpan? previewDelay = null)
    {
        this.previewDelay = previewDelay ?? TimeSpan.FromMilliseconds(150);
        this.engines = engines ?? [new YuRisEngine()];
        this.composer = composer ?? new AvaloniaImageComposer();
        RefreshSwatches();
        Chips =
        [
            new ImageFilterChipViewModel(ImageFilter.All, "Todas") { IsSelected = true },
            new ImageFilterChipViewModel(ImageFilter.WithJapanese, "Com japonês"),
            new ImageFilterChipViewModel(ImageFilter.Edited, "Editadas"),
            new ImageFilterChipViewModel(ImageFilter.Untranslated, "Sem tradução"),
            new ImageFilterChipViewModel(ImageFilter.WithoutText, "Sem texto"),
        ];
    }

    /// <summary>O painel pediu o build: quem hospeda leva a pessoa à página Testar e roda o build, que grava as imagens marcadas no pacote.</summary>
    public event EventHandler? BuildRequested;

    /// <summary>Quem lê as imagens com a IA: recebe o modelo e devolve o leitor. Sem ele (sem chave de API) a tela avisa em vez de ler.</summary>
    public Func<string, IImageTextReader>? ReaderFactory { get; set; }

    public Func<ExchangeRate> Rate { get; set; } = () => ExchangeRate.Placeholder;

    public Func<BudgetLimits> Limits { get; set; } = () => BudgetLimits.None;

    /// <summary>Abre um arquivo no programa do sistema; os testes trocam por um falso.</summary>
    public Action<string> OpenPath { get; set; } = OpenInFileManager;

    // O modelo que lê as imagens: o mais barato com visão.
    private static string ScanModel => ClaudeModels.Haiku;

    public ObservableCollection<ImageItemViewModel> Items { get; } = [];

    public ObservableCollection<ColorSwatchViewModel> TextSwatches { get; } = [];

    public ObservableCollection<ResultFact> ScanFacts { get; } = [];

    public ObservableCollection<ImageItemViewModel> Visible { get; } = [];

    public IReadOnlyList<ImageFilterChipViewModel> Chips { get; }

    public IReadOnlyList<string> FontChoices => composer.FontFamilies;

    public ObservableCollection<RewriteFile> ConfirmationFiles { get; } = [];

    public ObservableCollection<ResultFact> ConfirmationFacts { get; } = [];

    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>A leitura da imagem escolhida; os testes esperam por ela.</summary>
    public Task Selecting { get; private set; } = Task.CompletedTask;

    /// <summary>A prévia em andamento (espera o debounce, monta a imagem fora da thread de UI); os testes esperam por ela.</summary>
    public Task Previewing { get; private set; } = Task.CompletedTask;

    public IReadOnlyList<string> EraseChoices { get; } = ["Fundo ao redor", "Cor lisa"];

    /// <summary>O modo de apagar como o controle segmentado mostra.</summary>
    public string SelectedEraseChoice
    {
        get => UsesSolidErase ? EraseChoices[1] : EraseChoices[0];
        set => UsesSolidErase = value == EraseChoices[1];
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectTool), nameof(IsEraseTool), nameof(IsTextTool))]
    public partial ImageTool ActiveTool { get; set; } = ImageTool.SelectArea;

    /// <summary>O tamanho da fonte como o campo mostra: "18 px". Aceita o número com ou sem o "px"; o que não for número volta ao que vale.</summary>
    public string FontSizeText
    {
        get => $"{Plain(FontSize)} px";
        set
        {
            var digits = value.Replace("px", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            if (double.TryParse(digits, NumberStyles.Float, CultureInfo.CurrentCulture, out var size) || double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out size))
            {
                FontSize = size;
            }

            OnPropertyChanged();
        }
    }

    public bool IsSelectTool => ActiveTool == ImageTool.SelectArea;

    public bool IsEraseTool => ActiveTool == ImageTool.Erase;

    public bool IsTextTool => ActiveTool == ImageTool.Text;

    [ObservableProperty]
    public partial ProjectSummary? Project { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; private set; }

    public bool IsEmpty => !IsLoading && Items.Count == 0;

    [ObservableProperty]
    public partial ImageItemViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    public partial bool IsEditable { get; private set; }

    public bool HasImage => SelectedItem is not null;

    /// <summary>Bytes da imagem original, em PNG, para a tela mostrar.</summary>
    [ObservableProperty]
    public partial byte[]? OriginalPng { get; private set; }

    /// <summary>Bytes da prévia (apagada e com o texto), em PNG.</summary>
    [ObservableProperty]
    public partial byte[]? PreviewPng { get; private set; }

    [ObservableProperty]
    public partial int PixelWidth { get; private set; }

    [ObservableProperty]
    public partial int PixelHeight { get; private set; }

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Font { get; set; } = "Zen Kaku Gothic New";

    [ObservableProperty]
    public partial double FontSize { get; set; } = 18;

    [ObservableProperty]
    public partial bool IsBold { get; set; } = true;

    [ObservableProperty]
    public partial string TextColor { get; set; } = "#1d1d2a";

    [ObservableProperty]
    public partial bool HasOutline { get; set; }

    /// <summary>O campo Contorno: <c>Nenhum</c> ou uma cor <c>#rrggbb</c>. Texto que não é nenhum dos dois é ignorado e o campo volta ao que vale quando a imagem é aberta de novo.</summary>
    [ObservableProperty]
    public partial string OutlineText { get; set; } = NoOutline;

    [ObservableProperty]
    public partial string OutlineColor { get; set; } = "#ffffff";

    [ObservableProperty]
    public partial bool UsesSolidErase { get; set; }

    [ObservableProperty]
    public partial string EraseColor { get; set; } = "#ffffff";

    [ObservableProperty]
    public partial int RegionX { get; set; }

    [ObservableProperty]
    public partial int RegionY { get; set; }

    [ObservableProperty]
    public partial int RegionWidth { get; set; }

    [ObservableProperty]
    public partial int RegionHeight { get; set; }

    /// <summary>Quantos pixels da tela cada pixel da imagem ocupa na prévia do original; a tela informa, para a moldura e as alças terem o mesmo traço em qualquer zoom.</summary>
    [ObservableProperty]
    public partial double OverlayScale { get; set; } = 1;

    private double ScaleOrOne => OverlayScale > 0.01 ? OverlayScale : 1;

    public double OverlayStroke => 2 / ScaleOrOne;

    public double HandleSize => 10 / ScaleOrOne;

    public double HandleLeft => RegionX - (HandleSize / 2);

    public double HandleRight => RegionX + RegionWidth - (HandleSize / 2);

    public double HandleTop => RegionY - (HandleSize / 2);

    public double HandleBottom => RegionY + RegionHeight - (HandleSize / 2);

    [ObservableProperty]
    public partial string FitText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool FitsRegion { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    public partial bool IsDirty { get; private set; }

    public bool CanUndo => IsDirty && HasImage;

    /// <summary>Em que o pacote da imagem aberta se chama na tela: o formato (YPF), ou "pacote" quando não dá para dizer.</summary>
    public string PackageLabel => SelectedItem is { } item ? $"No {FormatName(item.Archive) ?? "pacote"}" : "No pacote";

    public bool CanOpenInventory => Items.Count > 0;

    /// <summary>O texto do botão que abre o diálogo da regravação: "Regravar no YPF" quando as imagens marcadas vivem num formato só.</summary>
    public string RewriteLabel
    {
        get
        {
            var formats = Items.Where(i => i.State == ImageState.Marked).Select(i => FormatName(i.Archive)).Distinct(StringComparer.Ordinal).ToList();
            return formats.Count == 1 && formats[0] is { } name ? $"Regravar no {name}" : "Regravar no pacote";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRewrite), nameof(MarkedText))]
    public partial int MarkedCount { get; private set; }

    public bool CanRewrite => MarkedCount > 0;

    public string MarkedText => $"{ProjectLabels.Number(MarkedCount)} {(MarkedCount == 1 ? "imagem alterada" : "imagens alteradas")} de {ProjectLabels.Number(Items.Count)}";

    [ObservableProperty]
    public partial bool IsMarked { get; private set; }

    [ObservableProperty]
    public partial string ErasedText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string NewTextSummary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string PackageText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirming))]
    public partial string ConfirmationTitle { get; private set; } = string.Empty;

    public bool IsConfirming => ConfirmationTitle.Length > 0;

    /// <summary>O rótulo do botão que confirma, com a conta das imagens: "Regravar 3 imagens".</summary>
    [ObservableProperty]
    public partial string ConfirmationButton { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScanConfirming))]
    public partial string ScanTitle { get; private set; } = string.Empty;

    public bool IsScanConfirming => ScanTitle.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScanBusy))]
    public partial bool IsScanning { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScanBusy))]
    public partial bool IsPreparingScan { get; private set; }

    public bool IsScanBusy => IsScanning || IsPreparingScan;

    [ObservableProperty]
    public partial string ScanProgressText { get; private set; } = string.Empty;

    /// <summary>A leitura das imagens em andamento; os testes esperam por ela.</summary>
    public Task Scanning { get; private set; } = Task.CompletedTask;

    partial void OnProjectChanged(ProjectSummary? value) => Loading = LoadAsync(value);

    partial void OnSelectedItemChanged(ImageItemViewModel? oldValue, ImageItemViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }

        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(PackageLabel));
        Selecting = SelectAsync(newValue);
    }

    partial void OnTextChanged(string value) => FieldsChanged();

    partial void OnFontChanged(string value) => FieldsChanged();

    partial void OnFontSizeChanged(double value)
    {
        OnPropertyChanged(nameof(FontSizeText));
        FieldsChanged();
    }

    partial void OnIsBoldChanged(bool value) => FieldsChanged();

    partial void OnTextColorChanged(string value)
    {
        RefreshSwatches();
        FieldsChanged();
    }

    partial void OnHasOutlineChanged(bool value)
    {
        SyncOutlineText();
        FieldsChanged();
    }

    partial void OnOutlineColorChanged(string value)
    {
        SyncOutlineText();
        FieldsChanged();
    }

    partial void OnOutlineTextChanged(string value)
    {
        if (syncingOutline)
        {
            return;
        }

        var text = value.Trim();
        syncingOutline = true;
        try
        {
            if (text.Length == 0 || string.Equals(text, NoOutline, StringComparison.OrdinalIgnoreCase))
            {
                HasOutline = false;
            }
            else if (ImageEditSerializer.IsColor(text))
            {
                OutlineColor = text.ToLowerInvariant();
                HasOutline = true;
            }
        }
        finally
        {
            syncingOutline = false;
        }
    }

    partial void OnOverlayScaleChanged(double value) => RefreshOverlay();

    partial void OnUsesSolidEraseChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectedEraseChoice));
        FieldsChanged();
    }

    partial void OnEraseColorChanged(string value) => FieldsChanged();

    partial void OnRegionXChanged(int value) => RegionChanged();

    partial void OnRegionYChanged(int value) => RegionChanged();

    partial void OnRegionWidthChanged(int value) => RegionChanged();

    partial void OnRegionHeightChanged(int value) => RegionChanged();

    private void RegionChanged()
    {
        RefreshOverlay();
        FieldsChanged();
    }

    private void RefreshOverlay()
    {
        OnPropertyChanged(nameof(OverlayStroke));
        OnPropertyChanged(nameof(HandleSize));
        OnPropertyChanged(nameof(HandleLeft));
        OnPropertyChanged(nameof(HandleRight));
        OnPropertyChanged(nameof(HandleTop));
        OnPropertyChanged(nameof(HandleBottom));
    }

    // O campo Contorno mostra "Nenhum" ou a cor; quem manda é HasOutline com OutlineColor.
    private void SyncOutlineText()
    {
        if (syncingOutline)
        {
            return;
        }

        syncingOutline = true;
        try
        {
            OutlineText = HasOutline ? OutlineColor : NoOutline;
        }
        finally
        {
            syncingOutline = false;
        }
    }

    // As três amostras do quadro, mais uma para a cor de uma edição antiga que não é nenhuma delas.
    private void RefreshSwatches()
    {
        var current = TextColor.ToLowerInvariant();
        var shown = SwatchPresets.Contains(current, StringComparer.Ordinal) || !ImageEditSerializer.IsColor(current) ? SwatchPresets : [.. SwatchPresets, current];
        if (!TextSwatches.Select(w => w.Hex).SequenceEqual(shown))
        {
            TextSwatches.Clear();
            foreach (var hex in shown)
            {
                TextSwatches.Add(new ColorSwatchViewModel(hex));
            }
        }

        foreach (var swatch in TextSwatches)
        {
            swatch.IsSelected = swatch.Hex == current;
        }
    }

    /// <summary>Escolhe a ferramenta da barra. Só "Selecionar área" muda o que o arrastar sobre o original faz; as outras levam a pessoa ao campo delas.</summary>
    [RelayCommand]
    private void UseTool(ImageTool tool) => ActiveTool = tool;

    /// <summary>Põe a cor do texto: uma das amostras.</summary>
    [RelayCommand]
    private void PickTextColor(string hex) => TextColor = hex;

    /// <summary>Se o ponteiro está numa alça da área, devolve o canto oposto, que fica parado enquanto a alça é arrastada.</summary>
    public bool TryResizeAnchor(double x, double y, out (double X, double Y) anchor)
    {
        var (left, top, right, bottom) = (RegionX, RegionY, RegionX + RegionWidth, RegionY + RegionHeight);
        var reach = HandleSize;
        (double X, double Y, double AnchorX, double AnchorY)[] corners = [(left, top, right, bottom), (right, top, left, bottom), (left, bottom, right, top), (right, bottom, left, top)];
        foreach (var corner in corners.Where(c => Math.Abs(x - c.X) <= reach && Math.Abs(y - c.Y) <= reach))
        {
            anchor = (corner.AnchorX, corner.AnchorY);
            return true;
        }

        anchor = default;
        return false;
    }

    /// <summary>Escolhe o filtro da faixa e refaz a lista.</summary>
    [RelayCommand]
    private void SetFilter(ImageFilter filter)
    {
        foreach (var chip in Chips)
        {
            chip.IsSelected = chip.Filter == filter;
        }

        Refilter();
    }

    /// <summary>Define a área do texto original, em pixels da imagem (a seleção feita na tela). Mantém a área dentro da imagem.</summary>
    public void SetRegion(int x, int y, int width, int height)
    {
        var clipped = new ImageRegion(x, y, width, height).ClipTo(PixelWidth, PixelHeight);
        if (clipped.Width < 1 || clipped.Height < 1)
        {
            return;
        }

        settingFields = true;
        (RegionX, RegionY, RegionWidth, RegionHeight) = (clipped.X, clipped.Y, clipped.Width, clipped.Height);
        settingFields = false;
        FieldsChanged();
    }

    /// <summary>Grava a imagem pronta e a edição, e põe a imagem no build.</summary>
    [RelayCommand]
    private Task MarkAsync() => PersistAsync(marked: true);

    /// <summary>Tira a imagem do build sem perder a edição.</summary>
    [RelayCommand]
    private Task UnmarkAsync() => PersistAsync(marked: false);

    /// <summary>Volta os campos ao que está gravado (ou ao padrão, se a imagem nunca foi editada).</summary>
    [RelayCommand]
    private void Undo()
    {
        if (SelectedItem is null || source is null)
        {
            return;
        }

        LoadFields(saved ?? Defaults(SelectedItem, source));
    }

    /// <summary>Abre o <c>images.json</c> no programa que o sistema tem para ele.</summary>
    [RelayCommand]
    private void OpenInventory()
    {
        if (folder is not null && File.Exists(folder.ImageInventoryPath))
        {
            OpenPath(folder.ImageInventoryPath);
        }
    }

    /// <summary>Lista as imagens do jogo, estima o custo e abre o diálogo que pede o OK antes de chamar a IA.</summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        if (folder is null || IsScanBusy)
        {
            return;
        }

        if (!CanCreateReader())
        {
            ErrorMessage = "Falta a chave de API. Configure-a nos Ajustes para a IA ler as imagens do jogo.";
            return;
        }

        ErrorMessage = null;
        IsPreparingScan = true;
        using var preparing = new CancellationTokenSource();
        scanCts = preparing;
        try
        {
            var project = folder;
            var directory = gameDirectory;
            var formats = engines.SelectMany(e => e.Archives).ToList();
            var (plan, estimate) = await Task.Run(
                () =>
                {
                    var listing = ImageLister.List(directory, formats, null, preparing.Token);
                    var made = ImageScanPlan.Build(listing, project.LoadImageInventory());
                    return (made, ImageScanCost.Estimate(made.ToRead, ScanModel));
                },
                preparing.Token);
            if (plan.Candidates.Count == 0)
            {
                ErrorMessage = "Não achei imagens que a IA possa ler nos pacotes do jogo.";
                return;
            }

            var rate = Rate();
            var cost = CostEstimator.Brl(estimate, rate);
            if (Limits().Limit is { } limit && rate.ToBrl(AiUsageLog.Load(project).Dollars) + cost > limit)
            {
                ErrorMessage = $"Parou antes de chamar a IA: o gasto do projeto mais esta leitura (cerca de {Money.Brl(cost)}) passa do limite de {Money.Brl(limit)}. Aumente o limite nos Ajustes.";
                return;
            }

            scanPlan = plan;
            if (plan.ToRead.Count == 0)
            {
                Scanning = RunScanAsync(plan);
                return;
            }

            ScanFacts.Clear();
            ScanFacts.Add(new ResultFact("Imagens nos pacotes do jogo", ProjectLabels.Number(plan.Candidates.Count)));
            ScanFacts.Add(new ResultFact("Já lidas", ProjectLabels.Number(plan.Reused)));
            ScanFacts.Add(new ResultFact("Vão à IA", ProjectLabels.Number(plan.ToRead.Count)));
            ScanFacts.Add(new ResultFact("Custo estimado", Money.Brl(cost)));
            ScanTitle = plan.ToRead.Count == 1 ? "Ler 1 imagem com a IA?" : $"Ler {ProjectLabels.Number(plan.ToRead.Count)} imagens com a IA?";
        }
        catch (OperationCanceledException)
        {
            // A pessoa cancelou a preparação: nada foi pedido à IA e nada a mostrar.
        }
        catch (Exception ex) when (ex is VNForgeException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Não foi possível listar as imagens do jogo: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(scanCts, preparing))
            {
                scanCts = null;
            }

            IsPreparingScan = false;
        }
    }

    [RelayCommand]
    private void ConfirmScan()
    {
        if (scanPlan is not { } plan)
        {
            return;
        }

        ScanTitle = string.Empty;
        Scanning = RunScanAsync(plan);
    }

    /// <summary>Fecha o diálogo da leitura sem chamar a IA.</summary>
    [RelayCommand]
    private void CancelScan()
    {
        ScanTitle = string.Empty;
        if (!IsScanning)
        {
            scanPlan = null;
        }
    }

    /// <summary>Cancela a leitura em andamento; o que já foi lido fica no inventário.</summary>
    [RelayCommand]
    private void StopScan() => scanCts?.Cancel();

    private async Task RunScanAsync(ImageScanPlan plan)
    {
        if (folder is not { } project || ReaderFactory is not { } factory)
        {
            return;
        }

        using var cts = new CancellationTokenSource();
        scanCts = cts;
        IsScanning = true;
        ScanProgressText = plan.ToRead.Count == 0 ? "Gravando o inventário..." : "Abrindo os pacotes do jogo...";
        try
        {
            ImageScanResult result;
            if (plan.ToRead.Count == 0)
            {
                result = ImageTextScanner.WithoutReading(plan);
            }
            else
            {
                var progress = new Progress<ImageScanProgress>(p => ScanProgressText = $"Lendo as imagens · {ProjectLabels.Number(p.Done)} de {ProjectLabels.Number(p.Total)}");
                result = await new ImageTextScanner(factory(ScanModel), engines.SelectMany(e => e.Archives).ToList())
                    .ScanAsync(gameDirectory, plan, new ImageScanRun(), progress, cts.Token);
            }

            // O gasto entra na conta antes da gravação: a API já cobrou, e uma falha ao gravar o inventário não apaga essa conta.
            if (result.InputTokens + result.OutputTokens > 0)
            {
                RunCostTracker.Record(project, ScanModel, new ClaudeUsage(result.InputTokens, result.OutputTokens, 0, 0));
            }

            if (!result.Entries.SequenceEqual(project.LoadImageInventory()))
            {
                project.SaveImageInventory(result.Entries);
            }

            ErrorMessage = ScanProblem(result);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Leitura cancelada. O que já foi lido ficou no inventário.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = $"A leitura das imagens parou: {ex.Message}";
        }
        finally
        {
            scanCts = null;
            scanPlan = null;
            IsScanning = false;
            ScanProgressText = string.Empty;
        }

        var error = ErrorMessage;
        await LoadAsync(Project);
        ErrorMessage = error;
    }

    private static string? ScanProblem(ImageScanResult result)
    {
        if (result.AbortReason is { } reason)
        {
            return reason;
        }

        if (result.Failed.Count > 0)
        {
            return $"{ProjectLabels.Number(result.Failed.Count)} imagens não puderam ser lidas (a primeira: {result.Failed[0].Message}). Leia de novo para tentar só as que faltam.";
        }

        return result.Cancelled ? "Leitura cancelada. O que já foi lido ficou no inventário." : null;
    }

    // Sem leitor ou sem chave (a fábrica lança ao montar o cliente) não adianta listar nem estimar.
    private bool CanCreateReader()
    {
        try
        {
            return ReaderFactory is not null && ReaderFactory(ScanModel) is not null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void OpenInFileManager(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            // Sem programa para o arquivo (sessão remota, CI) o comando não tem outro destino; só registra.
            Debug.WriteLine(e);
        }
    }

    /// <summary>Abre o diálogo que diz o que o build vai regravar nos pacotes do jogo.</summary>
    [RelayCommand]
    private async Task RewriteAsync()
    {
        if (folder is null || MarkedCount == 0)
        {
            return;
        }

        ConfirmationFiles.Clear();
        ConfirmationFacts.Clear();
        var marked = Items.Where(i => i.State == ImageState.Marked).ToList();
        var touched = marked.Select(i => i.Archive).Distinct(StringComparer.Ordinal).ToList();
        // Abrir os pacotes e listar a pasta do jogo leva tempo num jogo grande; fora da thread de UI.
        var (archives, sizes) = await Task.Run(() => (CountArchives(), touched.ToDictionary(a => a, EntrySizes, StringComparer.Ordinal)), CancellationToken.None);
        foreach (var item in marked)
        {
            var file = folder.EditedImagePath(item.Archive, item.Path);
            var after = Kilobytes(File.Exists(file) ? new FileInfo(file).Length : 0);
            var detail = sizes[item.Archive].TryGetValue(item.Path, out var before) ? $"{item.Archive} · {Kilobytes(before)} para {after}" : $"{item.Archive} · {after}";
            ConfirmationFiles.Add(new RewriteFile(item.Name, detail));
        }

        var formats = touched.Select(FormatName).Distinct(StringComparer.Ordinal).ToList();
        var format = formats.Count == 1 ? formats[0] : null;
        var entries = sizes.Values.Sum(s => s.Count);
        ConfirmationFacts.Add(new ResultFact(format is null ? "Pacotes tocados" : $"Arquivos {format} tocados", $"{ProjectLabels.Number(touched.Count)} de {ProjectLabels.Number(archives)}"));
        ConfirmationFacts.Add(new ResultFact("Entradas que ficam byte a byte iguais", ProjectLabels.Number(Math.Max(0, entries - marked.Count))));
        ConfirmationFacts.Add(new ResultFact("Cópia de segurança antes de gravar", "sim"));
        var count = marked.Count == 1 ? "1 imagem" : $"{ProjectLabels.Number(marked.Count)} imagens";
        ConfirmationButton = $"Regravar {count}";
        ConfirmationTitle = $"Regravar {count} {(format is null ? "nos pacotes do jogo" : $"no {format}")}?";
    }

    [RelayCommand]
    private void CancelRewrite() => ConfirmationTitle = string.Empty;

    [RelayCommand]
    private void ConfirmRewrite()
    {
        ConfirmationTitle = string.Empty;
        BuildRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadAsync(ProjectSummary? summary)
    {
        Items.Clear();
        Visible.Clear();
        SelectedItem = null;
        ReplacePreviewToken(createNew: false);
        folder = null;
        ErrorMessage = null;
        if (summary is null || summary.IsSample)
        {
            RefreshCounts();
            return;
        }

        IsLoading = true;
        try
        {
            var loaded = await Task.Run(() => ReadProject(summary.Root), CancellationToken.None);
            folder = loaded.Folder;
            gameDirectory = loaded.GameDirectory;
            originalOf = loaded.OriginalOf;
            edits = [.. loaded.Edits];
            foreach (var entry in loaded.Inventory)
            {
                Items.Add(new ImageItemViewModel(entry, LoadThumbnailAsync) { Edit = edits.FirstOrDefault(e => e.Archive == entry.Archive && e.Path == entry.Path) });
            }
        }
        catch (Exception ex) when (ex is VNForgeException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ErrorMessage = $"Não foi possível abrir as imagens do projeto: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }

        RefreshCounts();
        Refilter();
    }

    private sealed record LoadedProject(ProjectFolder Folder, string GameDirectory, Func<string, string> OriginalOf, IReadOnlyList<ImageTextEntry> Inventory, IReadOnlyList<ImageEdit> Edits);

    private static LoadedProject ReadProject(string root)
    {
        var project = ProjectFolder.Open(root);
        var game = Path.GetFullPath(project.LoadConfig().Game.Path, project.Root);
        return new LoadedProject(project, game, GameInjector.CreateOriginalResolver(project, game), project.LoadImageInventory(), project.LoadImageEdits());
    }

    private async Task SelectAsync(ImageItemViewModel? item)
    {
        ReplacePreviewToken(createNew: false);
        source = null;
        saved = null;
        IsEditable = false;
        OriginalPng = null;
        PreviewPng = null;
        IsMarked = false;
        IsDirty = false;
        FitText = string.Empty;
        ErasedText = NewTextSummary = PackageText = string.Empty;
        if (item is null || folder is null)
        {
            return;
        }

        ErrorMessage = null;
        byte[] bytes;
        try
        {
            bytes = await Task.Run(() => ReadOriginal(item), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (ReferenceEquals(SelectedItem, item))
            {
                ErrorMessage = $"Não foi possível ler {item.Name} no pacote: {ex.Message}";
            }

            return;
        }

        if (!ReferenceEquals(SelectedItem, item))
        {
            return;
        }

        var format = ImageProbe.Read(bytes)?.MediaType;
        if (format is null || !Editable.Contains(format))
        {
            ErrorMessage = $"{item.Name} não é PNG. Por enquanto o editor grava só imagens PNG no pacote.";
            return;
        }

        RasterPicture decoded;
        try
        {
            decoded = await Task.Run(() => composer.Decode(bytes), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (ReferenceEquals(SelectedItem, item))
            {
                ErrorMessage = $"{ex.Message} ({item.Name})";
            }

            return;
        }

        if (!ReferenceEquals(SelectedItem, item))
        {
            return;
        }

        source = decoded;
        sourceHash = SHA256.HashData(bytes);
        if (!string.Equals(Convert.ToHexStringLower(sourceHash), item.Entry.Hash, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = $"{item.Name} mudou no jogo depois que a lista foi feita. O texto lido nela está desatualizado e precisa ser lido de novo.";
        }

        PixelWidth = source.Width;
        PixelHeight = source.Height;
        OriginalPng = bytes;
        saved = item.Edit;
        IsEditable = true;
        LoadFields(saved ?? Defaults(item, source));
        IsDirty = false;
        await Previewing;
    }

    private byte[] ReadOriginal(ImageItemViewModel item)
    {
        var path = originalOf?.Invoke(item.Archive) ?? Path.Combine(gameDirectory, item.Archive);
        if (!File.Exists(path))
        {
            path = Path.Combine(gameDirectory, item.Archive.Replace('/', Path.DirectorySeparatorChar));
        }

        var extension = Path.GetExtension(path);
        var stream = File.OpenRead(path);
        try
        {
            var format = engines.SelectMany(e => e.Archives).FirstOrDefault(f => f.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) && f.CanOpen(stream))
                ?? throw new VNForgeException($"o formato do pacote {item.Archive} não é reconhecido.");
            using var archive = format.Open(stream);
            var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Path, item.Path, StringComparison.Ordinal))
                ?? throw new VNForgeException($"a imagem não está mais em {item.Archive}.");
            using var content = archive.OpenEntry(entry);
            using var copy = new MemoryStream();
            content.CopyTo(copy);
            return copy.ToArray();
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private ImageEdit Defaults(ImageItemViewModel item, RasterPicture picture)
    {
        var width = Math.Max(8, picture.Width * 2 / 5);
        var height = Math.Max(8, picture.Height / 8);
        return new ImageEdit(
            item.Archive, item.Path, Convert.ToHexStringLower(sourceHash), picture.Width, picture.Height,
            new ImageRegion((picture.Width - width) / 2, (picture.Height - height) / 2, width, height).ClipTo(picture.Width, picture.Height),
            ImageEraseModes.Surrounding, null, string.Empty, "Zen Kaku Gothic New", Math.Max(10, Math.Round(height * 0.6)), true, "#1d1d2a", null, false);
    }

    private void LoadFields(ImageEdit edit)
    {
        settingFields = true;
        Text = edit.Text;
        Font = edit.Font;
        FontSize = edit.FontSize;
        IsBold = edit.Bold;
        TextColor = edit.Color;
        HasOutline = edit.Outline is not null;
        OutlineColor = edit.Outline ?? "#ffffff";
        UsesSolidErase = edit.Erase == ImageEraseModes.Solid;
        EraseColor = edit.EraseColor ?? "#ffffff";
        (RegionX, RegionY, RegionWidth, RegionHeight) = (edit.Region.X, edit.Region.Y, edit.Region.Width, edit.Region.Height);
        settingFields = false;
        IsMarked = edit.Marked;
        FieldsChanged();
        IsDirty = saved is null ? IsDirty : !SameFields(saved, Current(saved.Marked));
    }

    private ImageEdit Current(bool marked) => new(
        SelectedItem!.Archive,
        SelectedItem.Path,
        Convert.ToHexStringLower(sourceHash),
        PixelWidth,
        PixelHeight,
        new ImageRegion(RegionX, RegionY, Math.Max(1, RegionWidth), Math.Max(1, RegionHeight)),
        UsesSolidErase ? ImageEraseModes.Solid : ImageEraseModes.Surrounding,
        UsesSolidErase ? Hex(EraseColor, "#ffffff") : null,
        Text,
        string.IsNullOrWhiteSpace(Font) ? "Zen Kaku Gothic New" : Font,
        FontSize is > 0 and <= 1000 ? FontSize : 18,
        IsBold,
        Hex(TextColor, "#000000"),
        HasOutline ? Hex(OutlineColor, "#ffffff") : null,
        marked);

    private static string Hex(string value, string fallback) => ImageEditSerializer.IsColor(value) ? value.ToLowerInvariant() : fallback;

    private static bool SameFields(ImageEdit a, ImageEdit b) => a with { Marked = false } == b with { Marked = false };

    private void FieldsChanged()
    {
        if (settingFields || SelectedItem is null || source is null || !IsEditable)
        {
            return;
        }

        var edit = Current(IsMarked);
        IsDirty = saved is null || !SameFields(saved, edit);
        ErasedText = $"{edit.Region.Width}×{edit.Region.Height} px, preenchida com {(edit.Erase == ImageEraseModes.Solid ? $"a cor {edit.EraseColor}" : "o fundo ao redor")}";
        NewTextSummary = edit.Text.Length == 0 ? "nenhum" : $"\"{edit.Text}\" · {edit.Font}{(edit.Bold ? " Bold" : string.Empty)} {Pixels(edit.FontSize)} · cor {edit.Color.TrimStart('#')}";
        Previewing = RenderPreviewAsync(edit, source, SelectedItem, ReplacePreviewToken(createNew: true));
    }

    // Cancela a prévia em andamento e descarta a fonte do token dela. A tarefa antiga só lê o token (IsCancellationRequested,
    // ThrowIfCancellationRequested e os Task.Delay e Task.Run que já o receberam), o que continua valendo depois do Dispose, porque o Cancel vem antes.
    private CancellationToken ReplacePreviewToken(bool createNew)
    {
        var old = previewCts;
        previewCts = createNew ? new CancellationTokenSource() : null;
        old?.Cancel();
        old?.Dispose();
        return previewCts?.Token ?? CancellationToken.None;
    }

    /// <summary>Cancela a prévia em andamento e libera o token dela e o semáforo das miniaturas.</summary>
    public void Dispose()
    {
        ReplacePreviewToken(createNew: false);
        thumbnailGate.Dispose();
    }

    // A prévia espera o debounce e monta a imagem numa thread de fundo: digitar não trava a janela, e só o último valor vira prévia.
    private async Task RenderPreviewAsync(ImageEdit edit, RasterPicture picture, ImageItemViewModel item, CancellationToken token)
    {
        try
        {
            if (previewDelay > TimeSpan.Zero)
            {
                await Task.Delay(previewDelay, token);
            }

            var (png, width, height) = await Task.Run(
                () =>
                {
                    var rendered = composer.Render(picture, edit);
                    var size = composer.Measure(edit);
                    return (rendered, size.Width, size.Height);
                },
                token);
            token.ThrowIfCancellationRequested();
            PreviewPng = png;
            FitsRegion = edit.Text.Length == 0 || (width <= edit.Region.Width && height <= edit.Region.Height);
            FitText = edit.Text.Length == 0
                ? "Sem texto: a área só é apagada."
                : FitsRegion
                    ? $"Cabe na placa · {Plain(width)} de {Pixels(edit.Region.Width)} de largura"
                    : $"Não cabe na placa · {Plain(width)} de {Pixels(edit.Region.Width)} de largura, {Plain(height)} de {Pixels(edit.Region.Height)} de altura";
            PackageText = $"{item.Archive} · entrada {item.Path} · {Kilobytes(OriginalPng?.Length ?? 0)} para {Kilobytes(png.Length)}";
        }
        catch (OperationCanceledException)
        {
            // Outra mudança chegou antes do fim: a prévia dela é a que vale.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Nenhuma falha da prévia some em silêncio, mas só a da imagem aberta vale para a tela.
            if (!token.IsCancellationRequested && ReferenceEquals(SelectedItem, item))
            {
                ErrorMessage = $"Não foi possível montar a prévia: {ex.Message}";
            }
        }
    }

    // A imagem pronta e o image-edits.json andam juntos: o PNG vai primeiro para um .tmp, depois o JSON, e só então o .tmp toma o lugar do PNG.
    // Uma falha em qualquer passo desfaz o anterior, para o JSON nunca apontar para uma imagem que não é a dele.
    private async Task PersistAsync(bool marked)
    {
        if (folder is null || SelectedItem is not { } item || source is not { } picture || !IsEditable)
        {
            return;
        }

        var edit = Current(marked);
        string? temp = null;
        try
        {
            var png = await Task.Run(() => composer.Render(picture, edit), CancellationToken.None);
            var file = folder.EditedImagePath(item.Archive, item.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            temp = file + ".tmp";
            await File.WriteAllBytesAsync(temp, png, CancellationToken.None);
            var previous = edits;
            var next = edits.Where(e => !(e.Archive == edit.Archive && e.Path == edit.Path)).Append(edit).ToList();
            folder.SaveImageEdits(next);
            try
            {
                File.Move(temp, file, overwrite: true);
            }
            catch (Exception moveError)
            {
                try
                {
                    folder.SaveImageEdits(previous);
                }
                catch (Exception rollbackError)
                {
                    // A falha do desfazer não pode esconder a que a causou.
                    throw new AggregateException(moveError, rollbackError);
                }

                throw;
            }

            temp = null;
            edits = next;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var reason = ex is AggregateException all ? string.Join(" ", all.InnerExceptions.Select(e => e.Message)) : ex.Message;
            ErrorMessage = $"Não foi possível gravar a edição de {item.Name}: {reason}";
            return;
        }
        finally
        {
            if (temp is not null && File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        item.Edit = edit;
        if (ReferenceEquals(SelectedItem, item))
        {
            // Durante o render a pessoa pode ter aberto outra imagem: o estado do editor só muda para a que gravou.
            ErrorMessage = null;
            saved = edit;
            IsMarked = marked;
            IsDirty = false;
        }

        RefreshCounts();
        Refilter();
    }

    private async Task LoadThumbnailAsync(ImageItemViewModel item)
    {
        await thumbnailGate.WaitAsync(CancellationToken.None);
        try
        {
            item.ThumbnailPng = await Task.Run(() => composer.Thumbnail(ReadOriginal(item), 240), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            item.ThumbnailError = ex.Message;
        }
        finally
        {
            thumbnailGate.Release();
        }
    }

    private void RefreshCounts()
    {
        MarkedCount = Items.Count(i => i.State == ImageState.Marked);
        OnPropertyChanged(nameof(MarkedText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanOpenInventory));
        OnPropertyChanged(nameof(RewriteLabel));
        Chips[(int)ImageFilter.All].Count = Items.Count;
        Chips[(int)ImageFilter.WithJapanese].Count = Items.Count(i => i.HasText);
        Chips[(int)ImageFilter.Edited].Count = Items.Count(i => i.State != ImageState.Pending);
        Chips[(int)ImageFilter.Untranslated].Count = Items.Count(i => i.HasText && i.State != ImageState.Marked);
        Chips[(int)ImageFilter.WithoutText].Count = Items.Count(i => !i.HasText);
    }

    private void Refilter()
    {
        var filter = Chips.First(c => c.IsSelected).Filter;
        var shown = Items.Where(i => filter switch
        {
            ImageFilter.WithJapanese => i.HasText,
            ImageFilter.Edited => i.State != ImageState.Pending,
            ImageFilter.Untranslated => i.HasText && i.State != ImageState.Marked,
            ImageFilter.WithoutText => !i.HasText,
            _ => true,
        }).ToList();
        Visible.Clear();
        foreach (var item in shown)
        {
            Visible.Add(item);
        }
    }

    private int CountArchives()
    {
        var extensions = engines.SelectMany(e => e.Archives).SelectMany(f => f.FileExtensions).ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            return Directory.EnumerateFiles(gameDirectory, "*", SearchOption.AllDirectories).Count(f => extensions.Contains(Path.GetExtension(f)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    // O tamanho de cada entrada do pacote, para o "antes" do diálogo e a conta das que ficam iguais; vazio quando o pacote não abre.
    private Dictionary<string, long> EntrySizes(string archiveName)
    {
        try
        {
            var path = originalOf?.Invoke(archiveName) ?? Path.Combine(gameDirectory, archiveName);
            var extension = Path.GetExtension(path);
            using var stream = File.OpenRead(path);
            var format = engines.SelectMany(e => e.Archives).FirstOrDefault(f => f.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) && f.CanOpen(stream));
            if (format is null)
            {
                return [];
            }

            stream.Position = 0;
            using var archive = format.Open(stream);
            return archive.Entries.ToDictionary(e => e.Path, e => e.Size, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or VNForgeException or InvalidDataException)
        {
            return [];
        }
    }

    // O formato do pacote como a pessoa o conhece: a extensão em maiúsculas (img.ypf vira YPF).
    private static string? FormatName(string archive) => Path.GetExtension(archive).TrimStart('.') is { Length: > 0 } extension ? extension.ToUpperInvariant() : null;

    private static string Plain(double value) => value.ToString("0", CultureInfo.InvariantCulture);

    private static string Pixels(double value) => $"{value.ToString("0", CultureInfo.InvariantCulture)} px";

    private static string Kilobytes(long bytes) => $"{ProjectLabels.Number((int)((bytes + 1023) / 1024))} KB";
}
