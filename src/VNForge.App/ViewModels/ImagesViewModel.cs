using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public ImageTextEntry Entry { get; } = entry;

    public string Archive => Entry.Archive;

    public string Path => Entry.Path;

    public string Name => System.IO.Path.GetFileName(Entry.Path);

    public bool HasText => Entry.HasText;

    public string JapaneseText => Entry.Text;

    public string Caption => $"{Name} · {Entry.Width}×{Entry.Height} · {Entry.Archive}";

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
public sealed partial class ImagesViewModel : ObservableObject
{
    private static readonly string[] Editable = ["image/png"];

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

    public ImagesViewModel(IReadOnlyList<IEngine>? engines = null, IImageComposer? composer = null, TimeSpan? previewDelay = null)
    {
        this.previewDelay = previewDelay ?? TimeSpan.FromMilliseconds(150);
        this.engines = engines ?? [new YuRisEngine()];
        this.composer = composer ?? new AvaloniaImageComposer();
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

    public ObservableCollection<ImageItemViewModel> Items { get; } = [];

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

    [ObservableProperty]
    public partial string FitText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool FitsRegion { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    public partial bool IsDirty { get; private set; }

    public bool CanUndo => IsDirty && HasImage;

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
        Selecting = SelectAsync(newValue);
    }

    partial void OnTextChanged(string value) => FieldsChanged();

    partial void OnFontChanged(string value) => FieldsChanged();

    partial void OnFontSizeChanged(double value) => FieldsChanged();

    partial void OnIsBoldChanged(bool value) => FieldsChanged();

    partial void OnTextColorChanged(string value) => FieldsChanged();

    partial void OnHasOutlineChanged(bool value) => FieldsChanged();

    partial void OnOutlineColorChanged(string value) => FieldsChanged();

    partial void OnUsesSolidEraseChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectedEraseChoice));
        FieldsChanged();
    }

    partial void OnEraseColorChanged(string value) => FieldsChanged();

    partial void OnRegionXChanged(int value) => FieldsChanged();

    partial void OnRegionYChanged(int value) => FieldsChanged();

    partial void OnRegionWidthChanged(int value) => FieldsChanged();

    partial void OnRegionHeightChanged(int value) => FieldsChanged();

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
        foreach (var item in marked)
        {
            var file = folder.EditedImagePath(item.Archive, item.Path);
            ConfirmationFiles.Add(new RewriteFile(item.Name, $"{item.Archive} · {Kilobytes(File.Exists(file) ? new FileInfo(file).Length : 0)}"));
        }

        var touched = marked.Select(i => i.Archive).Distinct(StringComparer.Ordinal).ToList();
        // Abrir os pacotes e listar a pasta do jogo leva tempo num jogo grande; fora da thread de UI.
        var (archives, entries) = await Task.Run(() => (CountArchives(), touched.Sum(CountEntries)), CancellationToken.None);
        ConfirmationFacts.Add(new ResultFact("Pacotes tocados", $"{ProjectLabels.Number(touched.Count)} de {ProjectLabels.Number(archives)}"));
        ConfirmationFacts.Add(new ResultFact("Entradas que ficam byte a byte iguais", ProjectLabels.Number(Math.Max(0, entries - marked.Count))));
        ConfirmationTitle = marked.Count == 1 ? "Gerar o build com 1 imagem alterada?" : $"Gerar o build com {ProjectLabels.Number(marked.Count)} imagens alteradas?";
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
        previewCts?.Cancel();
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
        catch (Exception ex) when (ex is VNForgeException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidFileException)
        {
            ErrorMessage = $"Não foi possível ler {item.Name} no pacote: {ex.Message}";
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
        catch (InvalidDataException ex)
        {
            ErrorMessage = $"{ex.Message} ({item.Name})";
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
            ErrorMessage = $"{item.Name} mudou no jogo depois do inventário. Rode vnf images scan de novo para atualizar o texto lido.";
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
        previewCts?.Cancel();
        previewCts = new CancellationTokenSource();
        Previewing = RenderPreviewAsync(edit, source, SelectedItem, previewCts.Token);
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
                    ? $"Cabe na área · {Pixels(width)} de {Pixels(edit.Region.Width)} de largura"
                    : $"Não cabe na área · {Pixels(width)} de {Pixels(edit.Region.Width)} de largura, {Pixels(height)} de {Pixels(edit.Region.Height)} de altura";
            PackageText = $"{item.Archive} · entrada {item.Path} · {Kilobytes(OriginalPng?.Length ?? 0)} para {Kilobytes(png.Length)}";
        }
        catch (OperationCanceledException)
        {
            // Outra mudança chegou antes do fim: a prévia dela é a que vale.
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            ErrorMessage = $"Não foi possível montar a prévia: {ex.Message}";
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
            catch
            {
                folder.SaveImageEdits(previous);
                throw;
            }

            temp = null;
            edits = next;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or Core.Glossary.GlossaryFileException)
        {
            ErrorMessage = $"Não foi possível gravar a edição: {ex.Message}";
            return;
        }
        finally
        {
            if (temp is not null && File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        ErrorMessage = null;
        item.Edit = edit;
        saved = edit;
        IsMarked = marked;
        IsDirty = false;
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
        catch (Exception ex) when (ex is VNForgeException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidFileException)
        {
            // Sem miniatura a faixa mostra o quadro vazio; o erro aparece se a pessoa abrir a imagem.
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

    private int CountEntries(string archiveName)
    {
        try
        {
            var path = originalOf?.Invoke(archiveName) ?? Path.Combine(gameDirectory, archiveName);
            var extension = Path.GetExtension(path);
            using var stream = File.OpenRead(path);
            var format = engines.SelectMany(e => e.Archives).FirstOrDefault(f => f.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) && f.CanOpen(stream));
            if (format is null)
            {
                return 0;
            }

            stream.Position = 0;
            using var archive = format.Open(stream);
            return archive.Entries.Count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or VNForgeException or InvalidDataException)
        {
            return 0;
        }
    }

    private static string Pixels(double value) => $"{value.ToString("0", CultureInfo.InvariantCulture)} px";

    private static string Kilobytes(long bytes) => $"{ProjectLabels.Number((int)((bytes + 1023) / 1024))} KB";
}
