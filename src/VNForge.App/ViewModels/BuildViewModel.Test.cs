using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Core.Qa;

namespace VNForge.App.ViewModels;

/// <summary>Em que pé está um dos cinco passos do teste no jogo.</summary>
public enum TestStepState
{
    /// <summary>Ainda não rodou, ou nunca rodou neste projeto.</summary>
    Pending,

    /// <summary>Está rodando agora.</summary>
    Running,

    /// <summary>Terminou.</summary>
    Done,

    /// <summary>Impede o teste (a Conferência tem erros).</summary>
    Blocked,

    /// <summary>O teste parou neste passo: falhou ou foi cancelado.</summary>
    Failed,
}

/// <summary>Um dos cinco passos do teste no jogo: o nome, o tempo (ou o motivo de estar travado) e o estado. <paramref name="IsLast"/> some com o traço que liga ao próximo.</summary>
public sealed record TestStepRow(string Label, string TimeText, TestStepState State = TestStepState.Done, bool IsLast = false)
{
    public bool IsPending => State == TestStepState.Pending;

    public bool IsRunning => State == TestStepState.Running;

    public bool IsDone => State == TestStepState.Done;

    public bool IsBlocked => State == TestStepState.Blocked;

    public bool IsFailed => State == TestStepState.Failed;

    /// <summary>Travado ou falhou: o ícone e o texto saem em vermelho.</summary>
    public bool IsProblem => IsBlocked || IsFailed;
}

/// <summary>Onde o último teste parou e o que dizer: o passo, a frase curta no passo e a legenda dos passos.</summary>
internal sealed record TestStop(GameTestStep Step, string Text, string Caption, bool PreviousDone);

/// <summary>
/// "Testar no jogo": o botão único que confere, guarda a cópia de segurança, injeta e abre o jogo (<see cref="GameTester"/>, o serviço do <c>vnf test</c>),
/// a faixa com o estado do jogo e "Voltar ao jogo original". Os botões de build, injeção e restauração separados seguem na tela, em "Detalhes técnicos".
/// </summary>
public sealed partial class BuildViewModel
{
    private const string StoppedText = "O teste parou antes de terminar. Os detalhes estão em Detalhes técnicos, no registro.";
    private const string ReturnStoppedText = "Não foi possível voltar ao jogo original. Os detalhes estão em Detalhes técnicos, no registro.";

    private GameStateFile savedGame = GameStateFile.Empty;
    private bool gameReturnable;
    private string? openSceneKey;

    /// <summary>"O jogo está com a tradução de 22/10", "O jogo está como veio" ou o que trava o teste.</summary>
    [ObservableProperty]
    public partial string GameBannerTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string GameBannerMessage { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial GameState GameBannerState { get; private set; } = GameState.Original;

    /// <summary>O que "Voltar ao jogo original" faz, ou por que não dá para fazer.</summary>
    [ObservableProperty]
    public partial string ReturnToOriginalHint { get; private set; } = "Devolve os arquivos originais ao jogo. A tradução continua no projeto.";

    /// <summary>As cenas cuja tradução mudou desde o último teste no jogo.</summary>
    public ChangedScenesViewModel SinceTest { get; } = new();

    /// <summary>"Jogo com a tradução de 22/10" ou "Jogo original", para a barra de status.</summary>
    [ObservableProperty]
    public partial string GameStatusText { get; private set; } = string.Empty;

    /// <summary>Testa só a cena que está aberta no Editor; as outras ficam como no jogo original.</summary>
    [ObservableProperty]
    public partial bool OnlyOpenScene { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpenScene))]
    [NotifyPropertyChangedFor(nameof(OpenSceneDescription))]
    public partial string OpenSceneName { get; private set; } = string.Empty;

    /// <summary>"Último teste · 22/10 às 23:16 · 3,4 s"; vazio enquanto o projeto não teve teste.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastTest))]
    public partial string LastTestTitle { get; private set; } = string.Empty;

    /// <summary>A linha acima dos passos: o último teste, "Testando agora", o que trava ou "Ainda não houve teste neste projeto".</summary>
    [ObservableProperty]
    public partial string TestStepsCaption { get; private set; } = string.Empty;

    /// <summary>Os cinco passos do teste, sempre os mesmos cinco: com o tempo do último teste, em andamento, travados ou cinza.</summary>
    public ObservableCollection<TestStepRow> LastTestSteps { get; } = [];

    private bool testRunning;
    private int testEpoch;
    private GameTestStep currentStep;
    private TestStop? stopped;

    public bool HasLastTest => LastTestTitle.Length > 0;

    public bool HasOpenScene => OpenSceneName.Length > 0;

    /// <summary>A linha de apoio de "Só a cena aberta no editor".</summary>
    public string OpenSceneDescription => HasOpenScene
        ? $"Só a cena “{OpenSceneName}” vai para o jogo; o resto fica em japonês."
        : "Abra uma cena no editor para testar só ela.";

    /// <summary>A cena aberta no Editor; a janela chama ao abrir a tela. Sem cena, a opção "Só a cena aberta" se desliga.</summary>
    public void SetOpenScene(string? key, string? name)
    {
        openSceneKey = string.IsNullOrEmpty(key) ? null : key;
        OpenSceneName = SceneLabel(openSceneKey, name);
        if (openSceneKey is null)
        {
            OnlyOpenScene = false;
        }
    }

    private static string SceneLabel(string? key, string? name)
    {
        if (key is null)
        {
            return string.Empty;
        }

        return string.IsNullOrEmpty(name) ? key : name;
    }

    private bool CanTest() => Project is { IsSample: false } && !IsBusy && !HasQaErrors;

    private bool CanTestAccents() => Project is { IsSample: false } && !IsBusy;

    private bool CanReturnToOriginal() => Project is { IsSample: false } && !IsBusy && gameReturnable;

    private void UpdateTestCommands()
    {
        TestCommand.NotifyCanExecuteChanged();
        TestAccentsCommand.NotifyCanExecuteChanged();
        ReturnToOriginalCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanTest))]
    private Task TestAsync() =>
        RunTestAsync(new GameTestOptions(OnlyOpenScene ? openSceneKey : null, AllowWarnings: AllowWarnings, OnlyChanged: OnlyChanged));

    [RelayCommand(CanExecute = nameof(CanTestAccents))]
    private Task TestAccentsAsync() => RunTestAsync(new GameTestOptions(AccentTest: true));

    private async Task RunTestAsync(GameTestOptions options)
    {
        if (Project is not { } summary)
        {
            return;
        }

        ClearConfirmation();
        ErrorMessage = null;
        restoredFrom = null;
        var mine = epoch;
        IsBusy = true;
        stopped = null;
        testRunning = true;
        testEpoch = mine;
        ShowRunningStep(GameTestStep.Check);
        var timer = StartTimer();
        try
        {
            var project = ProjectFolder.Open(summary.Root);
            var tester = new GameTester(engines, starter, qa, clock);
            AppendLog(options.AccentTest
                ? "Teste de acentos: só a frase fixa vai para o jogo."
                : "Teste no jogo: conferindo, guardando a cópia de segurança e copiando para o jogo.");
            var run = await RunAsync(
                options.AccentTest ? "Testar os acentos no jogo" : "Testar no jogo",
                GameTestStep.Check.Label(),
                (reporter, ct) => tester.Run(project, options, new StepProgress(reporter), new TaskProgress(reporter), ct),
                onMessage: message =>
                {
                    if (StepOf(message) is { } step)
                    {
                        ShowRunningStep(step);
                    }
                });
            if (mine != epoch)
            {
                return;
            }

            timer.Stop();
            if (run.Value is { } result)
            {
                DescribeTest(result);
            }
            else if (run.Failure is not null)
            {
                FailTest(run.Failure);
                stopped = StopOf(options, run.Failure);
            }
            else
            {
                ReportStopped("Teste");
                stopped = StopOf(options, null);
            }
        }
        catch (Exception e) when (IsExpected(e))
        {
            if (mine == epoch)
            {
                FailTest(e);
                stopped = StopOf(options, e);
            }
        }
        finally
        {
            testRunning = false;
            IsBusy = false;
            if (mine == epoch)
            {
                LogSummary = timer.Summary;
            }
        }

        await RefreshAsync();
        if (mine == epoch)
        {
            RaiseNotice();
        }
    }

    [RelayCommand(CanExecute = nameof(CanReturnToOriginal))]
    private async Task ReturnToOriginalAsync()
    {
        if (Project is not { } summary)
        {
            return;
        }

        ClearConfirmation();
        ErrorMessage = null;
        var mine = epoch;
        IsBusy = true;
        var timer = StartTimer();
        try
        {
            var project = ProjectFolder.Open(summary.Root);
            var tester = new GameTester(engines, starter, qa, clock);
            var run = await RunAsync("Voltar ao jogo original", "Devolvendo os arquivos originais ao jogo", (_, _) => tester.ReturnToOriginal(project));
            if (mine != epoch)
            {
                return;
            }

            timer.Stop();
            if (run.Value is { } result)
            {
                restoredFrom = result.BackupDirectory;
                AppendLog($"Restaurado: {ProjectLabels.Number(result.Restored.Count)} arquivo(s); removido: {ProjectLabels.Number(result.Removed.Count)} arquivo(s) criado(s) pela injeção.");
                foreach (var warning in result.Warnings)
                {
                    AppendLog($"Aviso: {warning}");
                }
            }
            else if (run.Failure is not null)
            {
                FailQuietly("Não foi possível voltar ao jogo original", run.Failure, ReturnStoppedText);
            }
            else
            {
                ReportStopped("Volta ao jogo original");
            }
        }
        catch (Exception e) when (IsExpected(e))
        {
            if (mine == epoch)
            {
                FailQuietly("Não foi possível voltar ao jogo original", e, ReturnStoppedText);
            }
        }
        finally
        {
            IsBusy = false;
            if (mine == epoch)
            {
                LogSummary = timer.Summary;
            }
        }

        await RefreshAsync();
    }

    private void DescribeTest(GameTestResult result)
    {
        foreach (var step in result.Steps)
        {
            AppendLog($"{step.Step.Label()} · {FormatDuration(step.Elapsed)}");
        }

        var injection = result.Injection;
        if (injection.BackupDirectory is not null)
        {
            AppendLog($"Backup: {injection.BackupDirectory}");
            AppendLog($"Injetado: {ProjectLabels.Number(injection.Written.Count)} arquivo(s) em {injection.GameDirectory} (hashes conferidos)");
        }
        else
        {
            AppendLog($"O jogo já estava igual ao build: nada foi copiado ({ProjectLabels.Number(injection.UnchangedCount)} arquivo(s) iguais).");
        }

        if (result.Executable is not null)
        {
            AppendLog($"Jogo aberto: {result.Executable}");
        }

        notice = new BuildNotice(
            BuildNoticeKind.Tested,
            result.Kind == GameTestKind.AccentTest ? "O jogo abriu só com a frase dos acentos." : "O jogo abriu com a tradução.");
    }

    /// <summary>
    /// A mensagem da tela fica em linguagem comum e sem caminho; a mensagem completa do Core vai para o registro, em "Detalhes técnicos".
    /// </summary>
    private void FailTest(Exception failure)
    {
        switch (failure)
        {
            case QaBlockedException blocked:
                foreach (var issue in blocked.Report.BlockingIssues.Take(MaxQaIssuesLogged))
                {
                    AppendLog(QaLine(issue));
                }

                Fail(
                    "Teste travado por erros",
                    $"Há erros que travam o teste ({QaBlockedException.DescribeBlocking(blocked.Report)}) e nada foi copiado para o jogo. Corrija em Conferir a tradução, ou ligue “Testar mesmo com avisos” para liberar só os avisos.");
                break;
            case GameExecutableNotFoundException:
                FailQuietly("Programa do jogo não encontrado", failure, "O VNForge não encontrou o programa do jogo, e nada foi copiado. Confira a pasta do jogo nos Ajustes.");
                break;
            case GamePatchRefusedException:
                FailQuietly("Ajuste da fonte recusado", failure, "O VNForge não conseguiu ajustar a fonte no programa do jogo, e nada foi copiado. Os detalhes estão em Detalhes técnicos, no registro.");
                break;
            case GameOpenException:
                FailQuietly("O jogo recebeu a tradução, mas não abriu", failure, "O jogo recebeu a tradução, mas não abriu. Abra o jogo por conta própria; para desfazer, use “Voltar ao jogo original”.");
                break;
            default:
                FailQuietly("O teste parou", failure, StoppedText);
                break;
        }
    }

    /// <summary>
    /// A barra da tela leva uma frase fixa em linguagem comum; a mensagem do Core, que pode ter caminho de pasta e nome de comando, vai só para o registro.
    /// A trava de "outra operação em andamento" já é uma frase sem caminho e aparece como está.
    /// </summary>
    private void FailQuietly(string logTitle, Exception failure, string shown)
    {
        AppendLog($"{logTitle}: {failure.Message}");
        ErrorMessage = failure is ProjectBusyException ? failure.Message : shown;
    }

    /// <summary>Lê o estado do jogo e do último teste e põe a faixa, a barra de status e a lista de passos no lugar.</summary>
    private void ShowGameState(ProjectState? state)
    {
        savedGame = state?.Saved ?? GameStateFile.Empty;
        gameReturnable = state?.Restore is not null;
        var inGame = state?.Restore is { } restore ? Effective(savedGame.InGame, restore.Manifest.CreatedAt, Path.GetFileName(restore.BackupDirectory)) : null;
        var lost = state is not null && state.Restore is null ? LostBackup(state) : null;
        ReturnToOriginalHint = lost is null
            ? "Devolve os arquivos originais ao jogo. A tradução continua no projeto."
            : "Sem a cópia de segurança o VNForge não consegue devolver o jogo. Reinstale o jogo ou use a verificação de arquivos da loja onde você o comprou.";
        GameStatusText = StatusText(state, lost, inGame);
        ShowBanner(state, inGame, lost);
        ShowLastTest(state is null ? null : savedGame.LastTest);
        if (state is null)
        {
            SinceTest.Clear();
        }
        else
        {
            SinceTest.Update(state.Changes, scene => state.SceneNames.DisplayName(scene.Key) ?? $"Cena {scene.Number}");
        }

        UpdateTestCommands();
    }

    private static string StatusText(ProjectState? state, string? lost, GameInGame? inGame)
    {
        if (state is null)
        {
            return string.Empty;
        }

        if (lost is not null)
        {
            return "Jogo com a tradução, sem cópia de segurança";
        }

        return inGame is null ? "Jogo original" : ShortGameStatus(inGame);
    }

    /// <summary>A faixa: travada pela Conferência, avisando que a cópia de segurança sumiu, ou o estado do jogo (traduzido ou como veio).</summary>
    private void ShowBanner(ProjectState? state, GameInGame? inGame, string? lost)
    {
        if (Project is { IsSample: true })
        {
            GameBannerState = GameState.Original;
            GameBannerTitle = "Esta é uma cena de exemplo, sem jogo para testar";
            GameBannerMessage = "Como não há jogo, o build e a injeção não se aplicam. Traduza, revise e confira o texto à vontade: nada aqui escreve em pasta de jogo.";
            return;
        }

        if (QaErrorCount > 0)
        {
            var title = lost is null ? DescribeGame(inGame) : DescribeGame(savedGame.InGame);
            GameBannerState = GameState.Blocked;
            GameBannerTitle = $"{ProjectLabels.Number(QaErrorCount)} {Plural(QaErrorCount, "erro trava", "erros travam")} o teste no jogo";
            GameBannerMessage = $"{state?.QaErrorKinds} {title}.";
            return;
        }

        if (lost is not null)
        {
            GameBannerState = GameState.Warning;
            GameBannerTitle = "O jogo está com a tradução, mas a cópia de segurança não foi encontrada";
            GameBannerMessage = lost;
            return;
        }

        GameBannerState = inGame is null ? GameState.Original : GameState.Translated;
        GameBannerTitle = DescribeGame(inGame);
        GameBannerMessage = inGame is null ? "Nenhuma tradução foi copiada para o jogo." : "A tradução continua no projeto, mesmo se você voltar ao original.";
    }

    /// <summary>
    /// O estado do arquivo só vale se não houve injeção depois dele: uma injeção mais nova (<c>vnf inject</c> de uma versão antiga, um patch do executável)
    /// deixa um backup com data maior que a do estado, e então a faixa descreve o jogo pelo backup. Sem arquivo de estado vale o backup também.
    /// </summary>
    private static GameInGame Effective(GameInGame? saved, DateTimeOffset newestBackupAt, string backupName) =>
        saved is not null && saved.AppliedAt >= newestBackupAt
            ? saved
            : new GameInGame(GameTestKind.Translation, newestBackupAt, backupName, null, null);

    /// <summary>
    /// O estado diz que o jogo tem uma tradução, mas não há backup que o devolva (a pasta sumiu ou o manifesto não lê): o motivo em português simples.
    /// Nulo quando não é o caso, inclusive quando o backup do estado já foi restaurado por fora.
    /// </summary>
    private string? LostBackup(ProjectState state)
    {
        if (savedGame.InGame is not { BackupDirectory: { } name } saved)
        {
            return null;
        }

        var backup = state.Backups.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.Ordinal));
        if (backup is { Restored: true })
        {
            return null;
        }

        var when = DateText(saved.AppliedAt);
        return backup is null
            ? $"A pasta com os originais trocados em {when} não está mais no projeto, então o VNForge não consegue devolver o jogo sozinho. Para voltar ao original, reinstale o jogo ou use a verificação de arquivos da loja onde você o comprou."
            : $"A cópia de segurança de {when} existe, mas o VNForge não consegue ler um dos arquivos dela, então não dá para devolver o jogo sozinho. Para voltar ao original, reinstale o jogo ou use a verificação de arquivos da loja onde você o comprou.";
    }

    /// <summary>
    /// Ao abrir o projeto a barra de status já mostra o que o último teste deixou no jogo, sem esperar a tela Testar ler o disco inteiro.
    /// Confere o estado com o backup mais novo; a leitura completa da tela corrige o resto.
    /// </summary>
    private void ShowSavedGameStatus(ProjectSummary? project)
    {
        try
        {
            GameStatusText = string.Empty;
            if (project is null)
            {
                return;
            }

            var folder = ProjectFolder.Open(project.Root);
            if (GameInjector.ListBackups(folder).FirstOrDefault(b => b.Manifest is not null && !b.Restored) is { Manifest: { } manifest } newest)
            {
                GameStatusText = ShortGameStatus(Effective(GameStateFile.Load(folder).InGame, manifest.CreatedAt, newest.Name));
            }
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            GameStatusText = string.Empty;
        }
    }

    private string DescribeGame(GameInGame? inGame)
    {
        if (inGame is null)
        {
            return "O jogo está como veio";
        }

        var date = DateText(inGame.AppliedAt);
        return inGame switch
        {
            { Kind: GameTestKind.AccentTest } => $"O jogo está com o teste de acentos de {date}",
            { ScriptKey: { } key } => $"O jogo está com só uma cena traduzida em {date}" + (string.Equals(key, openSceneKey, StringComparison.OrdinalIgnoreCase) && HasOpenScene ? $" (“{OpenSceneName}”)" : string.Empty),
            _ => $"O jogo está com a tradução de {date}",
        };
    }

    private static string ShortGameStatus(GameInGame inGame) => inGame.Kind switch
    {
        GameTestKind.AccentTest => $"Jogo com o teste de acentos de {DateText(inGame.AppliedAt)}",
        _ => $"Jogo com a tradução de {DateText(inGame.AppliedAt)}",
    };

    private static string DateText(DateTimeOffset at) => at.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture);

    /// <summary>
    /// Os cinco passos e a legenda. Teste rodando: o passo atual em andamento, mesmo que a página seja relida no meio do teste. Teste que parou: onde parou.
    /// Conferência com erros: o passo "Conferir" travado, a menos que o último teste tenha sido o dos acentos, que não passa pela Conferência da tradução e
    /// mostra o próprio resultado. Senão, o último teste ou os cinco passos cinza.
    /// </summary>
    private void ShowLastTest(GameLastTest? last)
    {
        if (testRunning && testEpoch == epoch)
        {
            ShowRunningStep(currentStep);
            return;
        }

        LastTestTitle = last is null ? string.Empty : LastTestTitleOf(last);
        if (stopped is { } stop)
        {
            SetSteps(StopRows(stop), stop.Caption);
        }
        else if (QaErrorCount > 0 && last is not { Kind: GameTestKind.AccentTest })
        {
            var blocked = $"{ProjectLabels.Number(QaErrorCount)} {Plural(QaErrorCount, "erro trava", "erros travam")}";
            SetSteps(
                StepRows(step => step == GameTestStep.Check ? (TestStepState.Blocked, blocked) : (TestStepState.Pending, string.Empty)),
                "Corrija os erros na Conferência para testar de novo.");
        }
        else if (last is null)
        {
            SetSteps(StepRows(_ => (TestStepState.Pending, string.Empty)), "Ainda não houve teste neste projeto");
        }
        else
        {
            SetSteps(DoneRows(last),QaErrorCount > 0 ? $"{LastTestTitle} · para testar a tradução, corrija os erros da Conferência" : LastTestTitle);
        }
    }

    private void SetSteps(IEnumerable<TestStepRow> rows, string caption)
    {
        LastTestSteps.Clear();
        foreach (var row in rows)
        {
            LastTestSteps.Add(row);
        }

        TestStepsCaption = caption;
    }

    /// <summary>Os cinco passos, sempre na mesma ordem; <paramref name="describe"/> diz o estado e o texto de cada um.</summary>
    private static List<TestStepRow> StepRows(Func<GameTestStep, (TestStepState State, string Text)> describe)
    {
        var steps = Enum.GetValues<GameTestStep>();
        return [.. steps.Select(step =>
        {
            var (state, text) = describe(step);
            return new TestStepRow(step.Label(), text, state, step == steps[^1]);
        })];
    }

    private static string LastTestTitleOf(GameLastTest last)
    {
        var local = last.At.ToLocalTime();
        var total = TimeSpan.FromMilliseconds(last.Steps.Sum(s => s.Milliseconds));
        var kind = last.Kind == GameTestKind.AccentTest ? " · só os acentos" : string.Empty;
        return $"Último teste · {local.ToString("dd/MM", CultureInfo.InvariantCulture)} às {local.ToString("HH:mm", CultureInfo.InvariantCulture)}{kind} · {FormatDuration(total)}";
    }

    private static GameTestStep? StepOf(string? message) =>
        Enum.GetValues<GameTestStep>().Cast<GameTestStep?>().FirstOrDefault(s => s!.Value.Label() == message);

    /// <summary>Os passos antes do atual aparecem prontos (sem tempo), o atual em andamento e os demais cinza.</summary>
    private void ShowRunningStep(GameTestStep step)
    {
        if (!testRunning)
        {
            return;
        }

        currentStep = step;
        SetSteps(
            StepRows(s => (s.CompareTo(step) switch { < 0 => TestStepState.Done, 0 => TestStepState.Running, _ => TestStepState.Pending }, string.Empty)),
            $"Testando agora · {step.Label()}");
    }

    /// <summary>
    /// Onde o teste parou, para os passos não voltarem a mostrar o teste anterior em verde. A Conferência que trava o teste não entra aqui: o passo "Conferir"
    /// já fica travado pela contagem de erros. Falhas antes do primeiro passo (programa do jogo ausente, ajuste do executável recusado) deixam os passos
    /// anteriores cinza, porque nada rodou.
    /// </summary>
    private TestStop? StopOf(GameTestOptions options, Exception? failure)
    {
        var what = options.AccentTest ? "O teste dos acentos" : "O último teste";
        if (failure is null)
        {
            return new TestStop(currentStep, "Cancelado", $"{what} foi cancelado.", PreviousDone: true);
        }

        if (failure is QaBlockedException)
        {
            return null;
        }

        var caption = $"{what} falhou.";
        return failure switch
        {
            GameExecutableNotFoundException => new TestStop(GameTestStep.Open, "Jogo não encontrado", caption, PreviousDone: false),
            GamePatchRefusedException => new TestStop(GameTestStep.Inject, "Ajuste recusado", caption, PreviousDone: false),
            _ => new TestStop(currentStep, FailureText(currentStep), caption, PreviousDone: true),
        };
    }

    private static string FailureText(GameTestStep step) => step switch
    {
        GameTestStep.Check => "Não deu para conferir",
        GameTestStep.Build => "Não deu para preparar",
        GameTestStep.Backup => "Não deu para guardar",
        GameTestStep.Inject => "Não deu para copiar",
        GameTestStep.Open => "O jogo não abriu",
        _ => "Falhou",
    };

    private static List<TestStepRow> StopRows(TestStop stop) =>
        StepRows(step => step.CompareTo(stop.Step) switch
        {
            < 0 when stop.PreviousDone => (TestStepState.Done, string.Empty),
            0 => (TestStepState.Failed, stop.Text),
            _ => (TestStepState.Pending, string.Empty),
        });

    private static List<TestStepRow> DoneRows(GameLastTest last) =>
        StepRows(step => last.Steps.FirstOrDefault(s => s.Step == step) is { } time
            ? (TestStepState.Done, FormatDuration(TimeSpan.FromMilliseconds(time.Milliseconds)))
            : (TestStepState.Pending, string.Empty));

    /// <summary>Cada passo que começa vira a mensagem da tarefa na barra inferior; a tela lê essa mesma mensagem para mostrar o passo em andamento.</summary>
    private sealed class StepProgress(IBackgroundTaskReporter reporter) : IProgress<GameTestStep>
    {
        public void Report(GameTestStep value) => reporter.Report(null, value.Label());
    }
}
