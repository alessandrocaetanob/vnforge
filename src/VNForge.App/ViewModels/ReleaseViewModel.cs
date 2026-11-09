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

/// <summary>Um dos seis passos de <see cref="ReleaseService.Create"/>, na ordem do Core, com o tempo que levou.</summary>
public sealed partial class ReleaseStepRow(ReleaseStep step, string label) : ObservableObject
{
    public ReleaseStep Step { get; } = step;

    public string Label { get; } = label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending), nameof(IsRunning), nameof(IsDone))]
    public partial ReleaseStepState State { get; set; }

    [ObservableProperty]
    public partial string TimeText { get; set; } = string.Empty;

    public bool IsPending => State == ReleaseStepState.Pending;

    public bool IsRunning => State == ReleaseStepState.Running;

    public bool IsDone => State == ReleaseStepState.Done;
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
        (ReleaseStep.Diff, "Calcular as diferenças e gravar o patch"),
        (ReleaseStep.Verify, "Verificar numa cópia limpa"),
        (ReleaseStep.Package, "Montar o instalador"),
        (ReleaseStep.Documents, "Gravar leia-me, créditos e mudanças"),
    ];

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

    public ObservableCollection<ReleaseGameFileRow> GameFiles { get; } = [];

    /// <summary>Termina quando a última leitura pedida por <see cref="ReloadAsync"/> acaba.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>Termina quando a última geração pedida acaba; os testes a aguardam.</summary>
    public Task Generation { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial ProjectSummary? Project { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(IsGenerating), nameof(IsDone), nameof(IsBlocked), nameof(ShowReceived), nameof(ShowChanges), nameof(GenerateLabel), nameof(ShowTestShortcut), nameof(ShowErrorsShortcut))]
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

    [ObservableProperty]
    public partial string GameFilesTotal { get; private set; } = string.Empty;

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

    /// <summary>"Ver o Testar" na faixa de pronto; com erros na Conferência o botão é "Ver os erros".</summary>
    public bool ShowTestShortcut => IsReady && !HasQaErrors;

    public bool ShowErrorsShortcut => IsReady && HasQaErrors;

    public string GenerateLabel => IsGenerating ? "Gerando..." : "Gerar patch e instalador";

    public string? VersionError => IsValidVersion(Version) || Version.Length == 0
        ? null
        : "Use letras, números, ponto, hífen ou sublinhado (1.2.0).";

    public bool HasVersionError => VersionError is not null;

    public bool CanGenerate => Project is not null && Phase == ReleasePhase.Ready && !HasQaErrors && IsValidVersion(Version)
        && !string.IsNullOrWhiteSpace(OutputDirectory);

    partial void OnProjectChanged(ProjectSummary? value)
    {
        loadVersion++;
        readiness = null;
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
        Files.Clear();
        GameFiles.Clear();
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
        ErrorMessage = null;
        Files.Clear();
        GameFiles.Clear();
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
        foreach (var step in Steps)
        {
            step.State = ReleaseStepState.Pending;
            step.TimeText = string.Empty;
        }
    }

    private void SetReceived(ReleaseResult? result)
    {
        Received.Clear();
        string Size(string name) => result is null ? "ao gerar" : FormatSize(result.Files.FirstOrDefault(f => f.Name.EndsWith(name, StringComparison.OrdinalIgnoreCase))?.Size ?? 0);
        Received.Add(new ReleaseReceivedRow("Instalador de um arquivo", result is null ? "ao gerar" : FormatSize(InstallerFile(result)?.Size ?? 0), "Icon.Box"));
        Received.Add(new ReleaseReceivedRow("Patch de diferença binária", Size(".vnfpatch"), "Icon.Box"));
        Received.Add(new ReleaseReceivedRow("Leia-me, créditos e mudanças", result is null ? "ao gerar" : FormatSize(result.Files.Where(f => f.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && f.Name != ThirdPartyLicenses.FileName).Sum(f => f.Size)), "Icon.Glossary"));
    }

    private static ReleaseFile? InstallerFile(ReleaseResult result) =>
        result.Files.FirstOrDefault(f => !f.Name.EndsWith(".vnfpatch", StringComparison.OrdinalIgnoreCase) && !f.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

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
            Path.GetFullPath(OutputDirectory.Trim()),
            string.IsNullOrWhiteSpace(Credits) ? null : Credits.Trim(),
            InstallerBaseName(),
            AllowWarnings: false);
        ErrorMessage = null;
        foreach (var step in Steps)
        {
            step.State = ReleaseStepState.Pending;
            step.TimeText = string.Empty;
        }

        runningStep = null;
        BannerState = GameState.Translated;
        BannerTitle = $"Gerando a versão {version}";
        BannerMessage = "Pode continuar no editor. O app avisa quando terminar.";
        SetPhase(ReleasePhase.Generating);
        UpdateCommands();

        ReleaseResult? result = null;
        Exception? failure = null;
        var readMe = string.Empty;
        var item = tasks.Start($"Lançar a versão {version}", (reporter, ct) =>
        {
            try
            {
                var sink = new StepSink(step => reporter.Report(null, LabelOf(step)));
                result = publisher.Create(project.Root, options, sink, ct);
                readMe = ReadText(Path.Combine(result.OutputDirectory, "LEIA-ME.txt"));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failure = e;
            }

            return Task.CompletedTask;
        });
        CancelRunning = item.CancelCommand;
        CancelGenerationCommand.NotifyCanExecuteChanged();

        void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BackgroundTaskItem.Message) && item.IsRunning && StepOf(item.Message) is { } step)
            {
                Observe(step);
            }
        }

        item.PropertyChanged += OnChanged;
        try
        {
            await item.Completion;
        }
        finally
        {
            item.PropertyChanged -= OnChanged;
            CancelRunning = null;
            CancelGenerationCommand.NotifyCanExecuteChanged();
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

    /// <summary>O passo que o Core acabou de abrir: os de antes ficam prontos e o tempo do que estava aberto fecha.</summary>
    private void Observe(ReleaseStep step)
    {
        CloseRunning();
        foreach (var row in Steps)
        {
            if (row.Step < step)
            {
                row.State = ReleaseStepState.Done;
            }
            else if (row.Step == step)
            {
                row.State = ReleaseStepState.Running;
                runningStep = row;
                runningSince = clock.GetTimestamp();
            }
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
        runningStep = null;
    }

    private void Finish(ReleaseResult result, string readMe)
    {
        CloseRunning();
        foreach (var step in Steps)
        {
            step.State = ReleaseStepState.Done;
        }

        Files.Clear();
        foreach (var file in result.Files)
        {
            var isPackage = file.Sha256 is not null;
            Files.Add(new ReleaseFileRow(file.Name, FormatSize(file.Size), file.Sha256, isPackage ? "Icon.Box" : "Icon.Glossary"));
        }

        GameFiles.Clear();
        foreach (var file in result.GameFiles)
        {
            GameFiles.Add(new ReleaseGameFileRow(
                file.Path,
                file.Created ? "novo" : FormatSize(file.OriginalSize),
                FormatSize(file.PatchSize),
                file.Created || file.OriginalSize == 0 ? string.Empty : Ratio(file.PatchSize, file.OriginalSize)));
        }

        var count = result.GameFiles.Count;
        GameFilesTotal = $"{ProjectLabels.Number(count)} {(count == 1 ? "arquivo tocado" : "arquivos tocados")}";
        FilesFolder = result.OutputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        installerPath = InstallerFile(result) is { } installer ? Path.Combine(result.OutputDirectory, installer.Name) : null;
        var verification = result.Verification;
        BannerState = GameState.Translated;
        BannerTitle = $"Versão {result.Version} pronta";
        BannerMessage = $"Verificada numa cópia limpa: {ProjectLabels.Number(verification.Identical)} de {ProjectLabels.Number(count)} {(count == 1 ? "arquivo idêntico" : "arquivos idênticos")} ao build. Desinstalar devolve os originais byte a byte.";
        VerificationText = $"Cópia limpa do jogo de referência: {ProjectLabels.Number(verification.Identical)} {(verification.Identical == 1 ? "arquivo alterado, idêntico" : "arquivos alterados, todos idênticos")} ao build. "
            + $"Desinstalação testada: {ProjectLabels.Number(verification.RestoredToOriginal)} {(verification.RestoredToOriginal == 1 ? "arquivo voltou" : "arquivos voltaram")} ao original, byte a byte"
            + (verification.RemovedCreated > 0 ? $", e {ProjectLabels.Number(verification.RemovedCreated)} {(verification.RemovedCreated == 1 ? "criado saiu" : "criados saíram")}." : ".");
        VerificationTime = Steps.First(s => s.Step == ReleaseStep.Verify).TimeText;
        ReadMeText = readMe.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        SetReceived(result);
        SetPhase(ReleasePhase.Done);
        NoticeRaised?.Invoke(this, $"Versão {result.Version} pronta em {FilesFolder}");
    }

    private void Fail(Exception? failure, string version)
    {
        foreach (var step in Steps)
        {
            step.State = ReleaseStepState.Pending;
            step.TimeText = string.Empty;
        }

        runningStep = null;
        if (failure is InjectionException blocked)
        {
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

    private static ReleaseStep? StepOf(string? label) =>
        StepLabels.Cast<(ReleaseStep Step, string Label)?>().FirstOrDefault(s => s!.Value.Label == label)?.Step;

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

    private sealed class StepSink(Action<ReleaseStep> report) : IProgress<ReleaseStep>
    {
        public void Report(ReleaseStep value) => report(value);
    }
}
