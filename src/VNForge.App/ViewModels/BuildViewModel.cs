using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Engines.YuRis;

namespace VNForge.App.ViewModels;

/// <summary>O que o botão Confirmar vai fazer; injetar e restaurar escrevem na pasta do jogo, então esperam um segundo clique.</summary>
public enum BuildConfirmation
{
    None,
    Inject,
    Restore,
}

/// <summary>Uma linha da lista "o que muda no jogo": o caminho relativo à pasta do jogo e se o arquivo substitui um original ou é novo.</summary>
public sealed record PlannedChange(string Path, string Kind);

/// <summary>
/// Página Build e injeção: roda o build, injeta o <c>build/</c> na pasta do jogo e restaura os originais com os mesmos serviços do
/// <c>vnf build</c>, <c>vnf inject</c> e <c>vnf restore</c>. Cada operação é uma tarefa da barra inferior; o jogo só é escrito pelo
/// fluxo de injeção, com backup, depois de o usuário confirmar o que o plano mostrou.
/// </summary>
public sealed partial class BuildViewModel : ObservableObject
{
    private const int MaxWarningsLogged = 10;
    private const int MaxQaIssuesLogged = 50;

    private static readonly NumberFormatInfo PtBr = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    private readonly BackgroundTaskService tasks;
    private readonly IReadOnlyList<IEngine> engines;
    private readonly IGameProcessStarter starter;
    private readonly QaEngine? qa;
    private InjectionPlan? injectionPlan;
    private RestorePlan? restorePlan;
    private InjectionPlan? confirmedInject;
    private RestorePlan? confirmedRestore;
    private int refreshVersion;
    private int epoch;

    public BuildViewModel(BackgroundTaskService tasks, IReadOnlyList<IEngine>? engines = null, IGameProcessStarter? starter = null, QaEngine? qa = null)
    {
        this.tasks = tasks;
        this.engines = engines ?? [new YuRisEngine()];
        this.starter = starter ?? new ShellGameProcessStarter();
        this.qa = qa;
        Log.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLog));
        Summary.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSummary));
    }

    /// <summary>O que o build, a injeção e a restauração fizeram, em ordem; a tela mostra como um log.</summary>
    public ObservableCollection<string> Log { get; } = [];

    /// <summary>Os arquivos de <c>build/</c> que a injeção gravaria agora no jogo.</summary>
    public ObservableCollection<PlannedChange> Changes { get; } = [];

    /// <summary>Resumo do último build deste projeto, uma linha por fato.</summary>
    public ObservableCollection<string> Summary { get; } = [];

    /// <summary>Termina quando a última leitura pedida por <see cref="ReloadAsync"/> acaba.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial ProjectSummary? Project { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    public partial string GameFolder { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string PlanMessage { get; private set; } = string.Empty;

    /// <summary>Pasta do backup mais recente; sem backup, a pasta onde o próximo será criado.</summary>
    [ObservableProperty]
    public partial string BackupLocation { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string BackupMessage { get; private set; } = string.Empty;

    /// <summary>Reconstrói só os scripts cuja tradução mudou desde o build anterior (<c>--only-changed</c>).</summary>
    [ObservableProperty]
    public partial bool OnlyChanged { get; set; } = true;

    /// <summary>Deixa o build seguir com avisos de QA; erros bloqueiam sempre (<c>--allow-warnings</c>).</summary>
    [ObservableProperty]
    public partial bool AllowWarnings { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirming))]
    public partial BuildConfirmation Pending { get; private set; }

    [ObservableProperty]
    public partial string ConfirmationText { get; private set; } = string.Empty;

    public bool IsConfirming => Pending != BuildConfirmation.None;

    public bool HasSummary => Summary.Count > 0;

    public bool HasLog => Log.Count > 0;

    partial void OnProjectChanged(ProjectSummary? value)
    {
        epoch++;
        Log.Clear();
        Summary.Clear();
        ErrorMessage = null;
        ClearConfirmation();
        refreshVersion++;
        Apply(null);
    }

    partial void OnIsBusyChanged(bool value) => UpdateCommands();

    partial void OnPendingChanged(BuildConfirmation value) => UpdateCommands();

    private void UpdateCommands()
    {
        BuildCommand.NotifyCanExecuteChanged();
        InjectCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
        LaunchCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    private bool CanBuild() => Project is not null && !IsBusy;

    private bool CanInject() => !IsBusy && injectionPlan is { Files.Count: > 0 };

    private bool CanRestore() => !IsBusy && restorePlan is not null;

    private bool CanLaunch() => Project is not null && !IsBusy;

    private bool CanConfirm() => IsConfirming && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        if (Project is not { } summary)
        {
            return;
        }

        ClearConfirmation();
        ErrorMessage = null;
        var mine = epoch;
        IsBusy = true;
        try
        {
            var project = ProjectFolder.Open(summary.Root);
            var options = new BuildOptions(OnlyChanged);
            var allowWarnings = AllowWarnings;
            AppendLog("Iniciando o build: conferindo a tradução e reconstruindo os scripts.");
            var run = await RunAsync("Build de " + summary.Name, "Reconstruindo os scripts com as traduções", ct =>
                ProjectBuilder.Build(project, engines, qa: qa, allowWarnings: allowWarnings, options: options, cancellationToken: ct));
            if (mine != epoch)
            {
                return;
            }

            if (run.Value is { } result)
            {
                DescribeBuild(project, result);
            }
            else if (run.Failure is QaBlockedException blocked)
            {
                DescribeQaBlocked(blocked);
            }
            else if (run.Failure is not null)
            {
                Fail("Build falhou", run.Failure.Message);
            }
            else
            {
                ReportStopped("Build");
            }
        }
        catch (Exception e) when (IsExpected(e))
        {
            if (mine == epoch)
            {
                Fail("Build falhou", e.Message);
            }
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanInject))]
    private void Inject()
    {
        if (injectionPlan is not { } plan)
        {
            return;
        }

        var replaced = plan.Files.Count(f => f.ReplacesExisting);
        confirmedInject = plan;
        confirmedRestore = null;
        ConfirmationText = $"Gravar {ProjectLabels.Number(plan.Files.Count)} arquivo(s) na pasta do jogo? Os {ProjectLabels.Number(replaced)} original(is) que serão trocados vão primeiro para o backup, em {plan.BackupRoot}.";
        Pending = BuildConfirmation.Inject;
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private void Restore()
    {
        if (restorePlan is not { } plan)
        {
            return;
        }

        confirmedRestore = plan;
        confirmedInject = null;
        ConfirmationText = $"Restaurar os originais na pasta do jogo? Devolve {ProjectLabels.Number(plan.Manifest.Files.Count)} arquivo(s) dos backups (o mais recente é {plan.BackupDirectory}) e apaga os que a injeção criou.";
        Pending = BuildConfirmation.Restore;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        var action = Pending;
        var inject = confirmedInject;
        var restore = confirmedRestore;
        ClearConfirmation();
        ErrorMessage = null;
        var mine = epoch;
        IsBusy = true;
        try
        {
            switch (action)
            {
                case BuildConfirmation.Inject when inject is not null:
                    await InjectAsync(inject, mine);
                    break;
                case BuildConfirmation.Restore when restore is not null:
                    await RestoreAsync(restore, mine);
                    break;
            }
        }
        catch (Exception e) when (IsExpected(e))
        {
            if (mine == epoch)
            {
                Fail("Operação falhou", e.Message);
            }
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private void CancelConfirmation()
    {
        ClearConfirmation();
    }

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchAsync()
    {
        if (Project is not { } summary)
        {
            return;
        }

        ErrorMessage = null;
        var mine = epoch;
        IsBusy = true;
        try
        {
            var project = ProjectFolder.Open(summary.Root);
            var executable = await Task.Run(() => new GameLauncher(starter).Launch(project));
            if (mine == epoch)
            {
                AppendLog($"Jogo aberto: {executable}");
            }
        }
        catch (Exception e) when (IsExpected(e))
        {
            if (mine == epoch)
            {
                Fail("Não foi possível abrir o jogo", e.Message);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InjectAsync(InjectionPlan plan, int mine)
    {
        AppendLog($"Injetando {ProjectLabels.Number(plan.Files.Count)} arquivo(s) em {plan.GameDirectory}");
        var run = await RunAsync("Injetar no jogo", "Fazendo o backup e gravando os arquivos", _ =>
        {
            EnsureBuildMatchesPlan(plan);
            return new GameInjector().Inject(plan);
        });
        if (mine != epoch)
        {
            return;
        }

        if (run.Value is { } result)
        {
            if (result.BackupDirectory is not null)
            {
                AppendLog($"Backup: {result.BackupDirectory}");
            }

            AppendLog($"Injetado: {ProjectLabels.Number(result.Written.Count)} arquivo(s) em {result.GameDirectory} (hashes conferidos)");
        }
        else if (run.Failure is not null)
        {
            Fail("Injeção falhou", run.Failure.Message);
        }
        else
        {
            ReportStopped("Injeção");
        }
    }

    private async Task RestoreAsync(RestorePlan plan, int mine)
    {
        var root = Project!.Root;
        AppendLog($"Restaurando os originais do backup {plan.BackupDirectory}");
        var run = await RunAsync("Restaurar originais", "Devolvendo os arquivos do backup", _ =>
        {
            EnsureBackupsMatchPlan(root, plan);
            return new GameInjector().Restore(plan);
        });
        if (mine != epoch)
        {
            return;
        }

        if (run.Value is { } result)
        {
            AppendLog($"Restaurado: {ProjectLabels.Number(result.Restored.Count)} arquivo(s); removido: {ProjectLabels.Number(result.Removed.Count)} arquivo(s) criado(s) pela injeção.");
            foreach (var kept in result.Kept)
            {
                AppendLog($"Mantido: {kept} (criado pela injeção, mas alterado depois; apague à mão se quiser)");
            }
        }
        else if (run.Failure is not null)
        {
            Fail("Restauração falhou", run.Failure.Message);
        }
        else
        {
            ReportStopped("Restauração");
        }
    }

    /// <summary>Recusa a injeção se um arquivo de <c>build/</c> mudou depois do plano que o usuário confirmou: o jogo não recebe conteúdo que a tela não mostrou.</summary>
    private static void EnsureBuildMatchesPlan(InjectionPlan plan)
    {
        foreach (var file in plan.Files)
        {
            using var stream = new FileStream(file.BuildPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream)) != file.BuildSha256)
            {
                throw new InjectionException($"O arquivo '{file.RelativePath}' de build/ mudou depois da conferência. Nada foi gravado no jogo; confira o que muda e injete de novo.");
            }
        }
    }

    /// <summary>Recusa a restauração se os backups mudaram depois do plano confirmado (surgiu um mais novo, por exemplo): restaurar o antigo devolveria o jogo a um estado velho.</summary>
    private static void EnsureBackupsMatchPlan(string root, RestorePlan plan)
    {
        var current = GameInjector.PlanRestore(ProjectFolder.Open(root));
        if (!SameRestore(current, plan))
        {
            throw new InjectionException($"Os backups mudaram depois da conferência (há um backup mais novo, {current.BackupDirectory}). Nada foi restaurado; confira o backup e restaure de novo.");
        }
    }

    private static bool SameInjection(InjectionPlan a, InjectionPlan b) =>
        a.GameDirectory == b.GameDirectory
        && a.Files.Select(f => (f.RelativePath, f.BuildSha256, f.OriginalSha256, f.TargetSha256))
            .SequenceEqual(b.Files.Select(f => (f.RelativePath, f.BuildSha256, f.OriginalSha256, f.TargetSha256)));

    private static bool SameRestore(RestorePlan a, RestorePlan b) =>
        a.GameDirectory == b.GameDirectory
        && a.BackupDirectory == b.BackupDirectory
        && a.Manifest.Files.SequenceEqual(b.Manifest.Files)
        && a.BackupDirectories.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(b.BackupDirectories.OrderBy(p => p.Key, StringComparer.Ordinal));

    /// <summary>
    /// Roda o trabalho como tarefa da barra inferior e devolve o resultado ou a falha esperada, capturados dentro do trabalho:
    /// o estado da tarefa só muda quando o <c>Dispatcher</c> processa o <c>post</c>, depois de <see cref="BackgroundTaskItem.Completion"/>.
    /// </summary>
    private async Task<TaskRun<T>> RunAsync<T>(string title, string message, Func<CancellationToken, T> work)
    {
        T? value = default;
        Exception? failure = null;
        var item = tasks.Start(title, (reporter, ct) =>
        {
            reporter.Report(null, message);
            try
            {
                value = work(ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failure = e;
            }

            return Task.CompletedTask;
        });
        var mine = epoch;
        void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (mine == epoch && e.PropertyName == nameof(BackgroundTaskItem.Message) && item.IsRunning && !string.IsNullOrEmpty(item.Message))
            {
                AppendLog(item.Message);
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
        }

        return new TaskRun<T>(value, failure);
    }

    private void DescribeBuild(ProjectFolder project, BuildResult result)
    {
        foreach (var archive in result.Archives)
        {
            var built = Path.Combine(result.BuildDirectory, archive.GamePath.Replace('/', Path.DirectorySeparatorChar));
            var detail = archive.ReplacedFiles == 0
                ? "sem alterações, idêntico ao original"
                : $"{ProjectLabels.Number(archive.ReplacedFiles)} {Plural(archive.ReplacedFiles, "script alterado", "scripts alterados")}";
            AppendLog($"Gravado: {built} ({detail})");
        }

        var summary = new List<string> { BuildSummaryLine(result) };
        foreach (var archive in result.Archives)
        {
            summary.Add($"{archive.GamePath}: {SizeText(project, result, archive)}");
        }

        if (result.Encoding.Replaced.Count > 0)
        {
            summary.Add($"Codificação '{result.Encoding.Strategy}': {ProjectLabels.Number(result.Encoding.Replaced.Count)} {Plural(result.Encoding.Replaced.Count, "caractere trocado", "caracteres trocados")}.");
        }

        if (result.Qa.Issues.Count > 0)
        {
            summary.Add($"QA: {QaBlockedException.Describe(result.Qa)}, liberados por 'Permitir avisos do QA'.");
        }

        if (result.Qa.Silenced.Count > 0)
        {
            summary.Add($"QA: {ProjectLabels.Number(result.Qa.Silenced.Count)} {Plural(result.Qa.Silenced.Count, "aviso marcado como ok", "avisos marcados como ok")}.");
        }

        foreach (var issue in result.Qa.Issues)
        {
            AppendLog(QaLine(issue));
        }

        foreach (var warning in result.Warnings.Take(MaxWarningsLogged))
        {
            AppendLog($"Aviso: {warning}");
        }

        if (result.Warnings.Count > MaxWarningsLogged)
        {
            AppendLog($"... e mais {ProjectLabels.Number(result.Warnings.Count - MaxWarningsLogged)} aviso(s).");
        }

        AppendLog(summary[0]);
        Summary.Clear();
        foreach (var line in summary)
        {
            Summary.Add(line);
        }
    }

    private void DescribeQaBlocked(QaBlockedException blocked)
    {
        foreach (var issue in blocked.Report.Issues.Take(MaxQaIssuesLogged))
        {
            AppendLog(QaLine(issue));
        }

        if (blocked.Report.Issues.Count > MaxQaIssuesLogged)
        {
            AppendLog($"... e mais {ProjectLabels.Number(blocked.Report.Issues.Count - MaxQaIssuesLogged)} ocorrência(s).");
        }

        Fail("Build bloqueado pelo QA", $"O QA bloqueou o build ({QaBlockedException.Describe(blocked.Report)}) e nada foi gravado. Corrija a tradução, marque o aviso como ok na linha ou ligue 'Permitir avisos do QA' para liberar só os avisos.");
    }

    private static string QaLine(QaIssue issue) =>
        $"{QaBlockedException.Label(issue.Severity)} {issue.UnitId}  [{issue.RuleId}] {issue.Message}";

    private static string BuildSummaryLine(BuildResult result)
    {
        var scripts = result.RebuiltScripts.Count;
        var packages = result.Archives.Count;
        if (scripts == 0 && result.ReusedScripts > 0)
        {
            return $"Nada mudou desde o build anterior: {ProjectLabels.Number(result.ReusedScripts)} {Plural(result.ReusedScripts, "script reaproveitado", "scripts reaproveitados")}, nenhum reconstruído.";
        }

        var reused = result.ReusedScripts > 0 ? $", {ProjectLabels.Number(result.ReusedScripts)} {Plural(result.ReusedScripts, "reaproveitado", "reaproveitados")}" : string.Empty;
        return $"Reconstruídos {ProjectLabels.Number(scripts)} {Plural(scripts, "script", "scripts")} de {ProjectLabels.Number(result.ScriptCount)} ({ProjectLabels.Number(result.TotalLines)} {Plural(result.TotalLines, "linha trocada", "linhas trocadas")}{reused}); "
            + $"{ProjectLabels.Number(packages)} {Plural(packages, "pacote gravado", "pacotes gravados")} em build/.";
    }

    private static string SizeText(ProjectFolder project, BuildResult result, RepackedArchive archive)
    {
        var relative = archive.GamePath.Replace('/', Path.DirectorySeparatorChar);
        var built = new FileInfo(Path.Combine(project.BuildDirectory, relative));
        var original = new FileInfo(Path.Combine(result.GameDirectory, relative));
        if (!built.Exists)
        {
            return "não gravado";
        }

        return original.Exists
            ? $"{FormatSize(built.Length)} (original {FormatSize(original.Length)})"
            : FormatSize(built.Length);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => string.Create(PtBr, $"{bytes} B"),
        < 1024 * 1024 => string.Create(PtBr, $"{bytes / 1024.0:N1} KB"),
        _ => string.Create(PtBr, $"{bytes / 1024.0 / 1024.0:N1} MB"),
    };

    private static string Plural(int count, string singular, string plural) => count == 1 ? singular : plural;

    private void ReportStopped(string what)
    {
        AppendLog($"{what} cancelado.");
    }

    private void Fail(string title, string message)
    {
        ErrorMessage = message;
        AppendLog($"{title}: {message}");
    }

    private void AppendLog(string line)
    {
        if (Log.Count > 0 && Log[^1] == line)
        {
            return;
        }

        Log.Add(line);
    }

    private void ClearConfirmation()
    {
        Pending = BuildConfirmation.None;
        ConfirmationText = string.Empty;
        confirmedInject = null;
        confirmedRestore = null;
    }

    /// <summary>Chamado ao fechar a janela: enquanto uma operação escreve no jogo, fechar a deixaria pela metade. Devolve <c>false</c> e avisa na página.</summary>
    public bool TryCloseSafely()
    {
        if (!IsBusy)
        {
            return true;
        }

        Fail("Aguarde", "Uma operação está escrevendo no jogo. Feche o VNForge quando ela terminar, para não deixar o jogo pela metade.");
        return false;
    }

    /// <summary>Lê de novo o build, o plano de injeção e o backup; a janela chama ao abrir a página, porque o jogo e o <c>build/</c> podem ter mudado fora do app. Trocar de projeto só limpa o estado; não lê o disco.</summary>
    public Task ReloadAsync() => Loading = RefreshAsync();

    private async Task RefreshAsync()
    {
        var version = ++refreshVersion;
        var root = Project?.Root;
        ProjectState? state = null;
        if (root is not null)
        {
            try
            {
                state = await Task.Run(() => ReadState(root));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                state = ProjectState.Unreadable(e.Message);
            }
        }

        if (version != refreshVersion)
        {
            return;
        }

        Apply(state);
    }

    private void Apply(ProjectState? state)
    {
        injectionPlan = state?.Plan;
        restorePlan = state?.Restore;
        DropStaleConfirmation();
        GameFolder = state?.GameFolder ?? string.Empty;
        PlanMessage = state?.PlanMessage ?? string.Empty;
        BackupLocation = state?.BackupLocation ?? string.Empty;
        BackupMessage = state?.BackupMessage ?? string.Empty;
        Changes.Clear();
        foreach (var file in state?.Plan?.Files ?? [])
        {
            Changes.Add(new PlannedChange(file.RelativePath, file.ReplacesExisting ? "Substitui o original" : "Arquivo novo"));
        }

        UpdateCommands();
    }

    /// <summary>O plano lido de novo substituiu o que o usuário viu: a confirmação pendente é de outro plano e cai, para ele conferir de novo.</summary>
    private void DropStaleConfirmation()
    {
        var stale = Pending switch
        {
            BuildConfirmation.Inject => injectionPlan is null || confirmedInject is null || !SameInjection(injectionPlan, confirmedInject),
            BuildConfirmation.Restore => restorePlan is null || confirmedRestore is null || !SameRestore(restorePlan, confirmedRestore),
            _ => false,
        };
        if (stale)
        {
            ClearConfirmation();
            AppendLog("A confirmação foi cancelada: o que seria gravado mudou desde que você pediu. Confira a lista e peça de novo.");
        }
    }

    private static ProjectState ReadState(string root)
    {
        var project = ProjectFolder.Open(root);
        var gameFolder = Path.GetFullPath(project.LoadConfig().Game.Path, project.Root);
        var hasBuild = Directory.Exists(project.BuildDirectory)
            && Directory.EnumerateFiles(project.BuildDirectory, "*", SearchOption.AllDirectories).Any();

        InjectionPlan? plan = null;
        string planMessage;
        if (!hasBuild)
        {
            planMessage = "Ainda não há build. Faça o build para ver o que muda no jogo.";
        }
        else
        {
            try
            {
                plan = GameInjector.Plan(project);
                planMessage = DescribePlan(plan);
            }
            catch (VNForgeException e)
            {
                planMessage = e.Message;
            }
        }

        RestorePlan? restore = null;
        string backupMessage;
        var backupLocation = project.BackupDirectory;
        try
        {
            restore = GameInjector.PlanRestore(project);
            backupLocation = restore.BackupDirectory;
            backupMessage = $"Backup de {restore.Manifest.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm} com {ProjectLabels.Number(restore.Manifest.Files.Count)} arquivo(s). Restaurar devolve os originais ao jogo.";
        }
        catch (VNForgeException e)
        {
            var hasBackups = Directory.Exists(project.BackupDirectory) && Directory.EnumerateDirectories(project.BackupDirectory).Any();
            backupMessage = hasBackups ? e.Message : "Nenhum backup ainda. O primeiro Injetar cria um nesta pasta.";
        }

        return new ProjectState(gameFolder, plan, planMessage, restore, backupLocation, backupMessage);
    }

    private static string DescribePlan(InjectionPlan plan)
    {
        if (plan.Files.Count == 0)
        {
            return $"Os {ProjectLabels.Number(plan.UnchangedCount)} arquivo(s) de build/ já estão iguais no jogo. Nada a injetar.";
        }

        var unchanged = plan.UnchangedCount > 0 ? $"; {ProjectLabels.Number(plan.UnchangedCount)} já {(plan.UnchangedCount == 1 ? "está igual" : "estão iguais")} no jogo" : string.Empty;
        return $"A injeção grava {ProjectLabels.Number(plan.Files.Count)} arquivo(s) na pasta do jogo{unchanged}.";
    }

    private static bool IsExpected(Exception e) =>
        e is VNForgeException or IOException or UnauthorizedAccessException or OperationCanceledException;

    private sealed record ProjectState(string GameFolder, InjectionPlan? Plan, string PlanMessage, RestorePlan? Restore, string BackupLocation, string BackupMessage)
    {
        public static ProjectState Unreadable(string message) => new(string.Empty, null, message, null, string.Empty, string.Empty);
    }

    private readonly record struct TaskRun<T>(T? Value, Exception? Failure);
}
