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

/// <summary>Um passo de "Último teste": o nome e o tempo que levou.</summary>
public sealed record TestStepRow(string Label, string TimeText);

/// <summary>
/// "Testar no jogo": o botão único que confere, guarda a cópia de segurança, injeta e abre o jogo (<see cref="GameTester"/>, o serviço do <c>vnf test</c>),
/// a faixa com o estado do jogo e "Voltar ao jogo original". Os botões de build, injeção e restauração separados seguem na tela, em "Detalhes técnicos".
/// </summary>
public sealed partial class BuildViewModel
{
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

    /// <summary>Os cinco passos do último teste, cada um com o tempo.</summary>
    public ObservableCollection<TestStepRow> LastTestSteps { get; } = [];

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
        OpenSceneName = openSceneKey is null ? string.Empty : string.IsNullOrEmpty(name) ? openSceneKey : name;
        if (openSceneKey is null)
        {
            OnlyOpenScene = false;
        }
    }

    private bool CanTest() => Project is not null && !IsBusy && !HasQaErrors;

    private bool CanTestAccents() => Project is not null && !IsBusy;

    private bool CanReturnToOriginal() => !IsBusy && gameReturnable;

    private void UpdateTestCommands()
    {
        TestCommand.NotifyCanExecuteChanged();
        TestAccentsCommand.NotifyCanExecuteChanged();
        ReturnToOriginalCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanTest))]
    private Task TestAsync() =>
        RunTestAsync(new GameTestOptions(OnlyOpenScene ? openSceneKey : null, AllowWarnings: AllowWarnings));

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
                (reporter, ct) => tester.Run(project, options, new StepProgress(reporter), new TaskProgress(reporter), ct));
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
            }
            else
            {
                ReportStopped("Teste");
            }
        }
        catch (Exception e) when (IsExpected(e))
        {
            if (mine == epoch)
            {
                FailTest(e);
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
                Fail("Não foi possível voltar ao jogo original", run.Failure.Message);
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
                Fail("Não foi possível voltar ao jogo original", e.Message);
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
                foreach (var issue in blocked.Report.OtherIssues.Take(MaxQaIssuesLogged))
                {
                    AppendLog(QaLine(issue));
                }

                Fail(
                    "Teste travado pela Conferência",
                    $"A Conferência travou o teste ({QaBlockedException.DescribeBlocking(blocked.Report)}) e nada foi copiado para o jogo. Corrija em Conferir a tradução, ou ligue “Testar mesmo com avisos” para liberar só os avisos.");
                break;
            case GameExecutableNotFoundException:
                AppendLog(failure.Message);
                ErrorMessage = "O VNForge não encontrou o programa do jogo, e nada foi copiado. Confira a pasta do jogo nos Ajustes.";
                break;
            default:
                Fail("O teste parou", failure.Message);
                break;
        }
    }

    /// <summary>Lê o estado do jogo e do último teste e põe a faixa, a barra de status e a lista de passos no lugar.</summary>
    private void ShowGameState(ProjectState? state)
    {
        savedGame = state?.Saved ?? GameStateFile.Empty;
        // O arquivo pode estar atrasado (um vnf restore à mão, por exemplo): só vale o que ainda tem backup para devolver.
        // Backup sem arquivo de estado é de uma injeção feita pelo vnf inject: o jogo está trocado do mesmo jeito.
        gameReturnable = state?.Restore is not null;
        var inGame = state?.Restore is { } restore ? savedGame.InGame ?? FromBackup(restore) : null;
        var title = DescribeGame(inGame);
        var message = inGame is null ? "Nenhuma tradução foi copiada para o jogo." : "A tradução continua no projeto, mesmo se você voltar ao original.";
        GameStatusText = state is null ? string.Empty : inGame is null ? "Jogo original" : ShortGameStatus(inGame);
        if (QaErrorCount > 0)
        {
            GameBannerState = GameState.Blocked;
            GameBannerTitle = $"{ProjectLabels.Number(QaErrorCount)} {Plural(QaErrorCount, "erro trava", "erros travam")} o teste no jogo";
            GameBannerMessage = $"{state?.QaErrorKinds} {title}.";
        }
        else
        {
            GameBannerState = inGame is null ? GameState.Original : GameState.Translated;
            GameBannerTitle = title;
            GameBannerMessage = message;
        }

        ShowLastTest(state is null ? null : savedGame.LastTest);
        UpdateTestCommands();
    }

    /// <summary>
    /// Ao abrir o projeto a barra de status já mostra o que o último teste deixou no jogo, sem esperar a tela Testar ler o disco inteiro.
    /// O arquivo pode estar atrasado; a leitura completa da tela corrige.
    /// </summary>
    private void ShowSavedGameStatus(ProjectSummary? project)
    {
        try
        {
            var inGame = project is null ? null : GameStateFile.Load(ProjectFolder.Open(project.Root)).InGame;
            GameStatusText = inGame is null ? string.Empty : ShortGameStatus(inGame);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            GameStatusText = string.Empty;
        }
    }

    private static GameInGame FromBackup(RestorePlan restore) =>
        new(GameTestKind.Translation, restore.Manifest.CreatedAt, Path.GetFileName(restore.BackupDirectory), null, null);

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

    private void ShowLastTest(GameLastTest? last)
    {
        LastTestSteps.Clear();
        if (last is null)
        {
            LastTestTitle = string.Empty;
            return;
        }

        var local = last.At.ToLocalTime();
        var total = TimeSpan.FromMilliseconds(last.Steps.Sum(s => s.Milliseconds));
        var kind = last.Kind == GameTestKind.AccentTest ? " · só os acentos" : string.Empty;
        LastTestTitle = $"Último teste · {local.ToString("dd/MM", CultureInfo.InvariantCulture)} às {local.ToString("HH:mm", CultureInfo.InvariantCulture)}{kind} · {FormatDuration(total)}";
        foreach (var step in last.Steps)
        {
            LastTestSteps.Add(new TestStepRow(step.Step.Label(), FormatDuration(TimeSpan.FromMilliseconds(step.Milliseconds))));
        }
    }

    /// <summary>Cada passo que começa vira a mensagem da tarefa na barra inferior e uma linha do registro.</summary>
    private sealed class StepProgress(IBackgroundTaskReporter reporter) : IProgress<GameTestStep>
    {
        public void Report(GameTestStep value) => reporter.Report(null, value.Label());
    }
}
