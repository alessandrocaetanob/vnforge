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

/// <summary>Uma linha do log: o horário (<c>HH:mm:ss</c>, em mono na tela) e o que aconteceu.</summary>
public sealed record BuildLogLine(string Time, string Message);

/// <summary>Uma linha do cartão Resultado: o nome do fato (<c>Scripts</c>, <c>ysbin.ypf</c>, <c>Acentuação</c>) e o valor.</summary>
public sealed record ResultFact(string Label, string Value);

/// <summary>Uma linha da lista de backups: a pasta, quanto ocupa e o que guarda.</summary>
public sealed record BackupRow(string Name, string SizeText, string Note);

/// <summary>A severidade do InfoBar do cartão Resultado.</summary>
public enum BuildResultTone
{
    Info,
    Success,
    Warning,
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
    private const int MaxScriptsLogged = 5;

    private static readonly NumberFormatInfo PtBr = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    private readonly BackgroundTaskService tasks;
    private readonly IReadOnlyList<IEngine> engines;
    private readonly IGameProcessStarter starter;
    private readonly QaEngine? qa;
    private readonly TimeProvider clock;
    private InjectionPlan? injectionPlan;
    private RestorePlan? restorePlan;
    private InjectionPlan? confirmedInject;
    private RestorePlan? confirmedRestore;
    private int refreshVersion;
    private int epoch;
    private string? restoredFrom;

    public BuildViewModel(BackgroundTaskService tasks, IReadOnlyList<IEngine>? engines = null, IGameProcessStarter? starter = null, QaEngine? qa = null, TimeProvider? clock = null)
    {
        this.tasks = tasks;
        this.engines = engines ?? [new YuRisEngine()];
        this.starter = starter ?? new ShellGameProcessStarter();
        this.qa = qa;
        this.clock = clock ?? TimeProvider.System;
        Log.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLog));
        Facts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasFacts));
        Backups.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasBackups));
    }

    /// <summary>O que o build, a injeção e a restauração fizeram, em ordem; a tela mostra como um log.</summary>
    public ObservableCollection<BuildLogLine> Log { get; } = [];

    /// <summary>Os arquivos de <c>build/</c> que a injeção gravaria agora no jogo.</summary>
    public ObservableCollection<PlannedChange> Changes { get; } = [];

    /// <summary>Os números do último build desta sessão, um fato por linha do cartão Resultado.</summary>
    public ObservableCollection<ResultFact> Facts { get; } = [];

    /// <summary>Os backups da pasta <c>backup/</c> do projeto, do mais novo para o mais antigo.</summary>
    public ObservableCollection<BackupRow> Backups { get; } = [];

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

    /// <summary>O executável do jogo (<c>game.exe</c> dentro da pasta do jogo), que o cabeçalho mostra em mono.</summary>
    [ObservableProperty]
    public partial string GameExecutable { get; private set; } = string.Empty;

    /// <summary>Erros do QA agora: com algum, o <c>vnf build</c> recusa, então a faixa de aviso acende e "Novo build" trava.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQaErrors))]
    public partial int QaErrorCount { get; private set; }

    /// <summary>Avisos do QA abertos (sem as linhas sem tradução, que nunca bloqueiam).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AllowWarningsDescription))]
    public partial int QaWarningCount { get; private set; }

    [ObservableProperty]
    public partial string QaBannerTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string QaBannerDetail { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial BuildResultTone ResultTone { get; private set; }

    [ObservableProperty]
    public partial string ResultTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ResultText { get; private set; } = string.Empty;

    /// <summary>Quando e quanto durou a última operação: <c>24/10/2026 00:41 · 6,8 s</c>.</summary>
    [ObservableProperty]
    public partial string LogSummary { get; private set; } = string.Empty;

    /// <summary>Há backups na pasta mas a restauração não pôde ser planejada: <see cref="BackupMessage"/> diz por quê.</summary>
    [ObservableProperty]
    public partial bool HasRestoreProblem { get; private set; }

    /// <summary>Rodapé da lista de backups: a pasta, a garantia de que fica fora do git e quantos há.</summary>
    [ObservableProperty]
    public partial string BackupFooter { get; private set; } = string.Empty;

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

    /// <summary>Deixa o build seguir com avisos de QA; erros bloqueiam sempre e linhas sem tradução nunca bloqueiam (<c>--allow-warnings</c>).</summary>
    [ObservableProperty]
    public partial bool AllowWarnings { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirming))]
    public partial BuildConfirmation Pending { get; private set; }

    [ObservableProperty]
    public partial string ConfirmationText { get; private set; } = string.Empty;

    public bool IsConfirming => Pending != BuildConfirmation.None;

    public bool HasFacts => Facts.Count > 0;

    public bool HasLog => Log.Count > 0;

    public bool HasBackups => Backups.Count > 0;

    public bool HasQaErrors => QaErrorCount > 0;

    /// <summary>A chave do ícone do InfoBar do Resultado, para o <c>IconKeyConverter</c>.</summary>
    public string ResultIconKey => ResultTone switch
    {
        BuildResultTone.Success => "Icon.Check",
        BuildResultTone.Warning => "Icon.Warning",
        _ => "Icon.Info",
    };

    public bool IsResultSuccess => ResultTone == BuildResultTone.Success;

    public bool IsResultWarning => ResultTone == BuildResultTone.Warning;

    /// <summary>A linha de apoio de "Permitir avisos do QA": erros sempre bloqueiam, e quantos avisos há hoje.</summary>
    public string AllowWarningsDescription => QaWarningCount switch
    {
        0 => "Erros sempre bloqueiam. Não há avisos abertos.",
        1 => "Erros sempre bloqueiam. Hoje há 1 aviso aberto.",
        var n => $"Erros sempre bloqueiam. Hoje há {ProjectLabels.Number(n)} avisos abertos.",
    };

    /// <summary>"Ver erros" da faixa de aviso: a janela troca para a Conferência.</summary>
    public event EventHandler? ErrorsRequested;

    [RelayCommand]
    private void ShowErrors() => ErrorsRequested?.Invoke(this, EventArgs.Empty);

    partial void OnResultToneChanged(BuildResultTone value)
    {
        OnPropertyChanged(nameof(ResultIconKey));
        OnPropertyChanged(nameof(IsResultSuccess));
        OnPropertyChanged(nameof(IsResultWarning));
    }

    partial void OnProjectChanged(ProjectSummary? value)
    {
        epoch++;
        Log.Clear();
        Facts.Clear();
        LogSummary = string.Empty;
        restoredFrom = null;
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

    private bool CanBuild() => Project is not null && !IsBusy && !HasQaErrors;

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
        restoredFrom = null;
        var mine = epoch;
        IsBusy = true;
        var timer = StartTimer();
        try
        {
            var project = ProjectFolder.Open(summary.Root);
            var options = new BuildOptions(OnlyChanged);
            var allowWarnings = AllowWarnings;
            AppendLog("Iniciando o build: conferindo a tradução e reconstruindo os scripts.");
            var run = await RunAsync("Build de " + summary.Name, "Reconstruindo os scripts com as traduções", (reporter, ct) =>
                ProjectBuilder.Build(project, engines, qa: qa, allowWarnings: allowWarnings, options: options, progress: new TaskProgress(reporter), cancellationToken: ct));
            if (mine != epoch)
            {
                return;
            }

            var elapsed = timer.Stop();
            if (run.Value is { } result)
            {
                DescribeBuild(project, result, elapsed);
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
            if (mine == epoch)
            {
                LogSummary = timer.Summary;
            }
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
        var timer = StartTimer();
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
            if (mine == epoch)
            {
                LogSummary = timer.Summary;
            }
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
        restoredFrom = null;
        var run = await RunAsync("Injetar no jogo", "Fazendo o backup e gravando os arquivos", (_, _) =>
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
        var run = await RunAsync("Restaurar originais", "Devolvendo os arquivos do backup", (_, _) =>
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
            restoredFrom = result.BackupDirectory;
            AppendLog($"Restaurado: {ProjectLabels.Number(result.Restored.Count)} arquivo(s); removido: {ProjectLabels.Number(result.Removed.Count)} arquivo(s) criado(s) pela injeção.");
            foreach (var kept in result.Kept)
            {
                AppendLog($"Mantido: {kept} (criado pela injeção, mas alterado depois; apague à mão se quiser)");
            }

            foreach (var warning in result.Warnings)
            {
                AppendLog($"Aviso: {warning}");
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
    private async Task<TaskRun<T>> RunAsync<T>(string title, string message, Func<IBackgroundTaskReporter, CancellationToken, T> work)
    {
        T? value = default;
        Exception? failure = null;
        var item = tasks.Start(title, (reporter, ct) =>
        {
            reporter.Report(null, message);
            try
            {
                value = work(reporter, ct);
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
            if (mine == epoch && e.PropertyName == nameof(BackgroundTaskItem.Message) && item.IsRunning && item.Progress is null && !string.IsNullOrEmpty(item.Message))
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

    private void DescribeBuild(ProjectFolder project, BuildResult result, TimeSpan elapsed)
    {
        foreach (var archive in result.Archives)
        {
            var built = Path.Combine("build", archive.GamePath.Replace('/', Path.DirectorySeparatorChar));
            var detail = archive.ReplacedFiles == 0
                ? "sem alterações, idêntico ao original"
                : $"{ProjectLabels.Number(archive.ReplacedFiles)} {Plural(archive.ReplacedFiles, "script alterado", "scripts alterados")}";
            AppendLog($"Gravado: {built} ({detail})");
        }

        foreach (var script in result.RebuiltScripts.Take(MaxScriptsLogged))
        {
            AppendLog($"{script.Name} reconstruído · {ProjectLabels.Number(script.Lines)} {Plural(script.Lines, "texto", "textos")}");
        }

        if (result.RebuiltScripts.Count > MaxScriptsLogged)
        {
            var more = result.RebuiltScripts.Count - MaxScriptsLogged;
            AppendLog($"… e mais {ProjectLabels.Number(more)} {Plural(more, "script reconstruído", "scripts reconstruídos")}");
        }

        if (result.Encoding.Replaced.Count > 0)
        {
            AppendLog($"Acentuação {EncodingName(result.Encoding.Strategy)}: {ProjectLabels.Number(result.Encoding.Replaced.Count)} {Plural(result.Encoding.Replaced.Count, "caractere trocado", "caracteres trocados")}");
        }

        foreach (var issue in result.Qa.OtherIssues)
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

        AppendLog(BuildSummaryLine(result));
        AppendLog($"Build concluído em {FormatDuration(elapsed)} → build{Path.DirectorySeparatorChar}");
        ShowFacts(project, result);
    }

    /// <summary>Preenche o cartão Resultado com os números do build: scripts, o tamanho de cada pacote, a acentuação e o que o QA deixou passar.</summary>
    private void ShowFacts(ProjectFolder project, BuildResult result)
    {
        var facts = new List<ResultFact> { new("Scripts", ScriptsFact(result)) };
        foreach (var archive in result.Archives)
        {
            facts.Add(new ResultFact(archive.GamePath, SizeText(project, result, archive)));
        }

        var replaced = result.Encoding.Replaced.Count;
        facts.Add(new ResultFact(
            "Acentuação",
            replaced == 0
                ? $"{EncodingName(result.Encoding.Strategy)} · nenhum caractere substituído"
                : $"{EncodingName(result.Encoding.Strategy)} · {ProjectLabels.Number(replaced)} {Plural(replaced, "caractere substituído", "caracteres substituídos")}"));

        if (result.Qa.OtherIssues.Count > 0)
        {
            facts.Add(new ResultFact("QA", $"{QaBlockedException.DescribeBlocking(result.Qa)}, liberados por 'Permitir avisos do QA'."));
        }

        if (result.Qa.UntranslatedCount > 0)
        {
            facts.Add(new ResultFact("QA", WithoutQaPrefix(QaBlockedException.DescribeUntranslated(result.Qa))));
        }

        if (result.Qa.Silenced.Count > 0)
        {
            facts.Add(new ResultFact("QA", $"{ProjectLabels.Number(result.Qa.Silenced.Count)} {Plural(result.Qa.Silenced.Count, "aviso marcado como ok", "avisos marcados como ok")}."));
        }

        Facts.Clear();
        foreach (var fact in facts)
        {
            Facts.Add(fact);
        }
    }

    private static string WithoutQaPrefix(string text) => text.StartsWith("QA: ", StringComparison.Ordinal) ? text[4..] : text;

    private static string ScriptsFact(BuildResult result)
    {
        var rebuilt = result.RebuiltScripts.Count;
        var total = ProjectLabels.Number(result.ScriptCount);
        var reused = result.ReusedScripts > 0 ? $" · {ProjectLabels.Number(result.ReusedScripts)} {Plural(result.ReusedScripts, "reaproveitado", "reaproveitados")}" : string.Empty;
        return rebuilt == 0
            ? $"nenhum reconstruído de {total}{reused}"
            : $"{ProjectLabels.Number(rebuilt)} {Plural(rebuilt, "reconstruído", "reconstruídos")} de {total}{reused}";
    }

    /// <summary>A estratégia como o artboard a escreve: <c>cp1252</c> vira <c>CP1252</c>.</summary>
    private static string EncodingName(string strategy) => strategy.ToUpperInvariant();

    /// <summary>"6,8 s" até um minuto; depois "1 min 12 s".</summary>
    public static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            var tenths = (long)(elapsed.TotalMilliseconds / 100);
            return $"{tenths / 10},{tenths % 10} s";
        }

        return $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds} s";
    }

    private void DescribeQaBlocked(QaBlockedException blocked)
    {
        var others = blocked.Report.OtherIssues;
        foreach (var issue in others.Take(MaxQaIssuesLogged))
        {
            AppendLog(QaLine(issue));
        }

        if (others.Count > MaxQaIssuesLogged)
        {
            AppendLog($"... e mais {ProjectLabels.Number(others.Count - MaxQaIssuesLogged)} ocorrência(s).");
        }

        if (blocked.Report.UntranslatedCount > 0)
        {
            AppendLog(QaBlockedException.DescribeUntranslated(blocked.Report));
        }

        Fail("Build bloqueado pelo QA", $"O QA bloqueou o build ({QaBlockedException.DescribeBlocking(blocked.Report)}) e nada foi gravado. Corrija a tradução, marque o aviso como ok na linha ou ligue 'Permitir avisos do QA' para liberar só os avisos.");
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
        if (Log.Count > 0 && Log[^1].Message == line)
        {
            return;
        }

        Log.Add(new BuildLogLine(clock.GetLocalNow().ToString("HH:mm:ss", CultureInfo.InvariantCulture), line));
    }

    private RunTimer StartTimer() => new(clock);

    /// <summary>Marca o início de uma operação e mede quanto ela levou; o cabeçalho do log mostra o início e a duração.</summary>
    private sealed class RunTimer(TimeProvider clock)
    {
        private readonly DateTimeOffset started = clock.GetLocalNow();
        private readonly long startStamp = clock.GetTimestamp();
        private TimeSpan? elapsed;

        public TimeSpan Stop() => elapsed ??= clock.GetElapsedTime(startStamp);

        public string Summary => $"{started.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} · {FormatDuration(Stop())}";
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
                state = await Task.Run(() => ReadState(root, qa ?? QaEngine.Default));
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
        GameExecutable = state?.GameExecutable ?? string.Empty;
        PlanMessage = state?.PlanMessage ?? string.Empty;
        BackupLocation = state?.BackupLocation ?? string.Empty;
        BackupMessage = state?.BackupMessage ?? string.Empty;
        Changes.Clear();
        foreach (var file in state?.Plan?.Files ?? [])
        {
            Changes.Add(new PlannedChange(file.RelativePath, file.ReplacesExisting ? "Substitui o original" : "Arquivo novo"));
        }

        QaWarningCount = state?.QaWarnings ?? 0;
        QaErrorCount = state?.QaErrors ?? 0;
        ShowQaBanner(state);
        ShowBackups(state);
        ShowResult(state);
        UpdateCommands();
    }

    private void ShowQaBanner(ProjectState? state)
    {
        if (QaErrorCount == 0)
        {
            QaBannerTitle = string.Empty;
            QaBannerDetail = string.Empty;
            return;
        }

        QaBannerTitle = $"{ProjectLabels.Number(QaErrorCount)} {Plural(QaErrorCount, "erro novo impede", "erros novos impedem")} um build novo.";
        QaBannerDetail = state?.LastBuildAt is { } built
            ? $"O build abaixo é de {built.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture)} e continua válido para injetar."
            : "Corrija os erros na Conferência para poder fazer o build.";
    }

    private void ShowBackups(ProjectState? state)
    {
        Backups.Clear();
        foreach (var backup in state?.Backups ?? [])
        {
            Backups.Add(new BackupRow(BackupRelative(backup.Name), FormatSize(backup.SizeBytes), DescribeBackup(backup)));
        }

        HasRestoreProblem = Backups.Count > 0 && restorePlan is null;
        var separator = Path.DirectorySeparatorChar;
        var count = Backups.Count;
        var footer = $"Pasta backup{separator} do projeto";
        if (state?.BackupIgnored == true)
        {
            footer += " · fora do git";
        }

        BackupFooter = count == 0 ? footer : $"{footer} · {ProjectLabels.Number(count)} {Plural(count, "backup", "backups")}";
    }

    private static string BackupRelative(string name) => $"backup{Path.DirectorySeparatorChar}{name}";

    private static string DescribeBackup(BackupInfo backup)
    {
        if (backup.Problem is not null)
        {
            return backup.Problem;
        }

        var parts = new List<string>();
        var originals = backup.OriginalFiles;
        switch (originals.Count)
        {
            case 0:
                break;
            case 1:
                parts.Add($"{originals[0]} original");
                break;
            case 2:
                parts.Add($"{originals[0]} e {originals[1]} originais");
                break;
            default:
                parts.Add($"{ProjectLabels.Number(originals.Count)} arquivos originais");
                break;
        }

        var created = backup.CreatedFiles.Count;
        if (created > 0)
        {
            parts.Add($"{ProjectLabels.Number(created)} {Plural(created, "arquivo criado", "arquivos criados")}");
        }

        var manifest = backup.Manifest!;
        parts.Add($"injeção de {manifest.CreatedAt.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture)}");
        if (manifest.RestoredAt is { } restored)
        {
            parts.Add($"restaurado em {restored.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture)}");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// O InfoBar do Resultado diz em que pé o jogo está. A restauração que acabou de acontecer só existe na sessão (o backup devolvido some
    /// da lista de pendentes); o resto sai do disco: sem build, build por injetar, ou build já igual ao jogo com um backup que o restaura.
    /// </summary>
    private void ShowResult(ProjectState? state)
    {
        if (state is null)
        {
            SetResult(BuildResultTone.Info, string.Empty, string.Empty);
            return;
        }

        if (restoredFrom is not null)
        {
            SetResult(
                BuildResultTone.Warning,
                "Originais restaurados.",
                $"O jogo voltou aos arquivos de {BackupRelative(Path.GetFileName(restoredFrom))}. O build continua disponível para injetar.");
        }
        else if (!state.HasBuild)
        {
            SetResult(BuildResultTone.Info, "Ainda não há build.", state.PlanMessage);
        }
        else if (state.Plan is null)
        {
            SetResult(BuildResultTone.Warning, "Não foi possível comparar o build com o jogo.", state.PlanMessage);
        }
        else if (state.Plan.Files.Count > 0)
        {
            SetResult(
                BuildResultTone.Info,
                "Build pronto, ainda não injetado.",
                state.Restore is null ? "O jogo continua com os arquivos originais." : state.PlanMessage);
        }
        else if (state.Restore is { } restore)
        {
            SetResult(
                BuildResultTone.Success,
                "Injetado no jogo.",
                $"Originais copiados para {BackupRelative(Path.GetFileName(restore.BackupDirectory))} antes da cópia.");
        }
        else
        {
            SetResult(BuildResultTone.Info, "O build já é igual ao jogo.", state.PlanMessage);
        }
    }

    private void SetResult(BuildResultTone tone, string title, string text)
    {
        ResultTone = tone;
        ResultTitle = title;
        ResultText = text;
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

    private static ProjectState ReadState(string root, QaEngine engine)
    {
        var project = ProjectFolder.Open(root);
        var config = project.LoadConfig();
        var gameFolder = Path.GetFullPath(config.Game.Path, project.Root);
        var gameExecutable = Path.GetFullPath(Path.Combine(gameFolder, config.Game.Exe));
        var buildFiles = Directory.Exists(project.BuildDirectory)
            ? Directory.EnumerateFiles(project.BuildDirectory, "*", SearchOption.AllDirectories).ToList()
            : [];
        var hasBuild = buildFiles.Count > 0;
        DateTimeOffset? lastBuildAt = hasBuild ? buildFiles.Max(f => new DateTimeOffset(File.GetLastWriteTimeUtc(f), TimeSpan.Zero)) : null;

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

        var (qaErrors, qaWarnings) = CountQaIssues(project, engine);
        return new ProjectState(gameFolder, gameExecutable, hasBuild, lastBuildAt, plan, planMessage, restore, backupLocation, backupMessage, GameInjector.ListBackups(project), IsBackupIgnored(project), qaErrors, qaWarnings);
    }

    /// <summary>O <c>.gitignore</c> do projeto lista <c>backup/</c>: só então o rodapé garante que os originais ficam fora do git.</summary>
    private static bool IsBackupIgnored(ProjectFolder project)
    {
        try
        {
            var path = Path.Combine(project.Root, ".gitignore");
            return File.Exists(path) && File.ReadAllLines(path).Any(l => l.Trim() is "backup/" or "/backup/" or "backup");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Os erros e avisos que o QA acha agora, os mesmos que a Conferência lista e que o <c>vnf build</c> usa para recusar. Um QA que não roda
    /// (texto ilegível, regra que lança) não trava o build aqui: o Core recusa com a mensagem certa se for o caso.
    /// </summary>
    private static (int Errors, int Warnings) CountQaIssues(ProjectFolder project, QaEngine engine)
    {
        try
        {
            var report = engine.Run(QaContext.FromProject(project));
            return (report.Errors.Count, report.OtherIssues.Count(i => i.Severity == QaSeverity.Warning));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return (0, 0);
        }
    }

    private static string DescribePlan(InjectionPlan plan)
    {
        if (plan.Files.Count == 0)
        {
            return $"Os {ProjectLabels.Number(plan.UnchangedCount)} arquivo(s) de build/ já estão iguais no jogo. Nada a injetar.";
        }

        var unchanged = string.Empty;
        if (plan.UnchangedCount > 0)
        {
            var verb = plan.UnchangedCount == 1 ? "está igual" : "estão iguais";
            unchanged = $"; {ProjectLabels.Number(plan.UnchangedCount)} já {verb} no jogo";
        }

        return $"A injeção grava {ProjectLabels.Number(plan.Files.Count)} arquivo(s) na pasta do jogo{unchanged}.";
    }

    private static bool IsExpected(Exception e) =>
        e is VNForgeException or IOException or UnauthorizedAccessException or OperationCanceledException;

    private sealed record ProjectState(
        string GameFolder,
        string GameExecutable,
        bool HasBuild,
        DateTimeOffset? LastBuildAt,
        InjectionPlan? Plan,
        string PlanMessage,
        RestorePlan? Restore,
        string BackupLocation,
        string BackupMessage,
        IReadOnlyList<BackupInfo> Backups,
        bool BackupIgnored,
        int QaErrors,
        int QaWarnings)
    {
        public static ProjectState Unreadable(string message) =>
            new(string.Empty, string.Empty, false, null, null, message, null, string.Empty, string.Empty, [], false, 0, 0);
    }

    /// <summary>
    /// O progresso por script do Core vira o andamento da tarefa da barra inferior (fração e nome do script). Fica fora do log: um build de
    /// centenas de scripts não vira centenas de linhas, e o log mostra só o que o build fez.
    /// </summary>
    private sealed class TaskProgress(IBackgroundTaskReporter reporter) : IProgress<ScriptWorkProgress>
    {
        public void Report(ScriptWorkProgress value)
        {
            if (value.Total <= 0)
            {
                return;
            }

            var fraction = (double)value.Done / value.Total;
            reporter.Report(
                fraction,
                value.Name.Length == 0
                    ? $"Reconstruindo os scripts ({ProjectLabels.Number(value.Total)} de {ProjectLabels.Number(value.Total)})"
                    : $"Reconstruindo {value.Name} ({ProjectLabels.Number(value.Done + 1)} de {ProjectLabels.Number(value.Total)})");
        }
    }

    private readonly record struct TaskRun<T>(T? Value, Exception? Failure);
}
