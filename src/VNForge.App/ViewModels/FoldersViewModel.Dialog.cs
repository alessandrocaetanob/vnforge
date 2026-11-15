using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core.UserData;

namespace VNForge.App.ViewModels;

/// <summary>Qual diálogo de pasta está aberto.</summary>
public enum FolderDialogMode
{
    None,

    /// <summary>"Onde guardar os seus dados?", sobre a Biblioteca, na primeira abertura.</summary>
    FirstRun,

    /// <summary>"Mover os dados para ...?", depois de Trocar pasta nos Ajustes.</summary>
    Confirm,

    /// <summary>A pasta nova está numa unidade de rede ou montada.</summary>
    Network,

    /// <summary>A cópia está rodando; a barra de progresso ocupa o lugar dos botões.</summary>
    Moving,

    /// <summary>A troca ou a escolha não deu certo: nada mudou.</summary>
    Failed,

    /// <summary>A pasta escolhida sumiu na abertura (disco ou unidade desconectada).</summary>
    Missing,
}

/// <summary>Os diálogos da escolha e da troca de pasta (quadro da #694), em um só estado: um aberto por vez, e cada um leva ao próximo.</summary>
public sealed partial class FoldersViewModel
{
    private enum Context
    {
        Settings,
        FirstRun,
        Missing,
    }

    private Context context;
    private UserFolderKind pending;
    private UserFolderMovePlan? plan;
    private string pendingTarget = string.Empty;
    private string pendingFrom = string.Empty;
    private UserFolderProblem? problem;
    private CancellationTokenSource? moving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDialogOpen), nameof(IsFirstRun), nameof(IsConfirm), nameof(IsNetwork), nameof(IsMoving), nameof(IsFailed), nameof(IsMissing), nameof(ShowMissingWarning), nameof(DialogWidth))]
    public partial FolderDialogMode Mode { get; private set; }

    /// <summary>Aberto enquanto há um diálogo. Fechar de fora (Escape) escolhe a saída segura de cada um: a sugestão na primeira pergunta, a volta no resto, o cancelamento da cópia.</summary>
    public bool IsDialogOpen
    {
        get => Mode != FolderDialogMode.None;
        set
        {
            if (!value)
            {
                Dismiss();
            }
        }
    }

    public bool IsFirstRun => Mode == FolderDialogMode.FirstRun;

    public bool IsConfirm => Mode == FolderDialogMode.Confirm;

    public bool IsNetwork => Mode == FolderDialogMode.Network;

    public bool IsMoving => Mode == FolderDialogMode.Moving;

    public bool IsFailed => Mode == FolderDialogMode.Failed;

    public bool IsMissing => Mode == FolderDialogMode.Missing;

    /// <summary>640 na primeira pergunta e 560 nos demais, como no quadro.</summary>
    public double DialogWidth => IsFirstRun ? 640 : 560;

    // Primeira abertura.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigHint))]
    public partial string DraftConfig { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string DraftCache { get; private set; } = string.Empty;

    public string ConfigHint => string.Equals(DraftConfig, folders.DefaultConfig, StringComparison.OrdinalIgnoreCase)
        ? "Sugestão do Windows. Fica no seu perfil."
        : "Escolhida por você. O que já existe na pasta padrão fica onde está.";

    // Confirmar a troca e a unidade de rede.
    [ObservableProperty]
    public partial string DialogTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string DialogText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string FromPath { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ToPath { get; private set; } = string.Empty;

    /// <summary>A caixa "Apagar a pasta antiga depois de conferir a cópia": desmarcada, a pasta antiga fica onde está.</summary>
    [ObservableProperty]
    public partial bool DeleteOld { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueNetworkCommand))]
    public partial bool AcceptRisk { get; set; }

    /// <summary>O botão que segue depois do aviso da unidade de rede e da confirmação: "Mover e reiniciar" na troca, "Usar esta pasta" nas escolhas sem cópia.</summary>
    public string ProceedLabel => context == Context.Settings ? "Mover e reiniciar" : "Usar esta pasta";

    // Cópia em andamento.
    /// <summary>O quanto da cópia já foi, de 0 a 1.</summary>
    [ObservableProperty]
    public partial double MoveFraction { get; private set; }

    [ObservableProperty]
    public partial string MoveText { get; private set; } = string.Empty;

    // Falha.
    [ObservableProperty]
    public partial string FailureHeadline { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string FailureDetail { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string FailureSummary { get; private set; } = string.Empty;

    /// <summary>Até onde a cópia foi quando parou, de 0 a 1; nulo quando a falha veio antes de copiar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailureBar), nameof(FailureFraction))]
    public partial double? FailurePercent { get; private set; }

    public bool HasFailureBar => FailurePercent is not null;

    public double FailureFraction => (FailurePercent ?? 0) / 100;

    [ObservableProperty]
    public partial string FailureWhere { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanUseAsIs { get; private set; }

    // Pasta que sumiu.
    [ObservableProperty]
    public partial string MissingWarning { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string RetryNote { get; private set; } = string.Empty;

    /// <summary>Abre a pergunta da primeira abertura com as duas pastas sugeridas.</summary>
    public void ShowFirstRun()
    {
        context = Context.FirstRun;
        DraftConfig = folders.Config.Path;
        DraftCache = folders.Cache.Path;
        SetFirstRunText();
        Mode = FolderDialogMode.FirstRun;
    }

    private void SetFirstRunText()
    {
        DialogTitle = "Onde guardar os seus dados?";
        DialogText = "O VNForge guarda a lista de jogos, as fichas, a memória de tradução e as chaves de acesso numa pasta. Você pode trocar depois, em Ajustes.";
    }

    /// <summary>Abre o aviso da pasta que sumiu, com as saídas: tentar de novo, escolher outra ou voltar à pasta padrão.</summary>
    public void ShowMissing(UserFolderProblem missing)
    {
        ArgumentNullException.ThrowIfNull(missing);
        context = Context.Missing;
        problem = missing;
        pending = missing.Kind;
        pendingFrom = missing.Path;
        var config = missing.Kind == UserFolderKind.Config;
        DialogTitle = config ? "Não encontrei a pasta de dados" : "Não encontrei a pasta de cache";
        DialogText = config
            ? $"O VNForge guarda os seus dados em {missing.Path}, mas essa pasta não está disponível. Conecte o disco ou a unidade e tente de novo."
            : $"O VNForge guarda o cache, as capturas e os registros em {missing.Path}, mas essa pasta não está disponível. Conecte o disco ou a unidade e tente de novo.";
        MissingWarning = config
            ? $"Se você usar a pasta padrão, o VNForge começa vazio, sem a sua lista de jogos. Os dados em {missing.Path} não são apagados."
            : $"Se você usar a pasta padrão, o cache e as capturas começam vazios. O que está em {missing.Path} não é apagado.";
        RetryNote = string.Empty;
        OnlyRetry = missing.Source == UserFolderSource.Environment || missing.PointerUnreadable;
        if (missing.PointerUnreadable)
        {
            DialogTitle = "Não consegui ler a escolha de pasta";
            DialogText = missing.Message;
        }
        else if (missing.Source == UserFolderSource.Environment)
        {
            DialogText = missing.Message;
        }

        Mode = FolderDialogMode.Missing;
    }

    /// <summary>A pasta que falta vem de uma variável de ambiente ou o próprio ponteiro não abre: trocar ou voltar ao padrão não resolve (a variável vale mais, o ponteiro está preso), só tentar de novo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMissingWarning), nameof(CanChooseOther))]
    public partial bool OnlyRetry { get; private set; }

    public bool ShowMissingWarning => IsMissing && !OnlyRetry;

    public bool CanChooseOther => !OnlyRetry;

    /// <summary>Escolhe a pasta nova de <paramref name="kind"/>, confere e pede a confirmação da troca.</summary>
    internal Task ChangeAsync(UserFolderKind kind)
    {
        context = Context.Settings;
        pending = kind;
        return PickAsync(kind);
    }

    [RelayCommand]
    private Task ChooseConfigDraftAsync()
    {
        pending = UserFolderKind.Config;
        return PickAsync(UserFolderKind.Config);
    }

    [RelayCommand]
    private Task ChooseCacheDraftAsync()
    {
        pending = UserFolderKind.Cache;
        return PickAsync(UserFolderKind.Cache);
    }

    /// <summary>"Escolher outra pasta" de qualquer diálogo.</summary>
    [RelayCommand]
    private Task ChooseOtherAsync() => PickAsync(pending);

    private async Task PickAsync(UserFolderKind kind)
    {
        pending = kind;
        var picked = await picker.PickFolderAsync(kind == UserFolderKind.Config ? "Escolha a pasta dos dados" : "Escolha a pasta do cache, das capturas e dos registros");
        if (picked is null)
        {
            BackToContext();
            return;
        }

        // A pessoa escolhe "D:\Dados" e o VNForge usa "D:\Dados\VNForge", sem misturar os arquivos dele com os dela.
        var target = string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(picked)), "VNForge", StringComparison.OrdinalIgnoreCase) ? picked : Path.Combine(picked, "VNForge");
        pendingTarget = target;
        pendingFrom = SourceOf(kind);
        try
        {
            plan = prepare(kind, pendingFrom, target, OtherFolderOf(kind));
        }
        catch (UserFolderException e)
        {
            ShowFailure(e, copied: false);
            return;
        }

        if (plan.NonLocal)
        {
            ToPath = plan.To;
            DialogTitle = "Essa pasta está numa unidade de rede";
            DialogText = $"{plan.To} parece uma unidade montada ou de rede, como as do rclone.";
            AcceptRisk = false;
            OnPropertyChanged(nameof(ProceedLabel));
            Mode = FolderDialogMode.Network;
            return;
        }

        Proceed();
    }

    /// <summary>A pasta de onde a troca parte: o padrão na primeira pergunta, a que sumiu no aviso, a em uso nos Ajustes.</summary>
    private string SourceOf(UserFolderKind kind) => context switch
    {
        Context.FirstRun => kind == UserFolderKind.Config ? folders.DefaultConfig : folders.DefaultCache,
        Context.Missing when problem is not null => problem.Path,
        _ => folders.Of(kind).Path,
    };

    /// <summary>A outra pasta do usuário, contra a qual a nova não pode se sobrepor: o rascunho da primeira pergunta, ou a em uso.</summary>
    private string OtherFolderOf(UserFolderKind kind) => context == Context.FirstRun
        ? (kind == UserFolderKind.Config ? DraftCache : DraftConfig)
        : folders.Of(kind == UserFolderKind.Config ? UserFolderKind.Cache : UserFolderKind.Config).Path;

    private void Proceed()
    {
        switch (context)
        {
            case Context.FirstRun:
                SetFirstRunText();
                if (pending == UserFolderKind.Config)
                {
                    DraftConfig = pendingTarget;
                }
                else
                {
                    DraftCache = pendingTarget;
                }

                Mode = FolderDialogMode.FirstRun;
                break;
            case Context.Missing:
                AdoptAndRestart();
                break;
            default:
                ShowConfirm();
                break;
        }
    }

    private void ShowConfirm()
    {
        var chosen = plan!;
        var config = chosen.Kind == UserFolderKind.Config;
        FromPath = chosen.From;
        ToPath = chosen.To;
        DeleteOld = false;
        DialogTitle = config ? $"Mover os dados para {chosen.To}?" : $"Mover o cache, as capturas e os registros para {chosen.To}?";
        DialogText = $"O VNForge copia {chosen.FileCount} {(chosen.FileCount == 1 ? "arquivo" : "arquivos")} ({UserFolderMover.FormatSize(chosen.Bytes)}), confere a cópia e reinicia usando a pasta nova. Salve o que estiver aberto antes.";
        OnPropertyChanged(nameof(ProceedLabel));
        Mode = FolderDialogMode.Confirm;
    }

    [RelayCommand(CanExecute = nameof(AcceptRisk))]
    private void ContinueNetwork()
    {
        if (context == Context.Settings)
        {
            ShowConfirm();
            return;
        }

        Proceed();
    }

    /// <summary>"Mover e reiniciar": copia por <see cref="UserFolderMover.Move"/> fora da thread da tela e reinicia o app quando termina.</summary>
    [RelayCommand]
    private async Task ConfirmMoveAsync()
    {
        var chosen = plan!;
        var deleteOld = DeleteOld;
        moving = new CancellationTokenSource();
        var token = moving.Token;
        MoveFraction = 0;
        MoveText = "Copiando os arquivos…";
        Mode = FolderDialogMode.Moving;
        try
        {
            // O aviso de "não consegui apagar tudo da pasta antiga" não tem onde aparecer: o app reinicia logo em seguida, e a troca já valeu.
            _ = await Task.Run(
                () => move(
                    folders,
                    chosen,
                    deleteOld,
                    step => post(() =>
                    {
                        MoveFraction = step.Percent / 100.0;
                        MoveText = $"Copiando {step.Folder}…";
                    }),
                    token),
                CancellationToken.None);
        }
        catch (UserFolderException e)
        {
            ShowFailure(e, copied: true);
            return;
        }
        catch (OperationCanceledException)
        {
            Mode = FolderDialogMode.None;
            return;
        }
        finally
        {
            moving.Dispose();
            moving = null;
        }

        restart();
    }

    [RelayCommand]
    private void UseAsIs()
    {
        if (context == Context.FirstRun)
        {
            Proceed();
            return;
        }

        AdoptAndRestart();
    }

    private void AdoptAndRestart()
    {
        try
        {
            UserFolderMover.Adopt(folders, pending, pendingTarget);
        }
        catch (UserFolderException e)
        {
            ShowFailure(e, copied: false);
            return;
        }

        Mode = FolderDialogMode.None;
        restart();
    }

    private void ShowFailure(UserFolderException error, bool copied)
    {
        FailureHeadline = error.Headline;
        FailureDetail = error.Detail;
        DialogTitle = context == Context.Settings ? "Não deu para mover os dados" : "Não deu para usar essa pasta";
        FailureSummary = context switch
        {
            Context.Settings when copied => $"Nada mudou. O VNForge continua usando {pendingFrom}, e o que já tinha sido copiado para {pendingTarget} foi removido.",
            Context.Settings => $"Nada mudou. O VNForge continua usando {pendingFrom}.",
            Context.FirstRun => "Nada foi gravado. Escolha outra pasta ou continue com a sugestão do Windows.",
            _ => $"Nada mudou. Os dados em {pendingFrom} não foram apagados.",
        };
        FailurePercent = error.Percent;
        FailureWhere = error.Percent is { } percent && error.Folder is { } folder
            ? $"Parou aos {percent}%, copiando {(Path.HasExtension(folder) ? "o arquivo" : "a pasta")} {folder}."
            : string.Empty;
        CanUseAsIs = error.DestinationHasData;
        Mode = FolderDialogMode.Failed;
    }

    /// <summary>"Fechar" na falha e o cancelamento do seletor: volta ao diálogo de onde a pessoa veio, ou fecha.</summary>
    [RelayCommand]
    private void BackToContext()
    {
        if (context == Context.Missing && problem is not null)
        {
            ShowMissing(problem);
            return;
        }

        if (context == Context.FirstRun)
        {
            SetFirstRunText();
        }

        Mode = context == Context.FirstRun ? FolderDialogMode.FirstRun : FolderDialogMode.None;
    }

    /// <summary>"Cancelar" na confirmação e no aviso da unidade de rede.</summary>
    [RelayCommand]
    private void CancelDialog() => BackToContext();

    /// <summary>"Continuar": grava as duas pastas que a pessoa viu. Se elas são as que o app já usa, segue; senão reinicia para abrir a partir das novas.</summary>
    [RelayCommand]
    private void Continue()
    {
        try
        {
            UserFolderMover.Choose(folders, DraftConfig, DraftCache);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            pending = UserFolderKind.Config;
            pendingFrom = folders.Config.Path;
            ShowFailure(new UserFolderException($"Não consegui gravar a escolha em {folders.PointerPath}. Tente de novo ou escolha outra pasta.", innerException: e), copied: false);
            return;
        }

        Mode = FolderDialogMode.None;
        var changed = !string.Equals(DraftConfig, folders.Config.Path, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(DraftCache, folders.Cache.Path, StringComparison.OrdinalIgnoreCase);
        if (changed)
        {
            restart();
            return;
        }

        Continued?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>"Usar a pasta padrão" na pasta que sumiu: grava o caminho padrão dessa pasta e reinicia. O que está na pasta que sumiu não é apagado.</summary>
    [RelayCommand]
    private void UseDefault()
    {
        if (problem is null)
        {
            return;
        }

        var path = problem.Kind == UserFolderKind.Config ? folders.DefaultConfig : folders.DefaultCache;
        try
        {
            UserFolderMover.Choose(folders, problem.Kind, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            RetryNote = $"Não consegui gravar a escolha em {folders.PointerPath}. Tente de novo.";
            return;
        }

        Mode = FolderDialogMode.None;
        restart();
    }

    /// <summary>"Tentar de novo" na pasta que sumiu: se a unidade voltou, reinicia para abrir com os dados; senão avisa que ainda não está lá.</summary>
    [RelayCommand]
    private void Retry()
    {
        if (resolve().Problem is null)
        {
            Mode = FolderDialogMode.None;
            restart();
            return;
        }

        RetryNote = "A pasta ainda não está disponível.";
    }

    private void Dismiss()
    {
        switch (Mode)
        {
            case FolderDialogMode.None:
                break;
            case FolderDialogMode.FirstRun:
                Continue();
                break;
            case FolderDialogMode.Missing:
                // Sem a pasta o app não pode seguir: o diálogo volta no próximo ciclo da tela.
                post(() => OnPropertyChanged(nameof(IsDialogOpen)));
                break;
            case FolderDialogMode.Moving:
                moving?.Cancel();
                break;
            default:
                BackToContext();
                break;
        }
    }
}
