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
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>Provedor de IA na lista da tela; os que ainda não existem aparecem como "em breve".</summary>
public sealed record ProviderOption(string Name, bool IsAvailable)
{
    public string Label => IsAvailable ? Name : $"{Name} · em breve";
}

/// <summary>Opção de uma lista fechada do <c>vnforge.json</c> (esforço).</summary>
public sealed record ChoiceItem(string Id, string Label);

/// <summary>Opção de uma enumeração do perfil de formatação.</summary>
public sealed record ChoiceItem<T>(T Value, string Label);

/// <summary>
/// Cartão de modelo Claude: nome, preço por milhão de tokens (entrada / saída), para que serve e, com projeto aberto, a estimativa
/// para as linhas pendentes. <see cref="IsSelected"/> liga o <c>RadioButton</c> do cartão ao <see cref="SettingsViewModel.SelectedModel"/>.
/// </summary>
public sealed partial class ModelOption(string id, string name, string price, string use) : ObservableObject
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    /// <summary>"US$ 4 / 20 / M tokens": entrada / saída por milhão de tokens.</summary>
    public string Price { get; } = price;

    public string Use { get; } = use;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Custo estimado das linhas pendentes neste modelo; <see langword="null"/> sem projeto ou enquanto a conta roda.</summary>
    [ObservableProperty]
    public partial string? Estimate { get; private set; }

    /// <summary>A linha de baixo do cartão: o uso do modelo e, quando há projeto, a estimativa.</summary>
    [ObservableProperty]
    public partial string Detail { get; private set; } = use;

    internal void ShowEstimate(int? pendingLines, decimal dollars)
    {
        Estimate = pendingLines is > 0 ? SettingsViewModel.Money(dollars) : null;
        Detail = pendingLines switch
        {
            null => Use,
            0 => $"{Use} · nenhuma linha pendente",
            1 => $"{Use} · estimativa para a linha pendente: {Estimate}",
            _ => $"{Use} · estimativa para as {ProjectLabels.Number(pendingLines.Value)} pendentes: {Estimate}",
        };
    }
}

/// <summary>Cartão de estratégia de acentuação: nome, um exemplo do efeito e a explicação.</summary>
public sealed partial class EncodingOption(string id, string name, string sample, string description) : ObservableObject
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    /// <summary>"ação → acao": o que acontece com um texto acentuado.</summary>
    public string Sample { get; } = sample;

    public string Description { get; } = description;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// Tela Ajustes, em três abas (IA, Formatação, Acentuação): a chave da API (só no cofre do usuário), o modelo e os limites da IA, o guia
/// de estilo, o perfil de formatação com prévia ao vivo e a estratégia de acentuação. Modelo, esforço, lote, acentuação, guia e formatação
/// valem para o projeto aberto e são gravados a cada mudança (<see cref="ProjectSaved"/>); chave e paralelismo valem para o usuário.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>O texto da barra de status da moldura enquanto os Ajustes estão abertos (artboard de Ajustes).</summary>
    public const string AutosaveNote = "Alterações salvas automaticamente em vnforge.json";

    private const string StyleGuideFileName = "style.md";

    private const string SamplePreview = "Espera...  “Eu não sei”  ; talvez devêssemos ir embora agora, antes que alguém nos veja aqui no corredor.";

    private static readonly NumberFormatInfo PriceFormat = new() { NumberDecimalSeparator = "," };

    private static readonly NumberFormatInfo MoneyFormat = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };

    private static readonly HashSet<string> ConfigInputs =
    [
        nameof(SelectedModel), nameof(SelectedEffort), nameof(BatchSize), nameof(SelectedEncoding),
    ];

    private static readonly HashSet<string> FormattingInputs =
    [
        nameof(Nfc), nameof(InvisibleSpaces), nameof(Nbsp), nameof(CollapseSpaces), nameof(SpaceBeforePunctuation),
        nameof(SpaceAfterPunctuation), nameof(SelectedQuotes), nameof(SelectedEllipsis), nameof(WrapEnabled),
        nameof(WrapMaxWidth), nameof(WrapMaxLines),
    ];

    private readonly IApiKeyStore vault;
    private readonly AppSettingsFile appSettings;
    private readonly Func<string, string?> readEnvironment;
    private readonly Func<string, IClaudeMessageClient> clientFactory;
    private ProjectFolder? folder;
    private ProjectConfig? config;
    private FormattingProfile baseProfile = FormattingProfile.Default;
    private bool loading;
    private int estimateVersion;

    public SettingsViewModel(
        IApiKeyStore vault,
        AppSettingsFile appSettings,
        Func<string, string?>? readEnvironment = null,
        Func<string, IClaudeMessageClient>? clientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(appSettings);
        this.vault = vault;
        this.appSettings = appSettings;
        this.readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
        this.clientFactory = clientFactory ?? ClaudeProviderFactory.CreateClient;

        Providers =
        [
            new("Claude (Anthropic)", true),
            new("OpenAI", false),
            new("Gemini", false),
            new("Ollama / LM Studio", false),
        ];
        Themes = [new(ThemeChoice.System, "Sistema"), new(ThemeChoice.Light, "Claro"), new(ThemeChoice.Dark, "Escuro")];
        Models = [.. ClaudeModels.All.Select(Describe)];
        Efforts = [new("low", "Baixo"), new("medium", "Médio"), new("high", "Alto")];
        Encodings =
        [
            new("fold", "Remover acentos (fold)", "ação → acao", "Funciona em qualquer jogo sem mexer no executável. Bom para validar a ida e volta."),
            new("remap", "Remapear para meia largura (remap)", "ação → aｱｲo", "Usa katakanas de 1 byte como substitutos e uma fonte modificada que desenha as letras certas."),
            new("cp1252", "Latin-1 nativo (cp1252)", "ação → ação", "Acentos completos com o executável original. O patch opcional troca o nome da fonte para MS Gothic; aplique na tela Build. Experimental."),
            new("sjis", "Shift-JIS puro (sjis)", "ação → recusado", "Não trata acentos: o build recusa o que o Shift-JIS não representa. Serve de base e para texto sem acentos."),
        ];
        QuoteStyles = [new(QuoteStyle.None, "Manter"), new(QuoteStyle.Ascii, "Aspas retas"), new(QuoteStyle.Corner, "Aspas 「 」")];
        EllipsisStyles = [new(EllipsisStyle.None, "Manter"), new(EllipsisStyle.Character, "Caractere …"), new(EllipsisStyle.Dots, "Três pontos")];

        foreach (var model in Models)
        {
            model.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ModelOption.IsSelected) && model.IsSelected)
                {
                    SelectedModel = model;
                }
            };
        }

        foreach (var encoding in Encodings)
        {
            encoding.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(EncodingOption.IsSelected) && encoding.IsSelected)
                {
                    SelectedEncoding = encoding;
                }
            };
        }

        loading = true;
        var saved = appSettings.Load();
        Concurrency = saved.Concurrency;
        SelectedTheme = Themes.Single(t => t.Value == saved.Theme);
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

    /// <summary>Uma mudança foi gravada no projeto (<c>vnforge.json</c>, <c>formatting.json</c> ou <c>style.md</c>): a barra de status mostra <see cref="AutosaveNote"/>.</summary>
    public event EventHandler? ProjectSaved;

    public ReadOnlyCollection<ProviderOption> Providers { get; }

    public ReadOnlyCollection<ModelOption> Models { get; }

    public ReadOnlyCollection<ChoiceItem> Efforts { get; }

    public ReadOnlyCollection<EncodingOption> Encodings { get; }

    public ReadOnlyCollection<ChoiceItem<QuoteStyle>> QuoteStyles { get; }

    public ReadOnlyCollection<ChoiceItem<EllipsisStyle>> EllipsisStyles { get; }

    public int MinBatchSize => TranslationRunOptions.MinBatchSize;

    public int MaxBatchSize => TranslationRunOptions.MaxBatchSize;

    /// <summary>Sistema, Claro e Escuro; o padrão segue o Windows.</summary>
    public ReadOnlyCollection<ChoiceItem<ThemeChoice>> Themes { get; }

    /// <summary>Disparado quando o usuário troca o tema; o <c>App</c> aplica o <c>RequestedThemeVariant</c>.</summary>
    public event EventHandler<ThemeChoice>? ThemeChanged;

    public int MinConcurrency => AppSettings.MinConcurrency;

    public int MaxConcurrency => AppSettings.MaxConcurrency;

    /// <summary>Há projeto aberto e legível: os ajustes do projeto só valem com ele.</summary>
    public bool HasProject => folder is not null;

    /// <summary>Quanto o "Testar conexão" espera pela API antes de desistir; sem teto, a rede parada deixaria o botão em "Testando…" por minutos.</summary>
    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Termina quando a estimativa das linhas pendentes foi calculada (ou descartada por outro projeto).</summary>
    public Task EstimateLoading { get; private set; } = Task.CompletedTask;

    /// <summary>Índice da aba aberta (0 IA, 1 Formatação, 2 Acentuação); a seção "Aparência" do tema entra como mais uma aba no merge.</summary>
    [ObservableProperty]
    public partial int SelectedTab { get; set; }

    [ObservableProperty]
    public partial string? ProjectName { get; private set; }

    /// <summary>Linhas ainda sem tradução, base da estimativa dos cartões.</summary>
    [ObservableProperty]
    public partial int PendingLines { get; private set; }

    [ObservableProperty]
    public partial ChoiceItem<ThemeChoice>? SelectedTheme { get; set; }

    [ObservableProperty]
    public partial ModelOption? SelectedModel { get; set; }

    [ObservableProperty]
    public partial ChoiceItem? SelectedEffort { get; set; }

    [ObservableProperty]
    public partial int BatchSize { get; set; }

    [ObservableProperty]
    public partial int Concurrency { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCp1252Warning))]
    public partial EncodingOption? SelectedEncoding { get; set; }

    /// <summary>Com cp1252 o byte 0xEF (ï) é o prefixo dos códigos de controle do YU-RIS: a aba mostra o aviso.</summary>
    public bool ShowCp1252Warning => SelectedEncoding?.Id == "cp1252";

    [ObservableProperty]
    public partial string StyleGuide { get; set; } = string.Empty;

    /// <summary>Campo de digitação da chave nova; esvazia assim que a chave vai para o cofre.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveKeyCommand))]
    public partial string NewApiKey { get; set; } = string.Empty;

    /// <summary>De onde vem a chave, sem o valor dela (barra de status).</summary>
    [ObservableProperty]
    public partial string KeyStatus { get; private set; } = string.Empty;

    /// <summary>A linha de baixo do cartão da chave: configurada ou não, e onde ela fica.</summary>
    [ObservableProperty]
    public partial string KeyDetail { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    public partial bool KeyConfigured { get; private set; }

    /// <summary>"Trocar chave" com chave configurada, "Adicionar chave" sem ela.</summary>
    [ObservableProperty]
    public partial string ChangeKeyLabel { get; private set; } = string.Empty;

    /// <summary>O campo de chave nova está aberto no cartão.</summary>
    [ObservableProperty]
    public partial bool IsChangingKey { get; private set; }

    [ObservableProperty]
    public partial string? KeyMessage { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    [NotifyPropertyChangedFor(nameof(TestConnectionLabel))]
    public partial bool IsTestingConnection { get; private set; }

    /// <summary>O último "Testar conexão": a frase em português; <see langword="null"/> antes do teste e depois de trocar a chave.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionFailed))]
    public partial string? ConnectionMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionFailed))]
    public partial bool ConnectionOk { get; private set; }

    /// <summary>O teste terminou e falhou: a tela mostra a mensagem como erro.</summary>
    public bool ConnectionFailed => ConnectionMessage is not null && !ConnectionOk;

    /// <summary>O rótulo do botão enquanto o pedido está no ar.</summary>
    public string TestConnectionLabel => IsTestingConnection ? "Testando…" : "Testar conexão";

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
    public partial string? ErrorMessage { get; private set; }

    /// <summary>Dólares no formato da tela: <c>US$ 21,10</c>; abaixo de um centavo vira "menos de US$ 0,01".</summary>
    internal static string Money(decimal dollars) =>
        dollars < 0.01m ? "menos de US$ 0,01" : $"US$ {Math.Round(dollars, 2, MidpointRounding.AwayFromZero).ToString("N2", MoneyFormat)}";

    /// <summary>Lê os ajustes do projeto; <see langword="null"/> deixa só os ajustes do usuário.</summary>
    public void Load(ProjectSummary? project)
    {
        folder = null;
        config = null;
        ErrorMessage = null;
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
        RefreshPreview();
        StartEstimate(folder);
    }

    [RelayCommand]
    private void ChangeKey() => IsChangingKey = true;

    [RelayCommand]
    private void CancelChangeKey()
    {
        NewApiKey = string.Empty;
        IsChangingKey = false;
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
        IsChangingKey = false;
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

        NewApiKey = string.Empty;
        IsChangingKey = false;
        RefreshKey();
        KeyChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>"Testar conexão": um pedido mínimo ao Haiku com a chave de uso; nunca grava nem mostra a chave.</summary>
    [RelayCommand(CanExecute = nameof(CanTestConnection))]
    private async Task TestConnectionAsync()
    {
        if (IsTestingConnection)
        {
            return;
        }

        var key = ApiKeyStatus.Resolve(() => vault, readEnvironment).Key;
        if (key is null)
        {
            ConnectionOk = false;
            ConnectionMessage = "Configure a chave da API antes de testar a conexão.";
            return;
        }

        IsTestingConnection = true;
        ConnectionMessage = null;
        ConnectionOk = false;
        try
        {
            using var timeout = new CancellationTokenSource(ConnectionTimeout);
            var result = await ClaudeConnectionTest.RunAsync(clientFactory(key), timeout.Token);
            ConnectionOk = result.Success;
            ConnectionMessage = result.Message;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            ConnectionOk = false;
            ConnectionMessage = ClaudeErrorMessages.Describe(e);
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (loading || e.PropertyName is not { } name)
        {
            return;
        }

        if (name == nameof(PreviewBefore) || FormattingInputs.Contains(name))
        {
            RefreshPreview();
        }

        if (folder is null)
        {
            return;
        }

        if (ConfigInputs.Contains(name))
        {
            Persist(SaveConfig);
        }
        else if (FormattingInputs.Contains(name))
        {
            Persist(SaveFormatting);
        }
        else if (name == nameof(StyleGuide))
        {
            Persist(SaveStyleGuide);
        }
    }

    partial void OnSelectedModelChanged(ModelOption? value)
    {
        foreach (var model in Models)
        {
            model.IsSelected = model == value;
        }
    }

    partial void OnSelectedEncodingChanged(EncodingOption? value)
    {
        foreach (var encoding in Encodings)
        {
            encoding.IsSelected = encoding == value;
        }
    }

    partial void OnBatchSizeChanged(int value) => BatchSize = Math.Clamp(value, MinBatchSize, MaxBatchSize);

    partial void OnWrapMaxWidthChanged(int value) => WrapMaxWidth = Math.Clamp(value, 1, 200);

    partial void OnWrapMaxLinesChanged(int value) => WrapMaxLines = Math.Clamp(value, 1, 20);

    partial void OnSelectedThemeChanged(ChoiceItem<ThemeChoice>? value)
    {
        if (value is null || loading)
        {
            return;
        }

        try
        {
            appSettings.Save(new AppSettings(Concurrency, value.Value));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Não foi possível salvar o tema: {e.Message}";
        }

        ThemeChanged?.Invoke(this, value.Value);
    }

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
            appSettings.Save(new AppSettings(clamped, SelectedTheme?.Value ?? ThemeChoice.System));
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

    private bool CanTestConnection() => KeyConfigured && !IsTestingConnection;

    private static ModelOption Describe(string id)
    {
        var price = ModelPricing.For(id);
        var use = id switch
        {
            ClaudeModels.Opus => "Padrão para tradução e revisão",
            ClaudeModels.Sonnet => "Rascunho mais barato e rápido",
            ClaudeModels.Haiku => "Menus, nomes e textos curtos",
            ClaudeModels.Fable => "Cenas difíceis e revisão fina",
            _ => string.Empty,
        };
        return new ModelOption(id, ProjectLabels.Model(id), $"US$ {Rate(price.Input)} / {Rate(price.Output)} / M tokens", use);
    }

    private static string Rate(decimal value) => value == decimal.Truncate(value) ? value.ToString("0", PriceFormat) : value.ToString("0.00", PriceFormat);

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

    /// <summary>Grava uma mudança e avisa a janela; falha de disco vira o erro da tela, nunca uma exceção na digitação.</summary>
    private void Persist(Action write)
    {
        try
        {
            write();
            ErrorMessage = null;
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Não foi possível salvar os ajustes do projeto: {e.Message}";
            return;
        }

        ProjectSaved?.Invoke(this, EventArgs.Empty);
    }

    private void SaveConfig()
    {
        if (folder is null || config is null)
        {
            return;
        }

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
        folder.SaveConfig(updated);
        config = updated;
    }

    private void SaveFormatting()
    {
        if (folder is null)
        {
            return;
        }

        var profile = BuildProfile();
        folder.SaveFormatting(profile);
        baseProfile = profile;
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
        Wrap = baseProfile.Wrap with { Enabled = WrapEnabled, MaxWidth = WrapMaxWidth, MaxLines = baseProfile.Wrap.MaxLines is null && WrapMaxLines == WrapRule.DefaultMaxLines ? null : WrapMaxLines },
    };

    private void SaveStyleGuide()
    {
        if (folder is null)
        {
            return;
        }

        var path = Path.Combine(folder.Root, StyleGuideFileName);
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
        KeyDetail = resolved.Source switch
        {
            ApiKeySource.Environment => $"Configurada · variável de ambiente {ApiKeyResolver.EnvironmentVariable}, que vale mais que o cofre",
            ApiKeySource.Vault => "Configurada · guardada no Cofre do Windows (DPAPI), fora do projeto",
            _ => "Não configurada · use Adicionar chave para guardá-la no Cofre do Windows",
        };
        ChangeKeyLabel = KeyConfigured ? "Trocar chave" : "Adicionar chave";
        ConnectionMessage = null;
        ConnectionOk = false;
    }

    /// <summary>Conta as linhas pendentes fora da thread da tela e preenche a estimativa de cada cartão; um projeto aberto depois descarta o resultado do anterior.</summary>
    private void StartEstimate(ProjectFolder? opened)
    {
        var version = ++estimateVersion;
        PendingLines = 0;
        foreach (var model in Models)
        {
            model.ShowEstimate(null, 0m);
        }

        EstimateLoading = opened is null ? Task.CompletedTask : EstimateAsync(opened, version);
    }

    private async Task EstimateAsync(ProjectFolder opened, int version)
    {
        var ids = Models.Select(m => m.Id).ToList();
        (int Pending, IReadOnlyDictionary<string, CostEstimate> ByModel) result;
        try
        {
            result = await Task.Run(() =>
            {
                var pending = opened.LoadAllText().Values.SelectMany(units => units).Where(u => u.Status == TranslationStatus.Untranslated).ToList();
                return (pending.Count, CostEstimator.EstimateForModels(pending, ids));
            });
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Sem estimativa o cartão mostra só o uso do modelo; ler o texto do projeto falhou e a tela de Ajustes não depende disso.
            return;
        }

        if (version != estimateVersion)
        {
            return;
        }

        PendingLines = result.Pending;
        foreach (var model in Models)
        {
            model.ShowEstimate(result.Pending, result.ByModel[model.Id].Dollars);
        }
    }
}
