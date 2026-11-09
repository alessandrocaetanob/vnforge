using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Core.Publishing;
using VNForge.Core.Qa;
using VNForge.Patching;

namespace VNForge.App.ViewModels;

/// <summary>Em que pé está a tela Lançar: pronta para gerar, gerando, versão pronta ou travada porque o jogo não é a versão de referência.</summary>
public enum ReleasePhase
{
    Ready,
    Generating,
    Done,
    Blocked,
}

public enum ReleaseStepState
{
    Pending,
    Running,
    Done,
}

/// <summary>Um dos seis passos de <see cref="ReleaseService.Create"/>, na ordem do Core, com o tempo que levou e o que achou.</summary>
public sealed partial class ReleaseStepRow(ReleaseStep step, string label) : ObservableObject
{
    public ReleaseStep Step { get; } = step;

    public string Label { get; } = label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending), nameof(IsRunning), nameof(IsDone), nameof(StatusText))]
    public partial ReleaseStepState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string TimeText { get; set; } = string.Empty;

    /// <summary>O que o passo achou ("4 arquivos"); vazio enquanto não sabe.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string Detail { get; set; } = string.Empty;

    /// <summary>O arquivo do jogo em que o passo está.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string Item { get; set; } = string.Empty;

    /// <summary>De 0 a 1, quando o passo sabe o quanto já fez; nulo deixa a barra indeterminada.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsIndeterminate), nameof(Percent))]
    public partial double? Fraction { get; set; }

    public bool IsPending => State == ReleaseStepState.Pending;

    public bool IsRunning => State == ReleaseStepState.Running;

    public bool IsDone => State == ReleaseStepState.Done;

    public bool IsIndeterminate => Fraction is null;

    /// <summary>De 0 a 100, para a barra da etapa atual.</summary>
    public double Percent => Math.Round((Fraction ?? 0) * 100);

    /// <summary>O texto à direita: "2,9 s · 308 de 312 scripts reaproveitados" quando pronto, "63% · Game/script.ypf" enquanto roda.</summary>
    public string StatusText => State switch
    {
        ReleaseStepState.Done => Join(TimeText, Detail),
        ReleaseStepState.Running => Join(Fraction is { } f ? $"{Math.Round(f * 100):0}%" : string.Empty, Item),
        _ => string.Empty,
    };

    private static string Join(string first, string second) =>
        first.Length == 0 ? second : second.Length == 0 ? first : first + " · " + second;
}

/// <summary>Uma conferência antes de gerar. <paramref name="IsOk"/> falso mostra o círculo vazio (ainda não conferido) ou o erro.</summary>
public sealed record ReleaseCheckRow(string Text, bool IsOk, bool IsError = false)
{
    public bool IsPending => !IsOk && !IsError;
}

/// <summary>Uma linha de "O que o jogador recebe".</summary>
public sealed record ReleaseReceivedRow(string Label, string SizeText, string IconKey);

/// <summary>Uma cena de "Mudou desde o patch X".</summary>
public sealed record ReleaseSceneRow(string Title, string Detail);

/// <summary>Uma fala de exemplo de "Mudou desde o patch X": o texto de antes (riscado) e o de agora. <paramref name="Before"/> nulo é fala que não tinha tradução no patch anterior.</summary>
public sealed record ReleaseChangeRow(string Title, string? Before, string After)
{
    public bool HasBefore => Before is not null;
}

/// <summary>Um arquivo do jogo cujo SHA-256 não é o da extração. Os valores vêm abreviados; o inteiro fica na dica.</summary>
public sealed record ReleaseChecksumRow(string Path, string Expected, string Found, string ExpectedFull, string FoundFull);

/// <summary>Um arquivo gerado. Os dois pacotes levam o SHA-256, que o botão "Copiar checksum" entrega inteiro.</summary>
public sealed record ReleaseFileRow(string Name, string SizeText, string? Sha256, string IconKey)
{
    public bool HasChecksum => Sha256 is not null;
}

/// <summary>Um arquivo do jogo que o patch toca: o tamanho do original, o do delta e quanto o delta pesa sobre o original.</summary>
public sealed record ReleaseGameFileRow(string Path, string OriginalText, string PatchText, string RatioText);

/// <summary>
/// A tela Lançar: junta o que o <c>vnf release</c> faz (conferir, montar o build, calcular o patch, verificar numa cópia limpa, montar o instalador e os textos)
/// num fluxo só, com a versão, o nome do instalador, a pasta e os créditos à vista. O trabalho roda como tarefa da barra inferior e se cancela.
/// </summary>
public sealed partial class ReleaseViewModel : ObservableObject
{
    private static readonly NumberFormatInfo PtBr = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };
    private static readonly CultureInfo Brazil = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly (ReleaseStep Step, string Label)[] StepLabels =
    [
        (ReleaseStep.Check, "Conferir a tradução"),
        (ReleaseStep.Build, "Montar o build"),
        (ReleaseStep.Diff, "Calcular as diferenças"),
        (ReleaseStep.Patch, "Gravar o patch"),
        (ReleaseStep.Package, "Montar o instalador"),
        (ReleaseStep.Verify, "Verificar numa cópia limpa"),
    ];

    /// <summary>Quantos arquivos alterados o cartão "Checksum diferente" lista antes de resumir o resto.</summary>
    private const int MaxChecksumFiles = 3;

    private static readonly string InstallerExtension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;

    private readonly BackgroundTaskService tasks;
    private readonly IReleasePublisher publisher;
    private readonly TimeProvider clock;
    private readonly IGameProcessStarter starter;
    private readonly Action<string> openFolder;
    private ReleaseReadiness? readiness;
    private bool outputEdited;
    private bool installerEdited;
    private bool settingDerived;
    private int loadVersion;
    private int generationEpoch;
    private ReleaseStepRow? runningStep;
    private long runningSince;
    private string? installerPath;

    public ReleaseViewModel(
        BackgroundTaskService tasks,
        IReleasePublisher? publisher = null,
        TimeProvider? clock = null,
        IGameProcessStarter? starter = null,
        Action<string>? openFolder = null)
    {
        this.tasks = tasks;
        this.publisher = publisher ?? new ReleasePublisher([new VNForge.Engines.YuRis.YuRisEngine()]);
        this.clock = clock ?? TimeProvider.System;
        this.starter = starter ?? new ShellGameProcessStarter();
        this.openFolder = openFolder ?? (_ => { });
        foreach (var (step, label) in StepLabels)
        {
            Steps.Add(new ReleaseStepRow(step, label));
        }

        ResetTexts();
    }

    /// <summary>"Ver o Testar" na faixa de pronto.</summary>
    public event EventHandler? TestRequested;

    /// <summary>"Ver os erros" quando a Conferência trava o lançamento.</summary>
    public event EventHandler? ErrorsRequested;

    /// <summary>"Escolher outra pasta" quando o jogo não é a versão de referência: a pasta do jogo mora nos Ajustes.</summary>
    public event EventHandler? GameFolderRequested;

    /// <summary>Pede à tela para copiar o texto (o SHA-256 de um pacote) para a área de transferência.</summary>
    public event EventHandler<string>? CopyRequested;

    /// <summary>A versão ficou pronta; o aviso curto diz qual.</summary>
    public event EventHandler<string>? NoticeRaised;

    public IFolderPicker? Picker { get; set; }

    public ObservableCollection<ReleaseStepRow> Steps { get; } = [];

    public ObservableCollection<ReleaseCheckRow> Checks { get; } = [];

    public ObservableCollection<ReleaseReceivedRow> Received { get; } = [];

    public ObservableCollection<ReleaseSceneRow> Scenes { get; } = [];

    public ObservableCollection<ReleaseFileRow> Files { get; } = [];

    /// <summary>Os arquivos do jogo que o patch toca: a estimativa do patch anterior antes e durante a geração.</summary>
    public ObservableCollection<ReleaseGameFileRow> GameFiles { get; } = [];

    /// <summary>Uma fala por cena, com o antes e o depois; vazia quando o texto do patch anterior não foi guardado (então vale <see cref="Scenes"/>).</summary>
    public ObservableCollection<ReleaseChangeRow> ChangeExamples { get; } = [];

    public ObservableCollection<ReleaseChecksumRow> ChecksumFiles { get; } = [];

    /// <summary>Termina quando a última leitura pedida por <see cref="ReloadAsync"/> acaba.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>Termina quando a última geração pedida acaba; os testes a aguardam.</summary>
    public Task Generation { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial ProjectSummary? Project { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(IsGenerating), nameof(IsDone), nameof(IsBlocked), nameof(ShowReceived), nameof(ShowChanges), nameof(ShowGameFiles), nameof(GenerateLabel), nameof(ShowTestShortcut), nameof(ShowErrorsShortcut))]
    public partial ReleasePhase Phase { get; private set; }

    [ObservableProperty]
    public partial GameState BannerState { get; private set; } = GameState.Translated;

    [ObservableProperty]
    public partial string BannerTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string BannerMessage { get; private set; } = string.Empty;

    /// <summary>"Última versão: 1.1.0 · 02/10" no alto da página; vazio na primeira.</summary>
    [ObservableProperty]
    public partial string LastVersionText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionError), nameof(HasVersionError))]
    public partial string Version { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InstallerName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputError), nameof(HasOutputError))]
    public partial string OutputDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Credits { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string VersionHelp { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTestShortcut), nameof(ShowErrorsShortcut))]
    public partial bool HasQaErrors { get; private set; }

    [ObservableProperty]
    public partial string ChangesTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ChangesSubtitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ReceivedNote { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string FilesFolder { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string VerificationText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string VerificationTime { get; private set; } = string.Empty;

    /// <summary>"4 de 312 arquivos tocados".</summary>
    [ObservableProperty]
    public partial string GameFilesTotal { get; private set; } = string.Empty;

    /// <summary>A soma dos patches, na coluna "Patch" da linha de total.</summary>
    [ObservableProperty]
    public partial string GameFilesPatchTotal { get; private set; } = string.Empty;

    /// <summary>"e mais 38 linhas em 4 cenas", sob as falas de exemplo; vazio quando todas as falas estão à vista.</summary>
    [ObservableProperty]
    public partial string ChangesMore { get; private set; } = string.Empty;

    /// <summary>"e mais 2 arquivos alterados", sob os arquivos do cartão de checksum.</summary>
    [ObservableProperty]
    public partial string ChecksumMore { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ReadMeText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string GameFolder { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ProblemDetails { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowProblemDetails { get; set; }

    public bool IsReady => Phase == ReleasePhase.Ready;

    public bool IsGenerating => Phase == ReleasePhase.Generating;

    public bool IsDone => Phase == ReleasePhase.Done;

    public bool IsBlocked => Phase == ReleasePhase.Blocked;

    /// <summary>"O que o jogador recebe" fica na coluna da direita até a versão ficar pronta, quando o lugar é do leia-me.</summary>
    public bool ShowReceived => Phase != ReleasePhase.Done;

    public bool ShowChanges => Phase is ReleasePhase.Ready or ReleasePhase.Generating && Scenes.Count > 0;

    /// <summary>As falas de exemplo com o antes e o depois; sem elas a lista é a de cenas com a contagem.</summary>
    public bool HasChangeExamples => ChangeExamples.Count > 0;

    public bool HasSceneCounts => ChangeExamples.Count == 0;

    public bool HasChangesMore => ChangesMore.Length > 0;

    /// <summary>"O que o patch muda no jogo" é uma estimativa do patch anterior: aparece antes e durante a geração, e na versão pronta o lugar é da verificação.</summary>
    public bool ShowGameFiles => Phase is ReleasePhase.Ready or ReleasePhase.Generating && GameFiles.Count > 0;

    public bool HasChecksumFiles => ChecksumFiles.Count > 0;

    public bool HasChecksumMore => ChecksumMore.Length > 0;

    /// <summary>"Ver o Testar" na faixa de pronto; com erros na Conferência o botão é "Ver os erros".</summary>
    public bool ShowTestShortcut => IsReady && !HasQaErrors;

    public bool ShowErrorsShortcut => IsReady && HasQaErrors;

    public string GenerateLabel => IsGenerating ? "Gerando..." : "Gerar patch e instalador";

    public string? VersionError => IsValidVersion(Version) || Version.Length == 0
        ? null
        : "Use letras, números, ponto, hífen ou sublinhado (1.2.0).";

    public bool HasVersionError => VersionError is not null;

    public bool CanGenerate => Project is not null && Phase == ReleasePhase.Ready && !HasQaErrors && IsValidVersion(Version)
        && !string.IsNullOrWhiteSpace(OutputDirectory) && !HasOutputError;

    /// <summary>O aviso sob a pasta de saída quando ela é a pasta do jogo ou está dentro dela; os arquivos do lançamento nunca vão para o jogo.</summary>
    public string? OutputError => OutputInsideGame()
        ? "A pasta de saída não pode ser a pasta do jogo nem ficar dentro dela. Escolha outra."
        : null;

    public bool HasOutputError => OutputError is not null;

    private bool OutputInsideGame()
    {
        if (Project is null || readiness is null || string.IsNullOrWhiteSpace(OutputDirectory) || string.IsNullOrWhiteSpace(readiness.GameFolder))
        {
            return false;
        }

        try
        {
            return ReleaseService.IsInside(ResolvedOutput(Project.Root), readiness.GameFolder);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private string ResolvedOutput(string projectRoot) => Path.GetFullPath(OutputDirectory.Trim(), projectRoot);

    partial void OnProjectChanged(ProjectSummary? value)
    {
        // Uma geração do projeto anterior não pode mostrar o resultado dela na página do novo: cancela e invalida a época.
        generationEpoch++;
        CancelRunning?.Execute(null);
        CancelRunning = null;
        loadVersion++;
        readiness = null;
        OnPropertyChanged(nameof(OutputError));
        OnPropertyChanged(nameof(HasOutputError));
        outputEdited = false;
        installerEdited = false;
        SetDerived(() =>
        {
            Version = string.Empty;
            InstallerName = string.Empty;
            OutputDirectory = string.Empty;
            Credits = string.Empty;
        });
        SetPhase(ReleasePhase.Ready);
        ErrorMessage = null;
        HasQaErrors = false;
        LastVersionText = string.Empty;
        Scenes.Clear();
        ChangeExamples.Clear();
        Files.Clear();
        GameFiles.Clear();
        SetChecksum(null);
        ResetTexts();
        UpdateCommands();
    }

    partial void OnVersionChanged(string value)
    {
        if (!settingDerived)
        {
            SetDerived(() =>
            {
                if (!outputEdited && Project is not null)
                {
                    OutputDirectory = DefaultOutput(value);
                }

                if (!installerEdited && Project is not null && IsValidVersion(value))
                {
                    InstallerName = DefaultInstaller(value);
                }
            });
        }

        UpdateCommands();
    }

    partial void OnInstallerNameChanged(string value) => installerEdited |= !settingDerived;

    partial void OnOutputDirectoryChanged(string value)
    {
        outputEdited |= !settingDerived;
        UpdateCommands();
    }

    /// <summary>Lê o projeto de novo (a Conferência, o último teste, a versão anterior e o jogo); a janela chama ao abrir a página. Não faz nada enquanto gera.</summary>
    public Task ReloadAsync() => Loading = LoadAsync();

    private async Task LoadAsync()
    {
        var root = Project?.Root;
        if (root is null || Phase == ReleasePhase.Generating)
        {
            return;
        }

        var version = ++loadVersion;
        ReleaseReadiness? read = null;
        string? problem = null;
        try
        {
            read = await Task.Run(() => publisher.Inspect(root, CancellationToken.None), CancellationToken.None);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            problem = e.Message;
        }

        if (version != loadVersion || Phase == ReleasePhase.Generating)
        {
            return;
        }

        if (read is null)
        {
            readiness = null;
            SetPhase(ReleasePhase.Ready);
            ErrorMessage = problem;
            UpdateCommands();
            return;
        }

        Apply(read);
    }

    private void Apply(ReleaseReadiness read)
    {
        readiness = read;
        OnPropertyChanged(nameof(OutputError));
        OnPropertyChanged(nameof(HasOutputError));
        ErrorMessage = null;
        Files.Clear();
        ShowEstimatedGameFiles(read);
        SetChecksum(read.ChangedFiles);
        SetReceived(null);
        HasQaErrors = read.QaErrors > 0;
        GameFolder = read.GameFolder;
        LastVersionText = read.PreviousVersion is null ? string.Empty : $"Última versão: {read.PreviousVersion} · {DateText(read.PreviousAt)}";
        VersionHelp = read.PreviousVersion is null ? "Primeira versão do patch" : $"Patch anterior: {read.PreviousVersion}, de {DateText(read.PreviousAt)}";
        ShowChangesOf(read);
        SetChecks(read);
        SetDerived(() =>
        {
            if (Version.Length == 0)
            {
                Version = SuggestVersion(read.PreviousVersion);
            }

            if (!outputEdited)
            {
                OutputDirectory = DefaultOutput(Version);
            }

            if (!installerEdited && IsValidVersion(Version))
            {
                InstallerName = DefaultInstaller(Version);
            }
        });

        if (read.Reference == ReleaseReference.Differs)
        {
            ProblemDetails = read.ReferenceProblem ?? string.Empty;
            ShowProblemDetails = false;
            ShowBlocked();
        }
        else
        {
            SetPhase(ReleasePhase.Ready);
            ShowReady(read);
        }

        UpdateCommands();
    }

    private void ShowReady(ReleaseReadiness read)
    {
        if (read.QaErrors > 0)
        {
            BannerState = GameState.Blocked;
            BannerTitle = "A Conferência tem erros";
            BannerMessage = $"{ProjectLabels.Number(read.QaErrors)} {(read.QaErrors == 1 ? "erro impede" : "erros impedem")} o lançamento. Corrija em Conferir a tradução.";
            return;
        }

        BannerState = GameState.Translated;
        BannerTitle = "A tradução pode virar uma versão";
        var counts = Project?.Counts;
        var lines = counts is null ? string.Empty : $"{ProjectLabels.Number(counts.Total)} linhas · {ApprovedPercent(counts)} aprovadas · ";
        BannerMessage = read.Reference == ReleaseReference.Matches
            ? lines + "versão do jogo conferida pelo checksum"
            : lines + "o jogo é conferido pelo checksum ao gerar";
    }

    private void ShowBlocked()
    {
        SetPhase(ReleasePhase.Blocked);
        BannerState = GameState.Blocked;
        BannerTitle = "O jogo desta pasta não é a versão de referência";
        BannerMessage = "O patch só vale para a versão que o projeto usou. Nada foi gerado.";
    }

    private void SetChecks(ReleaseReadiness read)
    {
        Checks.Clear();
        Checks.Add(read.QaErrors == 0
            ? new ReleaseCheckRow("Conferir sem erros", true)
            : new ReleaseCheckRow($"Conferir com {ProjectLabels.Number(read.QaErrors)} {(read.QaErrors == 1 ? "erro" : "erros")}", false, IsError: true));
        Checks.Add(read.TestedAt is { } tested
            ? new ReleaseCheckRow($"Testado no jogo em {tested.ToLocalTime().ToString("dd/MM", Brazil)} às {tested.ToLocalTime().ToString("HH:mm", Brazil)}", true)
            : new ReleaseCheckRow("Ainda não testado no jogo", false));
        Checks.Add(read.Reference switch
        {
            ReleaseReference.Matches => new ReleaseCheckRow("Jogo de referência confere", true),
            ReleaseReference.Differs => new ReleaseCheckRow("Jogo de referência não confere", false, IsError: true),
            _ => new ReleaseCheckRow("Jogo de referência confere ao gerar", false),
        });
    }

    private void ShowChangesOf(ReleaseReadiness read)
    {
        Scenes.Clear();
        foreach (var scene in read.Changes.Scenes)
        {
            Scenes.Add(new ReleaseSceneRow($"Cena {scene.Number}", $"{ProjectLabels.Number(scene.Lines)} {(scene.Lines == 1 ? "linha" : "linhas")}"));
        }

        ChangeExamples.Clear();
        var examples = read.ChangeExamples ?? [];
        foreach (var example in examples)
        {
            ChangeExamples.Add(new ReleaseChangeRow($"Cena {example.SceneNumber} · linha {example.LineNumber}", example.Before, example.After));
        }

        var shownScenes = examples.Select(e => e.SceneNumber).ToHashSet();
        var moreLines = read.Changes.TotalLines - examples.Count;
        var moreScenes = read.Changes.Scenes.Count(s => s.Lines - (shownScenes.Contains(s.Number) ? 1 : 0) > 0);
        ChangesMore = examples.Count > 0 && moreLines > 0
            ? $"e mais {ProjectLabels.Number(moreLines)} {(moreLines == 1 ? "linha" : "linhas")} em {ProjectLabels.Number(moreScenes)} {(moreScenes == 1 ? "cena" : "cenas")}"
            : string.Empty;
        OnPropertyChanged(nameof(HasChangeExamples));
        OnPropertyChanged(nameof(HasSceneCounts));
        OnPropertyChanged(nameof(HasChangesMore));

        if (read.PreviousVersion is null)
        {
            ChangesTitle = string.Empty;
            ChangesSubtitle = string.Empty;
        }
        else
        {
            ChangesTitle = $"Mudou desde o patch {read.PreviousVersion}";
            ChangesSubtitle = read.Changes.State == ChangesSinceTestState.Changed
                ? $"{ProjectLabels.Number(read.Changes.TotalLines)} {(read.Changes.TotalLines == 1 ? "linha" : "linhas")} em {ProjectLabels.Number(read.Changes.Scenes.Count)} {(read.Changes.Scenes.Count == 1 ? "cena" : "cenas")} · vai para MUDANCAS.txt"
                : read.Changes.State == ChangesSinceTestState.Unchanged
                    ? "Nenhuma fala mudou · vai para MUDANCAS.txt"
                    : "A lista de falas que mudaram não está disponível para a versão anterior";
        }

        OnPropertyChanged(nameof(ShowChanges));
    }

    private void ResetTexts()
    {
        BannerState = GameState.Translated;
        BannerTitle = string.Empty;
        BannerMessage = string.Empty;
        VersionHelp = string.Empty;
        ChangesTitle = string.Empty;
        ChangesSubtitle = string.Empty;
        Checks.Clear();
        SetReceived(null);
        ReceivedNote = "Ao gerar, o app aplica o patch numa cópia limpa e compara com o build, byte a byte.";
        GameFilesTotal = string.Empty;
        GameFilesPatchTotal = string.Empty;
        ChangesMore = string.Empty;
        ResetSteps();
    }

    /// <summary>
    /// "O que o jogador recebe": os tamanhos do <paramref name="result"/> quando a versão ficou pronta; antes disso, os do patch anterior como estimativa;
    /// "ao gerar" na primeira versão.
    /// </summary>
    private void SetReceived(ReleaseResult? result)
    {
        Received.Clear();
        var files = result?.Files ?? readiness?.PreviousFiles;
        string Size(long? size) => size is { } value ? FormatSize(value) : "ao gerar";
        Received.Add(new ReleaseReceivedRow("Instalador de um arquivo", Size(files is null ? null : InstallerFile(files)?.Size), "Icon.Box"));
        Received.Add(new ReleaseReceivedRow("Patch de diferença binária", Size(files?.FirstOrDefault(f => f.Name.EndsWith(".vnfpatch", StringComparison.OrdinalIgnoreCase))?.Size), "Icon.Box"));
        Received.Add(new ReleaseReceivedRow(
            "Leia-me, créditos e mudanças",
            Size(files?.Where(f => f.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && f.Name != ThirdPartyLicenses.FileName).Sum(f => f.Size)),
            "Icon.Glossary"));
    }

    private static ReleaseFile? InstallerFile(IEnumerable<ReleaseFile> files) =>
        files.FirstOrDefault(f => !f.Name.EndsWith(".vnfpatch", StringComparison.OrdinalIgnoreCase) && !f.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

    /// <summary>A tabela "O que o patch muda no jogo" antes de gerar: os arquivos do patch anterior, que o próximo costuma repetir.</summary>
    private void ShowEstimatedGameFiles(ReleaseReadiness read)
    {
        GameFiles.Clear();
        GameFilesTotal = string.Empty;
        GameFilesPatchTotal = string.Empty;
        if (read.PreviousGameFiles is { Count: > 0 } files)
        {
            foreach (var file in files)
            {
                GameFiles.Add(GameFileRow(file));
            }

            var touched = $"{ProjectLabels.Number(files.Count)} {(files.Count == 1 ? "arquivo tocado" : "arquivos tocados")}";
            GameFilesTotal = read.GameFileCount is { } all && all >= files.Count
                ? $"{ProjectLabels.Number(files.Count)} de {ProjectLabels.Number(all)} arquivos tocados"
                : touched;
            GameFilesPatchTotal = FormatSize(files.Sum(f => f.PatchSize));
        }

        OnPropertyChanged(nameof(ShowGameFiles));
    }

    private static ReleaseGameFileRow GameFileRow(ReleaseGameFile file) => new(
        file.Path,
        file.Created ? "novo" : FormatSize(file.OriginalSize),
        FormatSize(file.PatchSize),
        file.Created || file.OriginalSize == 0 ? string.Empty : Ratio(file.PatchSize, file.OriginalSize));

    /// <summary>Os arquivos do jogo que não têm o SHA-256 da extração, com o esperado e o encontrado, até <see cref="MaxChecksumFiles"/>.</summary>
    private void SetChecksum(IReadOnlyList<ChangedGameFile>? changed)
    {
        ChecksumFiles.Clear();
        foreach (var file in (changed ?? []).Take(MaxChecksumFiles))
        {
            ChecksumFiles.Add(new ReleaseChecksumRow(
                file.Path,
                ShortSha(file.ExpectedSha256),
                file.FoundSha256 is null ? "arquivo ausente" : ShortSha(file.FoundSha256),
                file.ExpectedSha256,
                file.FoundSha256 ?? string.Empty));
        }

        var more = (changed?.Count ?? 0) - ChecksumFiles.Count;
        ChecksumMore = more > 0 ? $"e mais {ProjectLabels.Number(more)} {(more == 1 ? "arquivo alterado" : "arquivos alterados")}" : string.Empty;
        OnPropertyChanged(nameof(HasChecksumFiles));
        OnPropertyChanged(nameof(HasChecksumMore));
    }

    /// <summary>"SHA-256 3f9a…c21d": os quatro primeiros e os quatro últimos caracteres.</summary>
    private static string ShortSha(string sha) => sha.Length > 8 ? $"SHA-256 {sha[..4]}…{sha[^4..]}" : "SHA-256 " + sha;

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private Task GenerateAsync() => Generation = RunGenerationAsync();

    private async Task RunGenerationAsync()
    {
        if (Project is not { } project)
        {
            return;
        }

        var version = Version.Trim();
        var options = new ReleaseOptions(
            version,
            ResolvedOutput(project.Root),
            string.IsNullOrWhiteSpace(Credits) ? null : Credits.Trim(),
            InstallerBaseName(),
            AllowWarnings: false);
        ErrorMessage = null;
        ResetSteps();
        runningStep = null;
        BannerState = GameState.Translated;
        BannerTitle = $"Gerando a versão {version}";
        BannerMessage = "Pode continuar no editor. O app avisa quando terminar.";
        SetPhase(ReleasePhase.Generating);
        UpdateCommands();

        ReleaseResult? result = null;
        Exception? failure = null;
        var readMe = string.Empty;
        var epoch = generationEpoch;
        var item = tasks.Start($"Lançar a versão {version}", (reporter, ct) =>
        {
            try
            {
                ReleaseStep? announced = null;
                var sink = new StepSink(progress =>
                {
                    if (announced != progress.Step)
                    {
                        announced = progress.Step;
                        reporter.Report(null, LabelOf(progress.Step));
                    }

                    // O estado das etapas mora na thread da interface; o Core relata de outra.
                    tasks.Post(() =>
                    {
                        if (epoch == generationEpoch && IsGenerating)
                        {
                            Observe(progress);
                        }
                    });
                });
                result = publisher.Create(project.Root, options, sink, ct);
                readMe = ReadText(Path.Combine(result.OutputDirectory, "LEIA-ME.txt"));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A página mostra o erro com o texto dela; a exceção sai para a barra de tarefas marcar a falha.
                failure = e;
                throw;
            }

            return Task.CompletedTask;
        });
        CancelRunning = item.CancelCommand;
        CancelGenerationCommand.NotifyCanExecuteChanged();

        try
        {
            await item.Completion;
        }
        finally
        {
            if (ReferenceEquals(CancelRunning, item.CancelCommand))
            {
                CancelRunning = null;
            }

            CancelGenerationCommand.NotifyCanExecuteChanged();
        }

        if (epoch != generationEpoch)
        {
            return;
        }

        if (result is not null)
        {
            Finish(result, readMe);
        }
        else
        {
            Fail(failure, version);
        }

        UpdateCommands();
    }

    private IRelayCommand? CancelRunning { get; set; }

    [RelayCommand(CanExecute = nameof(CanCancelGeneration))]
    private void CancelGeneration() => CancelRunning?.Execute(null);

    private bool CanCancelGeneration() => IsGenerating && CancelRunning is not null;

    /// <summary>
    /// Um relato do Core. O primeiro de um passo o abre (os de antes ficam prontos e o tempo do que estava aberto fecha); os seguintes
    /// trazem o que o passo achou e o quanto já fez.
    /// </summary>
    private void Observe(ReleaseProgress progress)
    {
        var current = Steps.FirstOrDefault(s => s.Step == progress.Step);
        if (current is null)
        {
            return;
        }

        if (!ReferenceEquals(current, runningStep))
        {
            CloseRunning();
            foreach (var row in Steps.Where(r => r.Step < progress.Step && !r.IsDone))
            {
                row.State = ReleaseStepState.Done;
            }

            current.State = ReleaseStepState.Running;
            runningStep = current;
            runningSince = clock.GetTimestamp();
        }

        if (progress.Detail is not null)
        {
            current.Detail = progress.Detail;
        }

        if (progress.Item is not null)
        {
            current.Item = progress.Item;
        }

        if (progress.Fraction is { } fraction)
        {
            current.Fraction = Math.Clamp(fraction, 0, 1);
        }
    }

    private void CloseRunning()
    {
        if (runningStep is null)
        {
            return;
        }

        runningStep.State = ReleaseStepState.Done;
        runningStep.TimeText = FormatSeconds(clock.GetElapsedTime(runningSince));
        runningStep.Fraction = null;
        runningStep.Item = string.Empty;
        runningStep = null;
    }

    private void ResetSteps()
    {
        foreach (var step in Steps)
        {
            step.State = ReleaseStepState.Pending;
            step.TimeText = string.Empty;
            step.Detail = string.Empty;
            step.Item = string.Empty;
            step.Fraction = null;
        }
    }

    private void Finish(ReleaseResult result, string readMe)
    {
        CloseRunning();
        foreach (var step in Steps)
        {
            step.State = ReleaseStepState.Done;
        }

        Files.Clear();

        // LICENCAS.txt segue na pasta de saída (a licença dos componentes de terceiros), mas não é um dos arquivos que o jogador procura: a lista mostra os cinco do quadro.
        foreach (var file in result.Files.Where(f => f.Name != ThirdPartyLicenses.FileName))
        {
            var isPackage = file.Sha256 is not null;
            Files.Add(new ReleaseFileRow(file.Name, FormatSize(file.Size), file.Sha256, isPackage ? "Icon.Box" : "Icon.Glossary"));
        }

        // Na versão pronta o lugar da tabela de estimativa é da verificação.
        GameFiles.Clear();
        GameFilesTotal = string.Empty;
        GameFilesPatchTotal = string.Empty;
        var count = result.GameFiles.Count;
        FilesFolder = result.OutputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        installerPath = InstallerFile(result.Files) is { } installer ? Path.Combine(result.OutputDirectory, installer.Name) : null;
        var verification = result.Verification;
        BannerState = GameState.Translated;
        BannerTitle = $"Versão {result.Version} pronta";
        BannerMessage = $"Verificada numa cópia limpa: {ProjectLabels.Number(verification.Identical)} de {ProjectLabels.Number(count)} {(count == 1 ? "arquivo idêntico" : "arquivos idênticos")} ao build. Desinstalar devolve os originais byte a byte.";
        VerificationText = $"Cópia limpa do jogo de referência: {ProjectLabels.Number(verification.Identical)} {(verification.Identical == 1 ? "arquivo alterado, idêntico" : "arquivos alterados, todos idênticos")} ao build. "
            + $"Desinstalação testada: {ProjectLabels.Number(verification.RestoredToOriginal)} {(verification.RestoredToOriginal == 1 ? "arquivo" : "arquivos")} de volta ao original, byte a byte"
            + (verification.RemovedCreated > 0 ? $", e {ProjectLabels.Number(verification.RemovedCreated)} {(verification.RemovedCreated == 1 ? "criado saiu" : "criados saíram")}." : ".");
        VerificationTime = Steps.First(s => s.Step == ReleaseStep.Verify).TimeText;
        ReadMeText = readMe.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        SetReceived(result);
        SetPhase(ReleasePhase.Done);
        NoticeRaised?.Invoke(this, $"Versão {result.Version} pronta em {FilesFolder}");
    }

    private void Fail(Exception? failure, string version)
    {
        ResetSteps();
        runningStep = null;
        if (failure is InjectionException blocked)
        {
            SetChecksum(blocked.ChangedFiles.Count > 0 ? blocked.ChangedFiles : readiness?.ChangedFiles);
            ProblemDetails = blocked.Message;
            ShowProblemDetails = false;
            ShowBlocked();
            return;
        }

        SetPhase(ReleasePhase.Ready);
        if (readiness is not null)
        {
            ShowReady(readiness);
        }

        ErrorMessage = failure switch
        {
            null => $"A geração da versão {version} foi cancelada. Nada foi gravado no jogo.",
            QaBlockedException => failure.Message,
            VNForgeException => failure.Message,
            _ => $"Não consegui gerar a versão {version}: {failure.Message}",
        };
    }

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        if (Picker is null)
        {
            return;
        }

        if (await Picker.PickFolderAsync("Pasta de saída da versão") is { } folder)
        {
            OutputDirectory = folder;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (OutputDirectory.Length > 0 && Directory.Exists(OutputDirectory))
        {
            openFolder(OutputDirectory);
        }
    }

    [RelayCommand]
    private void TestInstaller()
    {
        if (installerPath is not null && File.Exists(installerPath))
        {
            starter.Start(installerPath, Path.GetDirectoryName(installerPath)!);
        }
    }

    [RelayCommand]
    private void CopyChecksum(ReleaseFileRow? file)
    {
        if (file?.Sha256 is { } sha)
        {
            CopyRequested?.Invoke(this, sha);
        }
    }

    [RelayCommand]
    private void ShowTest() => TestRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ShowErrors() => ErrorsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ChooseGameFolder() => GameFolderRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ToggleProblemDetails() => ShowProblemDetails = !ShowProblemDetails;

    /// <summary>A versão que o Core aceita: letras, números, ponto, hífen e sublinhado, começando por letra ou número.</summary>
    public static bool IsValidVersion(string? version) => version is { Length: > 0 } && VersionPattern().IsMatch(version.Trim());

    /// <summary>1.1.0 vira 1.2.0; sem versão anterior, 1.0.0; um número que não tem a forma 1.x.y recebe um "-2" de partida.</summary>
    public static string SuggestVersion(string? previous)
    {
        if (previous is null)
        {
            return "1.0.0";
        }

        var match = SemanticVersion().Match(previous);
        if (!match.Success)
        {
            return previous + "-2";
        }

        var major = match.Groups[1].Value;
        var minor = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) + 1;
        return $"{major}.{minor.ToString(CultureInfo.InvariantCulture)}.0";
    }

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z._-]*$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^(\d+)\.(\d+)(?:\.\d+)?$")]
    private static partial Regex SemanticVersion();

    private string DefaultOutput(string version) =>
        Project is null ? string.Empty : Path.Combine(Project.Root, ReleaseService.FolderName, IsValidVersion(version) ? version.Trim() : "versao");

    private string DefaultInstaller(string version)
    {
        try
        {
            return publisher.DefaultInstallerName(Project!.Root, version.Trim()) + InstallerExtension;
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private string? InstallerBaseName()
    {
        var name = InstallerName.Trim();
        if (InstallerExtension.Length > 0 && name.EndsWith(InstallerExtension, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^InstallerExtension.Length];
        }

        return name.Length == 0 ? null : name;
    }

    private void SetDerived(Action change)
    {
        settingDerived = true;
        try
        {
            change();
        }
        finally
        {
            settingDerived = false;
        }
    }

    private void SetPhase(ReleasePhase phase)
    {
        Phase = phase;
        ReceivedNote = phase switch
        {
            ReleasePhase.Generating when readiness?.PreviousFiles is not null => $"Estimativa a partir do patch {readiness.PreviousVersion}. O tamanho final aparece ao fim do cálculo.",
            ReleasePhase.Generating => "O tamanho final aparece ao fim do cálculo.",
            ReleasePhase.Blocked => "Disponível quando o jogo de referência confere.",
            _ => "Ao gerar, o app aplica o patch numa cópia limpa e compara com o build, byte a byte.",
        };
        OnPropertyChanged(nameof(ShowChanges));
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.NotifyCanExecuteChanged();
        CancelGenerationCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Fechar a janela no meio do lançamento cancela a tarefa; o Core só grava no jogo na instalação, então nada fica pela metade.</summary>
    public void CancelIfRunning() => CancelRunning?.Execute(null);

    private static string LabelOf(ReleaseStep step) => StepLabels.First(s => s.Step == step).Label;

    private static string ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string DateText(DateTimeOffset? at) => at is { } value ? value.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture) : string.Empty;

    private static string ApprovedPercent(StatusCounts counts) =>
        (counts.Total == 0 ? 0 : (int)Math.Floor(100.0 * counts.Approved / counts.Total)).ToString(CultureInfo.InvariantCulture) + "%";

    private static string Ratio(long patch, long original) =>
        (100.0 * patch / original).ToString("0.0", PtBr) + "%";

    private static string FormatSeconds(TimeSpan time) => time.TotalSeconds.ToString("0.0", PtBr) + " s";

    /// <summary>"5,1 MB", "421 KB", "14 KB"; abaixo de 1 KB, "812 B".</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => (bytes / 1024.0 / 1024.0).ToString("0.0", PtBr) + " MB",
        >= 1024 => Math.Max(1, (long)Math.Round(bytes / 1024.0)).ToString("N0", PtBr) + " KB",
        _ => bytes.ToString("N0", PtBr) + " B",
    };

    private sealed class StepSink(Action<ReleaseProgress> report) : IProgress<ReleaseProgress>
    {
        public void Report(ReleaseProgress value) => report(value);
    }
}
