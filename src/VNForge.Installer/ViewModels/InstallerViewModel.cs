using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Patching;

namespace VNForge.Installer.ViewModels;

/// <summary>As janelas do instalador: as cinco do quadro (#540) e as três que o quadro deixou como "estados que faltam".</summary>
public enum InstallerScreen
{
    /// <summary>1 · Pasta do jogo.</summary>
    Folder,

    /// <summary>2 · Instalando (e, com outro texto, voltando ao original).</summary>
    Installing,

    /// <summary>3 · Concluído.</summary>
    Done,

    /// <summary>4 · Versão errada do jogo.</summary>
    WrongVersion,

    /// <summary>5 · Já instalada: desinstalar.</summary>
    AlreadyInstalled,

    /// <summary>A instalação (ou a volta ao original) parou: cancelada, ou com erro. O quadro a lista como estado que falta.</summary>
    Stopped,

    /// <summary>O jogo voltou ao original.</summary>
    Reverted,
}

public enum InstallerCardKind
{
    Ok,
    Error,
    Working,
}

/// <summary>O cartão da janela: ícone pelo tipo, título em negrito e uma linha de detalhe.</summary>
public sealed record InstallerCard(InstallerCardKind Kind, string Title, string Detail);

/// <summary>De onde vem a pasta que a janela 1 mostra; muda a frase de apoio.</summary>
public enum FolderSource
{
    Searched,
    Picked,
}

/// <param name="Folder">A pasta dada em <c>--pasta</c>; nula quando o instalador deve procurar.</param>
public sealed record InstallerStart(string? Folder, string CurrentDirectory, string? ExecutableDirectory);

/// <summary>
/// O estado das janelas do instalador. Confere a pasta, instala e desinstala pelo <see cref="IInstallerEngine"/> (que guarda os originais, confere o SHA-256
/// e devolve tudo se algo falha) e decide qual das janelas mostrar. Nenhuma regra do patch mora aqui.
/// </summary>
public sealed partial class InstallerViewModel : ObservableObject
{
    private enum Operation
    {
        None,
        Install,
        Revert,
    }

    private readonly IInstallerEngine? _engine;
    private readonly IInstallerShell _shell;
    private readonly TimeProvider _time;
    private readonly InstallerStart _start;
    private readonly Action<Action> _post;
    private PatchCheck? _check;
    private CancellationTokenSource? _cancel;
    private Operation _operation;
    private long _stepStarted;
    private long _installTotalBytes;
    private int _backupCount;
    private long _backupBytes;
    private int _appliedCount;
    private long[] _backupWeights = [];
    private long[] _applyWeights = [];
    private int _revertTotal;

    /// <param name="engine">O patch e a pasta do jogo; nulo num instalador vazio (sem patch dentro).</param>
    /// <param name="post">Entrega um trecho à thread da tela; o padrão roda na hora (os testes). O andamento vem de outra thread.</param>
    public InstallerViewModel(IInstallerEngine? engine, IInstallerShell shell, InstallerStart start, TimeProvider? time = null, Action<Action>? post = null)
    {
        _engine = engine;
        _shell = shell;
        _start = start;
        _time = time ?? TimeProvider.System;
        _post = post ?? (action => action());
        if (engine is null)
        {
            ShowEmpty();
        }
        else
        {
            Title = "Instalar a tradução";
        }
    }

    public string GameName => _engine?.Manifest.GameName ?? string.Empty;

    public string WindowTitle => _engine is null ? "Tradução PT-BR" : $"Tradução PT-BR · {GameName}";

    public string SideLine { get; } = "Tradução para português do Brasil";

    public string SideVersionLine => _engine is null
        ? string.Empty
        : string.IsNullOrWhiteSpace(_engine.Manifest.Credits) ? $"Versão {_engine.Manifest.Version}" : $"Versão {_engine.Manifest.Version} · {_engine.Manifest.Credits.Trim()}";

    /// <summary>As duas linhas sob o nome do jogo, na coluna da esquerda.</summary>
    public string SideInfo => _engine is null ? SideLine : $"{SideLine}\n{SideVersionLine}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFolder), nameof(IsInstalling), nameof(IsDone), nameof(IsWrongVersion), nameof(IsAlreadyInstalled), nameof(IsStopped), nameof(IsReverted), nameof(ShowFolderRow), nameof(ShowSteps), nameof(ShowReadMe), nameof(ShowOpenGame), nameof(ShowClose), nameof(ShowRetry))]
    public partial InstallerScreen Screen { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIntro))]
    public partial string Intro { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FolderPath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCard), nameof(CardIsOk), nameof(CardIsError), nameof(CardIsWorking), nameof(CardTitle), nameof(CardDetail))]
    public partial InstallerCard? Card { get; set; }

    /// <summary>0 a 100: o andamento da etapa em curso.</summary>
    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelInstallCommand))]
    public partial bool CanCancelInstall { get; set; }

    /// <summary>Há trabalho em curso: conferindo a pasta, instalando ou voltando ao original. A janela não fecha enquanto isso.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(BrowseCommand), nameof(RevertCommand))]
    public partial bool IsBusy { get; set; }

    public ObservableCollection<InstallStepViewModel> Steps { get; } = [];

    /// <summary>O código de saída que a janela devolve ao fechar: 0 feito, 2 falhou, 3 versão errada, 4 cancelado.</summary>
    public int ExitCode { get; private set; } = InstallerApp.Cancelled;

    /// <summary>Abre o seletor de pasta do sistema e devolve a escolhida (nulo se o jogador desistiu); a janela o liga.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>Lê a versão que o arquivo da pasta traz (o Game.exe que não confere); a janela de versão errada a cita. Troca-se nos testes.</summary>
    public Func<string, string?> ReadFileVersion { get; set; } = InstallerText.FileVersionOf;

    public event EventHandler? CloseRequested;

    public bool IsFolder => Screen == InstallerScreen.Folder;

    public bool IsInstalling => Screen == InstallerScreen.Installing;

    public bool IsDone => Screen == InstallerScreen.Done;

    public bool IsWrongVersion => Screen == InstallerScreen.WrongVersion;

    public bool IsAlreadyInstalled => Screen == InstallerScreen.AlreadyInstalled;

    public bool IsStopped => Screen == InstallerScreen.Stopped;

    public bool IsReverted => Screen == InstallerScreen.Reverted;

    public bool ShowFolderRow => IsFolder;

    public bool ShowSteps => IsInstalling;

    public bool ShowReadMe => IsDone && HasReadMe;

    public bool ShowOpenGame => IsDone && CanOpenGame;

    /// <summary>O botão Fechar comum; na janela "Jogo original de volta" ele é o botão principal e tem o seu próprio.</summary>
    public bool ShowClose => IsDone || IsAlreadyInstalled || IsStopped;

    public bool ShowRetry => IsStopped && _engine is not null;

    public bool HasIntro => !string.IsNullOrEmpty(Intro);

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public bool HasCard => Card is not null;

    public bool CardIsOk => Card?.Kind == InstallerCardKind.Ok;

    public bool CardIsError => Card?.Kind == InstallerCardKind.Error;

    public bool CardIsWorking => Card?.Kind == InstallerCardKind.Working;

    public string CardTitle => Card?.Title ?? string.Empty;

    public string CardDetail => Card?.Detail ?? string.Empty;

    /// <summary>O jogador pode abrir o leia-me que veio no patch.</summary>
    public bool HasReadMe => !string.IsNullOrWhiteSpace(_engine?.Manifest.ReadMe);

    /// <summary>Há um executável a abrir depois de instalar (<see cref="IInstallerShell.FindGame"/>).</summary>
    public bool CanOpenGame => GameExecutablePath is not null;

    public string? GameExecutablePath { get; private set; }

    /// <summary>A capa que veio no patch (PNG, JPEG ou WebP); nula quando o patch não leva capa e a janela desenha a dela.</summary>
    public byte[]? CoverBytes => _engine?.Manifest.Cover is { Length: > 0 } cover ? cover : null;

    public bool HasCover => CoverBytes is not null;

    /// <summary>Procura o jogo (ou usa a pasta de <c>--pasta</c>) e mostra a janela que cabe: pasta conferida, versão errada ou já instalada.</summary>
    public async Task StartAsync()
    {
        if (_engine is null)
        {
            return;
        }

        ShowChecking(string.Empty);
        if (_start.Folder is { } given)
        {
            await CheckAsync(Path.GetFullPath(given, _start.CurrentDirectory), FolderSource.Picked);
            return;
        }

        IsBusy = true;
        (string Folder, PatchCheck Check)? found;
        try
        {
            found = await Task.Run(() => GameFolderLocator.Find(_engine, GameFolderLocator.Candidates(_start.CurrentDirectory, _start.ExecutableDirectory)));
        }
        finally
        {
            IsBusy = false;
        }

        if (found is null)
        {
            ShowNoFolder();
            return;
        }

        await ShowCheckAsync(found.Value.Folder, found.Value.Check, FolderSource.Searched);
    }

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private async Task BrowseAsync()
    {
        if (PickFolder is null || await PickFolder() is not { Length: > 0 } picked)
        {
            return;
        }

        await CheckAsync(picked, FolderSource.Picked);
    }

    private bool CanBrowse() => !IsBusy && _engine is not null;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (_engine is null || _check is null)
        {
            return;
        }

        var folder = FolderPath;
        var todo = _check.Files.Where(f => f.State != PatchFileState.Patched).ToList();
        var entries = _engine.Manifest.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        _backupWeights = [.. todo.Select(f => Math.Max(1, entries[f.Path].OriginalSize))];
        _applyWeights = [.. todo.Select(f => Math.Max(1, entries[f.Path].ResultSize))];
        var backedUp = todo.Where(f => !entries[f.Path].Created && f.State is PatchFileState.Original or PatchFileState.Upgrade).ToList();
        _backupCount = backedUp.Count;
        _backupBytes = backedUp.Sum(f => entries[f.Path].OriginalSize);
        _appliedCount = todo.Count;
        _installTotalBytes = _backupBytes + (2 * todo.Sum(f => entries[f.Path].ResultSize));
        BeginRun(Operation.Install, "Instalando", $"Não feche o instalador. {InstallerText.EstimateSentence(_installTotalBytes)}", ["Conferir a versão do jogo", "Guardar a cópia dos originais", "Aplicar a tradução", "Conferir o resultado"]);
        var token = _cancel!.Token;
        try
        {
            var result = await Task.Run(() => _engine.Install(folder, new UiProgress(this), token));
            FinishAllSteps(result.Written.Count);
            ShowDone(folder, result);
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (Exception ex)
        {
            ShowFailure("A instalação não terminou", "Não foi possível instalar a tradução", ex.Message);
        }
        finally
        {
            EndRun();
        }
    }

    private bool CanInstall() => !IsBusy && IsFolder && _check is { State: PatchCheckState.Ready } && Card is { Kind: InstallerCardKind.Ok };

    [RelayCommand(CanExecute = nameof(CanCancelInstall))]
    private void CancelInstall() => _cancel?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRevert))]
    private async Task RevertAsync()
    {
        if (_engine is null)
        {
            return;
        }

        var folder = FolderPath;
        BeginRun(Operation.Revert, "Voltando ao jogo original", "Não feche o instalador.", ["Devolver os originais"]);
        var token = _cancel!.Token;
        try
        {
            var result = await Task.Run(() => _engine.Uninstall(folder, new UiProgress(this), token));
            FinishAllSteps(result.Restored.Count + result.Removed.Count);
            ShowReverted(result);
        }
        catch (OperationCanceledException)
        {
            ShowCancelled();
        }
        catch (Exception ex)
        {
            ShowFailure("A volta ao original não terminou", "Não foi possível voltar ao jogo original", ex.Message);
        }
        finally
        {
            EndRun();
        }
    }

    private bool CanRevert() => !IsBusy && IsAlreadyInstalled && Card is { Kind: InstallerCardKind.Ok };

    /// <summary>"Tentar de novo": volta a conferir a pasta, que pode ter mudado (o jogo foi fechado, a permissão foi dada).</summary>
    [RelayCommand]
    private async Task RetryAsync()
    {
        if (string.IsNullOrEmpty(FolderPath))
        {
            await StartAsync();
            return;
        }

        await CheckAsync(FolderPath, FolderSource.Picked);
    }

    /// <summary>Fecha a janela com o código de saída do estado em que ela está.</summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(HasReadMe))]
    private void OpenReadMe()
    {
        if (_engine?.Manifest.ReadMe is { } text)
        {
            _shell.OpenReadMe(text);
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenGame))]
    private void OpenGame()
    {
        if (GameExecutablePath is not { } path)
        {
            return;
        }

        try
        {
            _shell.OpenGame(path);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Note = $"Não consegui abrir o jogo: {ex.Message} Abra-o pelo atalho de sempre.";
            return;
        }

        Close();
    }

    private async Task CheckAsync(string folder, FolderSource source)
    {
        if (_engine is null)
        {
            return;
        }

        ShowChecking(folder);
        IsBusy = true;
        try
        {
            var check = await Task.Run(() => _engine.Check(folder));
            await ShowCheckAsync(folder, check, source);
        }
        catch (Exception ex) when (ex is PatchException or IOException or UnauthorizedAccessException)
        {
            ShowFolderError(folder, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ShowCheckAsync(string folder, PatchCheck check, FolderSource source)
    {
        _check = check;
        FolderPath = folder;
        switch (check.State)
        {
            case PatchCheckState.WrongVersion:
                // Um arquivo que mudou depois da nossa instalação não é "outra versão do jogo": é a janela 5, com o aviso de que ele fica como está.
                if (check.InstalledVersion is not null && await TryPlanAsync(folder) is { Kept.Count: > 0 } changed)
                {
                    ShowAlreadyInstalled(changed);
                }
                else
                {
                    ShowWrongVersion(check);
                }

                break;

            case PatchCheckState.AlreadyInstalled:
                if (await TryPlanAsync(folder) is { } plan)
                {
                    ShowAlreadyInstalled(plan);
                }
                else
                {
                    ShowFolderError(folder, "A tradução já está nesta pasta, mas não foi este instalador que a pôs, então não há cópia dos originais para devolver.");
                }

                break;

            default:
                ShowReady(folder, check, source);
                break;
        }

        NotifyCommands();
    }

    private async Task<PatchUninstallPlan?> TryPlanAsync(string folder)
    {
        try
        {
            return await Task.Run(() => _engine!.PlanUninstall(folder));
        }
        catch (Exception ex) when (ex is PatchException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void ShowChecking(string folder)
    {
        Screen = InstallerScreen.Folder;
        Title = "Instalar a tradução";
        Intro = "Escolha a pasta onde o jogo está instalado.";
        Note = string.Empty;
        FolderPath = folder;
        Card = new InstallerCard(InstallerCardKind.Working, "Conferindo a versão do jogo", "Isto leva alguns segundos.");
        ExitCode = InstallerApp.Cancelled;
        NotifyCommands();
    }

    private void ShowNoFolder()
    {
        Screen = InstallerScreen.Folder;
        Title = "Instalar a tradução";
        Intro = "Escolha a pasta onde o jogo está instalado. O instalador procurou e não achou.";
        Note = "Antes de mexer no jogo, o instalador guarda uma cópia dos arquivos originais.";
        FolderPath = string.Empty;
        Card = null;
        _check = null;
        ExitCode = InstallerApp.Cancelled;
        NotifyCommands();
    }

    private void ShowFolderError(string folder, string message)
    {
        Screen = InstallerScreen.Folder;
        Title = "Instalar a tradução";
        Intro = "Escolha a pasta onde o jogo está instalado.";
        Note = string.Empty;
        FolderPath = folder;
        Card = new InstallerCard(InstallerCardKind.Error, "Não consegui conferir esta pasta", message);
        _check = null;
        ExitCode = InstallerApp.Cancelled;
        NotifyCommands();
    }

    private void ShowReady(string folder, PatchCheck check, FolderSource source)
    {
        var checkedFiles = check.Files.Where(f => f.Expected is not null).ToList();
        InstallerCard card;
        if (checkedFiles.Count == 0)
        {
            card = new InstallerCard(InstallerCardKind.Ok, "Pasta conferida", "O patch só acrescenta arquivos: nenhum arquivo do jogo vai mudar.");
        }
        else
        {
            // O quadro cita um arquivo só; o executável é o que o jogador reconhece. Os outros foram conferidos do mesmo jeito.
            var shown = checkedFiles.FirstOrDefault(f => f.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ?? checkedFiles[0];
            card = new InstallerCard(InstallerCardKind.Ok, "Versão do jogo conferida", $"{InstallerText.FileName(shown.Path)} confere com a versão esperada (SHA-256 {InstallerText.ShortHash(shown.Expected)}).");
        }

        Screen = InstallerScreen.Folder;
        Title = "Instalar a tradução";
        Intro = source == FolderSource.Searched
            ? "Escolha a pasta onde o jogo está instalado. O instalador procurou e achou esta."
            : "Escolha a pasta onde o jogo está instalado.";
        Note = "Antes de mexer no jogo, o instalador guarda uma cópia dos arquivos originais.";
        FolderPath = folder;
        Card = card;
        ExitCode = InstallerApp.Cancelled;
    }

    private void ShowWrongVersion(PatchCheck check)
    {
        var bad = check.Files.Where(f => f.State is PatchFileState.Missing or PatchFileState.Different).ToList();
        var first = bad[0];
        var name = InstallerText.FileName(first.Path);
        var extra = bad.Count switch
        {
            1 => string.Empty,
            2 => " Mais 1 arquivo também não confere.",
            _ => $" Mais {InstallerText.Files(bad.Count - 1)} também não conferem.",
        };
        InstallerCard card;
        if (first.Found is null)
        {
            card = new InstallerCard(InstallerCardKind.Error, $"Não achei o {name} nesta pasta", $"Esperado: SHA-256 {InstallerText.ShortHash(first.Expected)}.{extra} Nada foi alterado.");
        }
        else if (first.Expected is null)
        {
            card = new InstallerCard(InstallerCardKind.Error, $"O {name} desta pasta é diferente", $"Encontrado: SHA-256 {InstallerText.ShortHash(first.Found)}. O patch esperava que ele não existisse.{extra} Nada foi alterado.");
        }
        else
        {
            card = new InstallerCard(InstallerCardKind.Error, $"O {name} desta pasta é diferente", $"Encontrado: SHA-256 {InstallerText.ShortHash(first.Found)}. Esperado: {InstallerText.ShortHash(first.Expected)}.{extra} Nada foi alterado.");
        }

        Screen = InstallerScreen.WrongVersion;
        Title = "Esta não é a versão esperada";
        Intro = string.Empty;
        Card = card;
        var manifest = _engine!.Manifest;
        if (string.IsNullOrWhiteSpace(manifest.GameVersion))
        {
            Note = $"A tradução {manifest.Version} foi feita para outra versão do jogo. Procure uma pasta com a versão certa, ou veja no leia-me qual é e onde conseguir.";
        }
        else
        {
            var wanted = manifest.GameVersion.Trim();
            var found = first.Found is null ? null : ReadFileVersion(Path.Combine(FolderPath, first.Path));
            var whose = string.IsNullOrWhiteSpace(found) || found.Trim() == wanted ? "sua versão" : found.Trim();
            Note = $"A tradução {manifest.Version} foi feita para a versão {wanted} do jogo. Procure uma pasta com essa versão, ou veja no leia-me onde conseguir o patch da {whose}.";
        }

        ExitCode = InstallerApp.WrongVersion;
    }

    private void ShowAlreadyInstalled(PatchUninstallPlan plan)
    {
        var count = plan.Restore.Count + plan.Remove.Count;
        var restoreNames = plan.Restore.Select(InstallerText.FileName).ToList();
        var removeNames = plan.Remove.Select(InstallerText.FileName).ToList();
        InstallerCard card;
        string intro;
        if (plan.BrokenBackups.Count > 0)
        {
            var name = InstallerText.FileName(plan.BrokenBackups[0]);
            card = new InstallerCard(InstallerCardKind.Error, $"A cópia do original de {name} não confere", "Sem ela o instalador não devolve o jogo. Nada foi alterado; instale o jogo de novo.");
            intro = "Voltar ao original só é possível com as cópias que o instalador guardou na instalação.";
        }
        else
        {
            var detail = count == 0
                ? "Nenhum arquivo do jogo mudou desde a instalação."
                : $"{InstallerText.NameList(restoreNames)} {(restoreNames.Count == 1 ? "volta" : "voltam")} byte a byte.";
            if ((restoreNames.Count == 0 && removeNames.Count > 0) || (count == 0 && plan.Kept.Count > 0))
            {
                detail = string.Empty;
            }

            if (removeNames.Count > 0)
            {
                detail = $"{detail} {InstallerText.NameList(removeNames)} {(removeNames.Count == 1 ? "sai" : "saem")} da pasta.".Trim();
            }

            if (plan.Kept.Count > 0)
            {
                var kept = plan.Kept.Count == 1;
                detail = $"{detail} {InstallerText.NameList([.. plan.Kept.Select(InstallerText.FileName)])} {(kept ? "fica" : "ficam")} como {(kept ? "está" : "estão")}: {(kept ? "mudou" : "mudaram")} depois da instalação.".Trim();
            }

            card = new InstallerCard(InstallerCardKind.Ok, count == 0 ? "Nenhum arquivo para devolver" : $"{InstallerText.Files(count)} para devolver", detail);
            var when = plan.InstalledAt is { } at
                ? $", guardados em {TimeZoneInfo.ConvertTime(at, _time.LocalTimeZone).ToString("dd/MM 'às' HH:mm", System.Globalization.CultureInfo.InvariantCulture)},"
                : string.Empty;
            intro = $"Os arquivos originais{when} estão inteiros. Voltar ao original apaga a tradução do jogo e não mexe nos seus saves.";
        }

        Screen = InstallerScreen.AlreadyInstalled;
        Title = $"A tradução {plan.Version} já está instalada";
        Intro = intro;
        Note = string.Empty;
        Card = card;
        ExitCode = InstallerApp.Ok;
    }

    private void ShowDone(string folder, PatchInstallResult result)
    {
        var leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)));
        var changed = result.Written.Count == 0 ? "Nenhum arquivo precisou mudar." : $"{InstallerText.Files(result.Written.Count)} {(result.Written.Count == 1 ? "alterado" : "alterados")}.";
        GameExecutablePath = _shell.FindGame(folder, _engine!.Manifest);
        OnPropertyChanged(nameof(GameExecutablePath));
        OnPropertyChanged(nameof(CanOpenGame));
        OnPropertyChanged(nameof(ShowOpenGame));
        OpenGameCommand.NotifyCanExecuteChanged();
        Screen = InstallerScreen.Done;
        Title = "Tradução instalada";
        Intro = string.Empty;
        Card = new InstallerCard(InstallerCardKind.Ok, "Pronto. Abra o jogo normalmente.", $"{changed} Os originais estão guardados em {leaf}{Path.DirectorySeparatorChar}{PatchInstaller.BackupFolderName}.");
        Note = "Para voltar ao jogo original, execute este instalador de novo e escolha Voltar ao jogo original.";
        ExitCode = InstallerApp.Ok;
    }

    private void ShowReverted(PatchUninstallResult result)
    {
        var count = result.Restored.Count + result.Removed.Count;
        var kept = result.Kept.Count == 0
            ? string.Empty
            : $" Deixei {InstallerText.NameList([.. result.Kept.Select(InstallerText.FileName)])} como {(result.Kept.Count == 1 ? "está" : "estão")}: mudou depois da instalação.";
        Screen = InstallerScreen.Reverted;
        Title = "Jogo original de volta";
        Intro = string.Empty;
        Card = new InstallerCard(InstallerCardKind.Ok, "Pronto. O jogo está como antes da tradução.", $"{InstallerText.Files(count)} {(count == 1 ? "voltou" : "voltaram")} ao original, byte a byte.{kept}");
        Note = string.Empty;
        ExitCode = InstallerApp.Ok;
    }

    private void ShowCancelled()
    {
        Screen = InstallerScreen.Stopped;
        Title = _operation == Operation.Revert ? "A volta ao original foi cancelada" : "Instalação cancelada";
        Intro = string.Empty;
        Card = new InstallerCard(InstallerCardKind.Ok, "Os arquivos do jogo estão como antes", "Nada ficou pela metade. Você pode instalar de novo quando quiser.");
        Note = string.Empty;
        ExitCode = InstallerApp.Cancelled;
    }

    private void ShowFailure(string title, string cardTitle, string message)
    {
        Screen = InstallerScreen.Stopped;
        Title = title;
        Intro = string.Empty;
        Card = new InstallerCard(InstallerCardKind.Error, cardTitle, message);
        Note = string.Empty;
        ExitCode = InstallerApp.Failed;
    }

    private void ShowEmpty()
    {
        Screen = InstallerScreen.Stopped;
        Title = "Este instalador está vazio";
        Card = new InstallerCard(InstallerCardKind.Error, "Não achei o patch dentro dele", "Baixe o instalador de novo.");
        ExitCode = InstallerApp.Failed;
    }

    private void BeginRun(Operation operation, string title, string intro, string[] steps)
    {
        _operation = operation;
        _cancel = new CancellationTokenSource();
        Steps.Clear();
        foreach (var step in steps)
        {
            Steps.Add(new InstallStepViewModel(step));
        }

        Progress = 0;
        Title = title;
        Intro = intro;
        Note = string.Empty;
        Card = null;
        IsBusy = true;
        Screen = InstallerScreen.Installing;
        CanCancelInstall = operation == Operation.Install;
        ExitCode = InstallerApp.Cancelled;
        _stepStarted = _time.GetTimestamp();
        Steps[0].State = InstallStepState.Running;
        NotifyCommands();
    }

    private void EndRun()
    {
        _cancel?.Dispose();
        _cancel = null;
        _operation = Operation.None;
        CanCancelInstall = false;
        IsBusy = false;
        NotifyCommands();
    }

    private void OnProgress(PatchProgress progress)
    {
        var index = StepIndex(progress.Phase);
        if (index < 0 || index >= Steps.Count)
        {
            return;
        }

        AdvanceTo(index);
        CanCancelInstall = _operation == Operation.Install && progress.Phase is PatchPhase.Check or PatchPhase.Backup && _cancel is { IsCancellationRequested: false };
        if (progress.Phase == PatchPhase.Check)
        {
            return;
        }

        var fraction = Fraction(progress);
        Progress = fraction * 100;
        Steps[index].Detail = InstallerText.Percent(fraction);
    }

    private int StepIndex(PatchPhase phase) => _operation == Operation.Revert
        ? phase == PatchPhase.Restore ? 0 : -1
        : phase switch
        {
            PatchPhase.Check => 0,
            PatchPhase.Backup => 1,
            PatchPhase.Apply => 2,
            PatchPhase.Verify => 3,
            _ => -1,
        };

    private double Fraction(PatchProgress progress)
    {
        if (progress.Path is null)
        {
            return progress.Total == 0 || progress.Done >= progress.Total ? 1 : 0;
        }

        var weights = progress.Phase switch
        {
            PatchPhase.Backup => _backupWeights,
            PatchPhase.Apply or PatchPhase.Verify => _applyWeights,
            _ => [],
        };
        if (weights.Length == progress.Total && weights.Length > 0)
        {
            return (double)weights.Take(progress.Done).Sum() / weights.Sum();
        }

        return progress.Total == 0 ? 1 : (double)progress.Done / progress.Total;
    }

    private void AdvanceTo(int index)
    {
        for (var i = 0; i < index; i++)
        {
            if (Steps[i].State != InstallStepState.Done)
            {
                FinishStep(i);
            }
        }

        if (Steps[index].State == InstallStepState.Pending)
        {
            _stepStarted = _time.GetTimestamp();
            Steps[index].State = InstallStepState.Running;
        }
    }

    private void FinishStep(int index)
    {
        var step = Steps[index];
        if (step.State == InstallStepState.Pending)
        {
            _stepStarted = _time.GetTimestamp();
        }

        step.Detail = _operation == Operation.Revert
            ? InstallerText.Files(_revertTotal)
            : index switch
            {
                1 => $"{InstallerText.Files(_backupCount)} · {InstallerText.Size(_backupBytes)}",
                2 => InstallerText.Files(_appliedCount),
                _ => InstallerText.Elapsed(_time.GetElapsedTime(_stepStarted)),
            };
        step.State = InstallStepState.Done;
        _stepStarted = _time.GetTimestamp();
    }

    private void FinishAllSteps(int count)
    {
        _revertTotal = count;
        for (var i = 0; i < Steps.Count; i++)
        {
            if (Steps[i].State != InstallStepState.Done)
            {
                FinishStep(i);
            }
        }

        Progress = 100;
    }

    private void NotifyCommands()
    {
        InstallCommand.NotifyCanExecuteChanged();
        BrowseCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
        OpenReadMeCommand.NotifyCanExecuteChanged();
    }

    private sealed class UiProgress(InstallerViewModel owner) : IProgress<PatchProgress>
    {
        public void Report(PatchProgress value) => owner._post(() => owner.OnProgress(value));
    }
}
