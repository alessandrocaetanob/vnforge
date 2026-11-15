using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Injection;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Uma linha do rodapé do cartão Backups: uma pasta onde há backup do projeto, com quantos ela guarda (<c>Anterior: backup\ do projeto · fora do git · 2 backups</c>).</summary>
public sealed record BackupFolderLine(string Label, string Path, string Rest);

/// <summary>O que o aviso sobre o cartão Backups oferece depois de um backup à mão que terminou ou falhou.</summary>
public enum BackupNoticeAction
{
    None,
    OpenFolder,
    OpenSettings,
}

// "Fazer backup agora" (#693): a linha em Opções, o andamento dela e o aviso de como terminou. O backup em si é do GameInjector, o mesmo do vnf backup.
public sealed partial class BuildViewModel
{
    private Action? cancelBackup;
    private string? backupNoticeFolder;

    /// <summary>Onde os backups do projeto estão agora (a pasta dos Ajustes). Lida a cada vez que um projeto é aberto: trocar a pasta vale sem reabrir a página.</summary>
    public Func<BackupLocations> BackupLocationsSource { get; set; } = () => BackupLocations.Default;

    /// <summary>Abre a pasta do backup que acabou de ser feito no gerenciador de arquivos.</summary>
    public Action<string>? OpenFolder { get; set; }

    /// <summary>A pessoa pediu os Ajustes a partir do aviso de pasta inacessível ou sem espaço; a janela principal abre a aba Backups.</summary>
    public event EventHandler? BackupSettingsRequested;

    private ProjectFolder OpenProject(string root) => ProjectFolder.Open(root).WithBackups(BackupLocationsSource());

    /// <summary>A linha de Opções diz o que entra no backup antes do clique.</summary>
    [ObservableProperty]
    public partial string BackupNowDescription { get; private set; } = BackupNowDefaultText;

    /// <summary>O backup à mão está copiando: a linha troca o botão por Cancelar e mostra o andamento.</summary>
    [ObservableProperty]
    public partial bool IsBackingUp { get; private set; }

    /// <summary>De 0 a 1.</summary>
    [ObservableProperty]
    public partial double BackupProgress { get; private set; }

    [ObservableProperty]
    public partial string BackupProgressText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackupNotice), nameof(HasBackupNoticeAction), nameof(BackupNoticeActionLabel), nameof(BackupNoticeTitle), nameof(BackupNoticeDetail))]
    public partial string? BackupNotice { get; private set; }

    /// <summary>O aviso é de erro (a pasta não serve); do contrário é de sucesso.</summary>
    [ObservableProperty]
    public partial bool BackupNoticeIsError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackupNoticeAction), nameof(BackupNoticeActionLabel))]
    public partial BackupNoticeAction NoticeAction { get; private set; }

    public bool HasBackupNotice => !string.IsNullOrEmpty(BackupNotice);

    public bool HasBackupNoticeAction => NoticeAction != BackupNoticeAction.None;

    public string BackupNoticeActionLabel => NoticeAction == BackupNoticeAction.OpenFolder ? "Abrir a pasta" : "Ajustes";

    /// <summary>A primeira frase do aviso, em negrito.</summary>
    public string BackupNoticeTitle => SplitBackupNotice().Title;

    public string BackupNoticeDetail => SplitBackupNotice().Detail;

    /// <summary>Linhas do rodapé do cartão Backups para as pastas anteriores; vazio com a pasta padrão.</summary>
    public ObservableCollection<BackupFolderLine> PreviousBackupFolders { get; } = [];

    /// <summary>A pasta atual dos backups tem rodapé de duas linhas (atual e anterior) só quando a pessoa escolheu uma pasta nos Ajustes.</summary>
    [ObservableProperty]
    public partial bool HasBackupFolderChoice { get; private set; }

    /// <summary>"Atual:" antes da pasta nova, ou nada com o padrão (o rodapé fica como era).</summary>
    [ObservableProperty]
    public partial string BackupFooterLabel { get; private set; } = string.Empty;

    /// <summary>A pasta atual em mono, depois do "Atual:"; vazio com a pasta padrão, quando o rodapé é o texto de sempre.</summary>
    [ObservableProperty]
    public partial string BackupFooterPath { get; private set; } = string.Empty;

    /// <summary>A pasta escolhida está fora do ar (disco desconectado): o cartão avisa que os backups dela não aparecem.</summary>
    [ObservableProperty]
    public partial string? BackupFolderOffline { get; private set; }

    private const string BackupNowDefaultText = "Guarda os originais do jogo que o build troca e o projeto na pasta dos backups. Não mexe no jogo. Use antes de uma mudança grande.";

    private (string Title, string Detail) SplitBackupNotice()
    {
        var message = BackupNotice ?? string.Empty;
        var end = message.IndexOf(". ", StringComparison.Ordinal);
        return end < 0 ? (message, string.Empty) : (message[..(end + 1)], message[(end + 2)..].TrimStart());
    }

    private bool CanBackUp() => Project is { IsSample: false } && !IsBusy;

    partial void OnIsBackingUpChanged(bool value) => BackUpNowCommand.NotifyCanExecuteChanged();

    private static string BackupGameDescription(ManualBackupGame game) => game switch
    {
        ManualBackupGame.NoGame => "O jogo deste projeto não está ligado. Entra só o projeto.",
        ManualBackupGame.NoBuild => "Ainda não há build, então não sei quais arquivos do jogo ele troca. Entra só o projeto.",
        _ => BackupNowDefaultText,
    };

    [RelayCommand(CanExecute = nameof(CanBackUp))]
    private async Task BackUpNowAsync()
    {
        if (Project is not { } summary)
        {
            return;
        }

        ClearConfirmation();
        ErrorMessage = null;
        ClearBackupNotice();
        var mine = epoch;
        IsBusy = true;
        IsBackingUp = true;
        BackupProgress = 0;
        BackupProgressText = "Preparando o backup";
        using var cancel = new CancellationTokenSource();
        cancelBackup = cancel.Cancel;
        var timer = StartTimer();
        var shown = new Progress<(double Fraction, string Text)>(p =>
        {
            if (mine == epoch)
            {
                BackupProgress = p.Fraction;
                BackupProgressText = p.Text;
            }
        });
        try
        {
            var project = OpenProject(summary.Root);
            var run = await RunAsync(
                "Fazer backup",
                "Fazendo o backup: conferindo o que entra",
                (reporter, ct) =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cancel.Token);
                    using var projectLock = ProjectLock.Acquire(project);
                    var plan = GameInjector.PlanBackup(project);
                    var result = new GameInjector(clock, injectionFiles).BackUpNow(project, plan, new SyncProgress(p => ReportBackup(plan, p, reporter, shown)), linked.Token);
                    return new BackupRun(plan, result);
                });
            if (mine != epoch)
            {
                return;
            }

            timer.Stop();
            if (run.Value is { } done)
            {
                ShowBackupDone(done.Plan, done.Result);
            }
            else if (run.Failure is not null)
            {
                FailBackup(run.Failure);
            }
            else
            {
                ReportStopped("Backup");
            }
        }
        catch (Exception e) when (IsExpected(e))
        {
            if (mine == epoch)
            {
                FailBackup(e);
            }
        }
        finally
        {
            cancelBackup = null;
            IsBackingUp = false;
            IsBusy = false;
            if (mine == epoch)
            {
                LogSummary = timer.Summary;
            }
        }

        await RefreshAsync();
    }

    /// <summary>
    /// "Restaurar este" numa linha de backup à mão: a mesma confirmação da restauração da injeção, com a lista do que volta ao jogo. Só devolve o que aquele backup guarda.
    /// </summary>
    [RelayCommand]
    private void RestoreBackup(BackupRow? row)
    {
        if (row is not { CanRestore: true } || IsBusy || Project is not { IsSample: false } summary)
        {
            return;
        }

        try
        {
            var plan = GameInjector.PlanRestoreFrom(OpenProject(summary.Root), row.Directory);
            ReleasePatch();
            confirmedRestore = plan;
            confirmedInject = null;
            SetConfirmationFiles(plan.Manifest.Files.Select(DescribeRestoredFile));
            ConfirmationText = $"Restaurar os originais na pasta do jogo? Devolve {ProjectLabels.Number(plan.Manifest.Files.Count)} arquivo(s) do backup à mão {row.Name} e apaga os que a injeção criou.";
            Pending = BuildConfirmation.Restore;
        }
        catch (VNForge.Core.VNForgeException e)
        {
            Fail("Não foi possível restaurar o backup", e.Message);
        }
    }

    [RelayCommand]
    private void OpenBackupSettings() => BackupSettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void CancelBackup() => cancelBackup?.Invoke();

    [RelayCommand]
    private void RunBackupNotice()
    {
        if (NoticeAction == BackupNoticeAction.OpenFolder && backupNoticeFolder is not null)
        {
            OpenFolder?.Invoke(backupNoticeFolder);
        }
        else if (NoticeAction == BackupNoticeAction.OpenSettings)
        {
            BackupSettingsRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private static void ReportBackup(ManualBackupPlan plan, ManualBackupProgress progress, IBackgroundTaskReporter reporter, IProgress<(double Fraction, string Text)> shown)
    {
        // O jogo pesa centenas de MB e o projeto poucos: a barra anda pelo tamanho de cada etapa, não pelo número de arquivos.
        var gameShare = plan.TotalBytes == 0 ? 0 : (double)plan.GameBytes / plan.TotalBytes;
        var inStage = progress.Total == 0 ? 0 : (double)progress.Done / progress.Total;
        var fraction = progress.Stage == ManualBackupStage.Game ? inStage * gameShare : gameShare + (inStage * (1 - gameShare));
        var text = progress.Stage == ManualBackupStage.Game
            ? $"Copiando os originais do jogo: {ProjectLabels.Number(progress.Done + 1)} de {ProjectLabels.Number(progress.Total)} {Plural(progress.Total, "arquivo", "arquivos")} ({Path.GetFileName(progress.File)})"
            : $"Copiando o projeto: {ProjectLabels.Number(progress.Done + 1)} de {ProjectLabels.Number(progress.Total)} {Plural(progress.Total, "arquivo", "arquivos")}";
        reporter.Report(fraction, text);
        shown.Report((fraction, text));
    }

    private void ShowBackupDone(ManualBackupPlan plan, ManualBackupResult result)
    {
        var parts = new List<string>();
        if (plan.GameState == ManualBackupGame.Included && result.GameFiles > 0)
        {
            parts.Add($"{ProjectLabels.Number(result.GameFiles)} {Plural(result.GameFiles, "arquivo do jogo", "arquivos do jogo")} ({FormatSize(result.GameBytes)})");
        }

        parts.Add($"o projeto ({FormatSize(result.ProjectBytes)})");
        var message = $"Backup feito. {string.Join(" e ", parts)} em {result.BackupDirectory}.";
        AppendLog($"Backup: {result.BackupDirectory}");
        backupNoticeFolder = result.BackupDirectory;
        BackupNoticeIsError = false;
        NoticeAction = BackupNoticeAction.OpenFolder;
        BackupNotice = message;
    }

    private void FailBackup(Exception failure)
    {
        if (failure is InjectionException { FolderProblem: not BackupFolderProblem.None })
        {
            AppendLog($"Backup falhou: {failure.Message}");
            BackupNoticeIsError = true;
            NoticeAction = BackupNoticeAction.OpenSettings;
            BackupNotice = failure.Message;
            return;
        }

        Fail("Backup falhou", failure.Message);
    }

    private void ClearBackupNotice()
    {
        BackupNotice = null;
        NoticeAction = BackupNoticeAction.None;
        backupNoticeFolder = null;
    }

    /// <summary>O cartão Backups: as linhas, o rodapé da pasta atual e o das anteriores.</summary>
    private void ShowBackupFolders(ProjectState? state)
    {
        PreviousBackupFolders.Clear();
        BackupFolderOffline = null;
        var locations = state?.Locations;
        HasBackupFolderChoice = locations?.IsCustom == true;
        BackupFooterLabel = HasBackupFolderChoice ? "Atual: " : string.Empty;
        if (state is null || locations is null || !locations.IsCustom)
        {
            return;
        }

        foreach (var group in state.Backups.Where(b => !IsInCurrentRoot(b, state)).GroupBy(b => b.Root, StringComparer.OrdinalIgnoreCase))
        {
            var count = group.Count();
            var counted = $" · {ProjectLabels.Number(count)} {Plural(count, "backup", "backups")}";
            if (string.Equals(group.Key, state.DefaultRoot, StringComparison.OrdinalIgnoreCase))
            {
                var ignored = state.BackupIgnored ? " · fora do git" : string.Empty;
                PreviousBackupFolders.Add(new BackupFolderLine("Anterior: ", $"backup{Path.DirectorySeparatorChar}", $" do projeto{ignored}{counted}"));
            }
            else
            {
                PreviousBackupFolders.Add(new BackupFolderLine("Anterior: ", group.Key, counted));
            }
        }

        if (state.Unreachable.Count > 0)
        {
            BackupFolderOffline = $"A pasta {state.Unreachable[0]} não está acessível. Conecte o disco para ver e restaurar os backups dela.";
        }
    }

    private static bool IsInCurrentRoot(BackupInfo backup, ProjectState state) =>
        string.Equals(backup.Root, state.CurrentRoot, StringComparison.OrdinalIgnoreCase);

    /// <summary>O resultado do trabalho de fundo; uma classe, porque <c>TaskRun.Value</c> de uma tupla nunca é nulo e a falha passaria por sucesso.</summary>
    private sealed record BackupRun(ManualBackupPlan Plan, ManualBackupResult Result);

    /// <summary>Entrega o progresso na hora, na thread de quem copia (o <see cref="Progress{T}"/> posta para outra e perderia a ordem em relação ao fim do trabalho).</summary>
    private sealed class SyncProgress(Action<ManualBackupProgress> report) : IProgress<ManualBackupProgress>
    {
        public void Report(ManualBackupProgress value) => report(value);
    }
}
