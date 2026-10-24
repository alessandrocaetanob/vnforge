using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Provedor de IA na lista da tela; os que ainda não existem aparecem como "em breve".</summary>
public sealed record ProviderOption(string Name, bool IsAvailable)
{
    public string Label => IsAvailable ? Name : $"{Name} · em breve";
}

/// <summary>Modelo Claude com o preço por milhão de tokens.</summary>
public sealed record ModelOption(string Id, string Name, string Price);

/// <summary>Opção de uma lista fechada do <c>vnforge.json</c> (esforço, acentuação).</summary>
public sealed record ChoiceItem(string Id, string Label);

/// <summary>Opção de uma enumeração do perfil de formatação.</summary>
public sealed record ChoiceItem<T>(T Value, string Label);

/// <summary>
/// Tela Ajustes: a chave da API (só no cofre do usuário), o modelo e os limites da IA, a acentuação, o guia de estilo e o perfil de
/// formatação com prévia ao vivo. Modelo, acentuação, guia e formatação valem para o projeto aberto; chave e paralelismo valem para o usuário.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private const string StyleGuideFileName = "style.md";

    private const string SamplePreview = "Espera...  “Eu não sei”  ; talvez devêssemos ir embora agora, antes que alguém nos veja aqui no corredor.";

    private static readonly NumberFormatInfo PriceFormat = new() { NumberDecimalSeparator = "," };

    private static readonly HashSet<string> PreviewInputs =
    [
        nameof(Nfc), nameof(InvisibleSpaces), nameof(Nbsp), nameof(CollapseSpaces), nameof(SpaceBeforePunctuation),
        nameof(SpaceAfterPunctuation), nameof(SelectedQuotes), nameof(SelectedEllipsis), nameof(WrapEnabled),
        nameof(WrapMaxWidth), nameof(WrapMaxLines), nameof(PreviewBefore),
    ];

    private readonly IApiKeyStore vault;
    private readonly AppSettingsFile appSettings;
    private readonly Func<string, string?> readEnvironment;
    private ProjectFolder? folder;
    private ProjectConfig? config;
    private FormattingProfile baseProfile = FormattingProfile.Default;
    private bool loading;

    public SettingsViewModel(IApiKeyStore vault, AppSettingsFile appSettings, Func<string, string?>? readEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(appSettings);
        this.vault = vault;
        this.appSettings = appSettings;
        this.readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;

        Providers =
        [
            new("Claude (Anthropic)", true),
            new("OpenAI", false),
            new("Gemini", false),
            new("Ollama / LM Studio", false),
        ];
        Models = [.. ClaudeModels.All.Select(Describe)];
        Efforts = [new("low", "Baixo"), new("medium", "Médio"), new("high", "Alto")];
        Encodings = [.. new[] { "fold", "remap", "cp1252", "sjis" }.Select(id => new ChoiceItem(id, ProjectLabels.Encoding(id)))];
        QuoteStyles = [new(QuoteStyle.None, "Manter"), new(QuoteStyle.Ascii, "Aspas retas"), new(QuoteStyle.Corner, "Aspas 「 」")];
        EllipsisStyles = [new(EllipsisStyle.None, "Manter"), new(EllipsisStyle.Character, "Caractere …"), new(EllipsisStyle.Dots, "Três pontos")];

        loading = true;
        Concurrency = appSettings.Load().Concurrency;
        SelectedModel = Models[0];
        SelectedEffort = Efforts[1];
        SelectedEncoding = Encodings[0];
        SelectedQuotes = QuoteStyles[0];
        SelectedEllipsis = EllipsisStyles[0];
        BatchSize = AiConfig.Default.BatchSize;
        PreviewBefore = SamplePreview;
        loading = false;
        RefreshPreview();
        RefreshKey();
    }

    /// <summary>A chave foi guardada ou apagada: a janela atualiza a barra de status.</summary>
    public event EventHandler? KeyChanged;

    public ReadOnlyCollection<ProviderOption> Providers { get; }

    public ReadOnlyCollection<ModelOption> Models { get; }

    public ReadOnlyCollection<ChoiceItem> Efforts { get; }

    public ReadOnlyCollection<ChoiceItem> Encodings { get; }

    public ReadOnlyCollection<ChoiceItem<QuoteStyle>> QuoteStyles { get; }

    public ReadOnlyCollection<ChoiceItem<EllipsisStyle>> EllipsisStyles { get; }

    public int MinBatchSize => TranslationRunOptions.MinBatchSize;

    public int MaxBatchSize => TranslationRunOptions.MaxBatchSize;

    public int MinConcurrency => AppSettings.MinConcurrency;

    public int MaxConcurrency => AppSettings.MaxConcurrency;

    /// <summary>Há projeto aberto e legível: os ajustes do projeto só valem com ele.</summary>
    public bool HasProject => folder is not null;

    [ObservableProperty]
    public partial string? ProjectName { get; private set; }

    [ObservableProperty]
    public partial ModelOption? SelectedModel { get; set; }

    [ObservableProperty]
    public partial ChoiceItem? SelectedEffort { get; set; }

    [ObservableProperty]
    public partial int BatchSize { get; set; }

    [ObservableProperty]
    public partial int Concurrency { get; set; }

    [ObservableProperty]
    public partial ChoiceItem? SelectedEncoding { get; set; }

    [ObservableProperty]
    public partial string StyleGuide { get; set; } = string.Empty;

    /// <summary>Campo de digitação da chave nova; esvazia assim que a chave vai para o cofre.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveKeyCommand))]
    public partial string NewApiKey { get; set; } = string.Empty;

    /// <summary>De onde vem a chave, sem o valor dela.</summary>
    [ObservableProperty]
    public partial string KeyStatus { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool KeyConfigured { get; private set; }

    [ObservableProperty]
    public partial string? KeyMessage { get; private set; }

    [ObservableProperty]
    public partial bool Nfc { get; set; }

    [ObservableProperty]
    public partial bool InvisibleSpaces { get; set; }

    [ObservableProperty]
    public partial bool Nbsp { get; set; }

    [ObservableProperty]
    public partial bool CollapseSpaces { get; set; }

    [ObservableProperty]
    public partial bool SpaceBeforePunctuation { get; set; }

    [ObservableProperty]
    public partial bool SpaceAfterPunctuation { get; set; }

    [ObservableProperty]
    public partial ChoiceItem<QuoteStyle>? SelectedQuotes { get; set; }

    [ObservableProperty]
    public partial ChoiceItem<EllipsisStyle>? SelectedEllipsis { get; set; }

    [ObservableProperty]
    public partial bool WrapEnabled { get; set; }

    [ObservableProperty]
    public partial int WrapMaxWidth { get; set; } = WrapRule.DefaultMaxWidth;

    [ObservableProperty]
    public partial int WrapMaxLines { get; set; } = WrapRule.DefaultMaxLines;

    /// <summary>Texto de exemplo da prévia; o usuário pode trocar pelo seu.</summary>
    [ObservableProperty]
    public partial string PreviewBefore { get; set; } = string.Empty;

    /// <summary>O exemplo depois das regras ligadas; o texto salvo no projeto não muda.</summary>
    [ObservableProperty]
    public partial string PreviewAfter { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> PreviewWarnings { get; private set; } = [];

    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>Lê os ajustes do projeto; <see langword="null"/> deixa só os ajustes do usuário.</summary>
    public void Load(ProjectSummary? project)
    {
        folder = null;
        config = null;
        ErrorMessage = null;
        StatusMessage = null;
        ProjectName = project?.Name;
        if (project is not null)
        {
            try
            {
                var opened = ProjectFolder.Open(project.Root);
                var loadedConfig = opened.LoadConfig();
                var profile = opened.LoadFormatting();
                var styleFile = Path.Combine(opened.Root, StyleGuideFileName);
                loading = true;
                Apply(loadedConfig, profile, File.Exists(styleFile) ? File.ReadAllText(styleFile) : string.Empty);
                folder = opened;
                config = loadedConfig;
                baseProfile = profile;
            }
            catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
            {
                ErrorMessage = $"Não foi possível ler os ajustes do projeto: {e.Message}";
            }
            finally
            {
                loading = false;
            }
        }

        OnPropertyChanged(nameof(HasProject));
        SaveProjectCommand.NotifyCanExecuteChanged();
        RefreshPreview();
    }

    [RelayCommand(CanExecute = nameof(HasProject))]
    private void SaveProject()
    {
        if (folder is null || config is null)
        {
            return;
        }

        try
        {
            var updated = config with
            {
                Ai = config.Ai with
                {
                    Model = SelectedModel?.Id ?? config.Ai.Model,
                    Effort = SelectedEffort?.Id ?? config.Ai.Effort,
                    BatchSize = BatchSize,
                },
                Encoding = new EncodingConfig(SelectedEncoding?.Id ?? config.Encoding.Strategy),
            };
            var profile = BuildProfile();
            folder.SaveConfig(updated);
            folder.SaveFormatting(profile);
            SaveStyleGuide(folder.Root);
            config = updated;
            baseProfile = profile;
            ErrorMessage = null;
            StatusMessage = "Ajustes do projeto salvos.";
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            StatusMessage = null;
            ErrorMessage = $"Não foi possível salvar os ajustes do projeto: {e.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(IsNewKeyValid))]
    private void SaveKey()
    {
        if (!IsNewKeyValid())
        {
            return;
        }

        try
        {
            vault.Save(NewApiKey.Trim());
        }
        catch (Exception e) when (e is VNForgeException or CryptographicException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            KeyMessage = $"Não foi possível guardar a chave: {e.Message}";
            return;
        }

        NewApiKey = string.Empty;
        KeyMessage = "Chave guardada no Cofre do Windows (DPAPI), fora do projeto.";
        RefreshKey();
        KeyChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearKey()
    {
        try
        {
            KeyMessage = vault.Delete() ? "Chave apagada do Cofre do Windows." : "Não havia chave no Cofre do Windows.";
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            KeyMessage = $"Não foi possível apagar a chave: {e.Message}";
            return;
        }

        RefreshKey();
        KeyChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!loading && e.PropertyName is { } name && PreviewInputs.Contains(name))
        {
            RefreshPreview();
        }
    }

    partial void OnBatchSizeChanged(int value) => BatchSize = Math.Clamp(value, MinBatchSize, MaxBatchSize);

    partial void OnWrapMaxWidthChanged(int value) => WrapMaxWidth = Math.Clamp(value, 1, 200);

    partial void OnWrapMaxLinesChanged(int value) => WrapMaxLines = Math.Clamp(value, 1, 20);

    partial void OnConcurrencyChanged(int value)
    {
        var clamped = Math.Clamp(value, MinConcurrency, MaxConcurrency);
        if (clamped != value)
        {
            Concurrency = clamped;
            return;
        }

        if (loading)
        {
            return;
        }

        try
        {
            appSettings.Save(new AppSettings(clamped));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Não foi possível salvar as chamadas em paralelo: {e.Message}";
        }
    }

    private bool IsNewKeyValid()
    {
        var key = NewApiKey.Trim();
        return key.Length > 0 && !key.Any(char.IsWhiteSpace);
    }

    private static ModelOption Describe(string id)
    {
        var price = ModelPricing.For(id);
        return new ModelOption(id, ProjectLabels.Model(id), $"US$ {price.Input.ToString("0.00", PriceFormat)} / US$ {price.Output.ToString("0.00", PriceFormat)} por milhão de tokens");
    }

    private void Apply(ProjectConfig project, FormattingProfile profile, string style)
    {
        SelectedModel = Models.FirstOrDefault(m => m.Id == project.Ai.Model) ?? Models[0];
        SelectedEffort = Efforts.FirstOrDefault(e => e.Id == project.Ai.Effort) ?? Efforts[1];
        BatchSize = project.Ai.BatchSize;
        SelectedEncoding = Encodings.FirstOrDefault(e => e.Id == project.Encoding.Strategy) ?? Encodings[0];
        StyleGuide = style;
        Nfc = profile.Normalization.Nfc;
        InvisibleSpaces = profile.Normalization.InvisibleSpaces;
        Nbsp = profile.Normalization.Nbsp;
        CollapseSpaces = profile.Spacing.CollapseSpaces;
        SpaceBeforePunctuation = profile.Spacing.SpaceBeforePunctuation;
        SpaceAfterPunctuation = profile.Spacing.SpaceAfterPunctuation;
        SelectedQuotes = QuoteStyles.First(q => q.Value == profile.Typography.Quotes);
        SelectedEllipsis = EllipsisStyles.First(q => q.Value == profile.Typography.Ellipsis);
        WrapEnabled = profile.Wrap.Enabled;
        WrapMaxWidth = profile.Wrap.MaxWidth;
        WrapMaxLines = profile.Wrap.MaxLines ?? WrapRule.DefaultMaxLines;
    }

    /// <summary>O perfil que a tela edita por cima do que já estava no <c>formatting.json</c>: o resto (papéis, travessão, substituições) fica como está.</summary>
    private FormattingProfile BuildProfile() => baseProfile with
    {
        Normalization = new NormalizationRule(Nfc, InvisibleSpaces, Nbsp),
        Spacing = new SpacingRule(CollapseSpaces, SpaceBeforePunctuation, SpaceAfterPunctuation),
        Typography = baseProfile.Typography with
        {
            Quotes = SelectedQuotes?.Value ?? QuoteStyle.None,
            Ellipsis = SelectedEllipsis?.Value ?? EllipsisStyle.None,
        },
        Wrap = baseProfile.Wrap with { Enabled = WrapEnabled, MaxWidth = WrapMaxWidth, MaxLines = WrapMaxLines },
    };

    private void SaveStyleGuide(string root)
    {
        var path = Path.Combine(root, StyleGuideFileName);
        if (string.IsNullOrWhiteSpace(StyleGuide))
        {
            File.Delete(path);
            return;
        }

        File.WriteAllText(path, StyleGuide);
    }

    private void RefreshPreview()
    {
        try
        {
            var result = FormattingPipeline.Create(BuildProfile()).Format(PreviewBefore);
            PreviewAfter = result.Text.Replace(ControlMarkers.LineBreak, "\n", StringComparison.Ordinal);
            PreviewWarnings = result.Warnings;
        }
        catch (Exception e) when (e is FormattingException or ArgumentException)
        {
            PreviewAfter = PreviewBefore;
            PreviewWarnings = [$"A prévia não pôde ser calculada: {e.Message}"];
        }
    }

    private void RefreshKey()
    {
        var resolved = ApiKeyStatus.Resolve(() => vault, readEnvironment);
        KeyStatus = ApiKeyStatus.Describe(resolved);
        KeyConfigured = resolved.Source != ApiKeySource.None;
    }
}
