using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.ViewModels;

/// <summary>Um campo que mudou nos dois lados, como a tabela do diálogo mostra: o valor daqui e o do VNDB, com a escolha de quem vale (começa no lado mais recente).</summary>
public sealed partial class VndbConflictRow(string gameId, string gameTitle, string field, string local, string remote, bool localWins) : ObservableObject
{
    public string GameId { get; } = gameId;

    public string GameTitle { get; } = gameTitle;

    public string Field { get; } = field;

    public string Local { get; } = local;

    public string Remote { get; } = remote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseRemote))]
    public partial bool UseLocal { get; set; } = localWins;

    public bool UseRemote
    {
        get => !UseLocal;
        set => UseLocal = !value;
    }
}

/// <summary>
/// A aba VNDB de Ajustes: o token pessoal (guardado no cofre do Windows, nunca em arquivo do projeto), o que ele permite e o "Sincronizar agora". Quando a sincronização
/// acha campos mudados dos dois lados, o diálogo mostra cada um com o valor que vai valer e só grava depois da confirmação.
/// </summary>
public sealed partial class VndbConnectionViewModel : ObservableObject
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");

    private readonly VndbListService service;
    private readonly TimeProvider clock;
    private string? lastRun;

    public VndbConnectionViewModel(VndbListService service, TimeProvider? clock = null)
    {
        this.service = service;
        this.clock = clock ?? TimeProvider.System;
        ShowToken();
    }

    /// <summary>A lista do VNDB mudou no arquivo da Biblioteca: quem mostra a lista deve reler.</summary>
    public event EventHandler? ListChanged;

    public IReadOnlyList<VndbConflictRow> Conflicts { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemoveToken))]
    public partial bool HasToken { get; private set; }

    [ObservableProperty]
    public partial string TokenMask { get; private set; } = "Nenhum token guardado";

    [ObservableProperty]
    public partial string TokenSourceText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemoveToken))]
    public partial bool IsFromEnvironment { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccountError))]
    public partial string AccountText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccountError))]
    public partial bool ConnectionOk { get; private set; }

    [ObservableProperty]
    public partial bool CanRead { get; private set; }

    [ObservableProperty]
    public partial bool CanWrite { get; private set; }

    [ObservableProperty]
    public partial bool IsChangingToken { get; private set; }

    [ObservableProperty]
    public partial string NewToken { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Message { get; private set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string LastSyncText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsConflictsOpen { get; set; }

    [ObservableProperty]
    public partial string ConflictsIntro { get; private set; } = string.Empty;

    /// <summary>O VNDB não respondeu ou recusou o token: o selo vermelho com o que ele disse.</summary>
    public bool HasAccountError => !ConnectionOk && AccountText.Length > 0;

    /// <summary>O que a sincronização leva (os quatro campos) e se terminar aqui marca no VNDB; cada mudança vai para <c>vndb-sync.json</c>.</summary>
    public bool SyncLabels
    {
        get => service.Choices.Labels;
        set => Choose(service.Choices with { Labels = value }, nameof(SyncLabels));
    }

    public bool SyncVote
    {
        get => service.Choices.Vote;
        set => Choose(service.Choices with { Vote = value }, nameof(SyncVote));
    }

    public bool SyncDates
    {
        get => service.Choices.Dates;
        set => Choose(service.Choices with { Dates = value }, nameof(SyncDates));
    }

    public bool SyncNotes
    {
        get => service.Choices.Notes;
        set => Choose(service.Choices with { Notes = value }, nameof(SyncNotes));
    }

    public bool MarkFinishedOnVndb
    {
        get => service.Choices.MarkFinishedOnVndb;
        set => Choose(service.Choices with { MarkFinishedOnVndb = value }, nameof(MarkFinishedOnVndb));
    }

    private void Choose(VndbSyncChoices choices, string property)
    {
        try
        {
            service.Choices = choices;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A escolha vale até o app fechar; o erro diz por que não foi para o disco.
            Message = "A escolha não foi guardada no disco: " + e.Message;
        }

        OnPropertyChanged(property);
    }

    public bool CanRemoveToken => HasToken && !IsFromEnvironment;

    public string ChangeTokenLabel => HasToken ? "Trocar token" : "Guardar um token";

    /// <summary>Relê o token e pergunta ao VNDB de quem ele é e o que permite. Sem token, só limpa o estado; um erro da rede ou do token vira texto, não exceção.</summary>
    public async Task RefreshAsync()
    {
        ShowToken();
        if (service.Token().Token is not { } token)
        {
            return;
        }

        try
        {
            ShowAccount(await service.CheckAsync(token));
        }
        catch (Exception e) when (e is VndbException or HttpRequestException or TaskCanceledException)
        {
            ConnectionOk = false;
            AccountText = e.Message;
        }
    }

    [RelayCommand]
    private void ChangeToken()
    {
        NewToken = string.Empty;
        Message = null;
        IsChangingToken = true;
    }

    [RelayCommand]
    private void CancelChangeToken()
    {
        NewToken = string.Empty;
        IsChangingToken = false;
    }

    [RelayCommand]
    private async Task SaveTokenAsync()
    {
        var token = NewToken.Trim();
        if (token.Length == 0)
        {
            Message = "Cole o token que você gerou em vndb.org/u/tokens.";
            return;
        }

        IsBusy = true;
        try
        {
            var account = await service.SaveTokenAsync(token);
            NewToken = string.Empty;
            IsChangingToken = false;
            Message = null;
            ShowToken();
            ShowAccount(account);
        }
        catch (Exception e) when (e is VndbException or HttpRequestException or TaskCanceledException)
        {
            // O token nunca entra na mensagem: só o que o VNDB respondeu.
            Message = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void RemoveToken()
    {
        Message = service.RemoveToken() ? "Token apagado do cofre do Windows." : "Não havia token no cofre do Windows.";
        ShowToken();
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (service.Token().Token is not { } token)
        {
            Message = "Guarde o token do VNDB antes de sincronizar.";
            return;
        }

        IsBusy = true;
        Message = null;
        try
        {
            var plan = await service.PlanAsync(token, LibrarySyncDirection.Both);
            var conflicts = plan.Items.SelectMany(i => i.Conflicts.Select(c => new VndbConflictRow(i.Game.Id, i.Game.Title, c.Field, c.Local, c.Remote, c.Winner == LibrarySyncWinner.Local))).ToList();
            if (conflicts.Count == 0)
            {
                await ApplyAsync(plan, token);
                return;
            }

            Conflicts = conflicts;
            OnPropertyChanged(nameof(Conflicts));
            var titles = conflicts.Select(c => c.GameTitle).Distinct().ToList();
            ConflictsIntro = titles.Count == 1
                ? $"{titles[0]} foi alterado aqui e no site do VNDB desde a última sincronização. Escolha o que vale para cada campo."
                : $"{titles.Count} jogos foram alterados aqui e no site do VNDB desde a última sincronização. Escolha o que vale para cada campo.";
            IsConflictsOpen = true;
        }
        catch (Exception e) when (e is VndbException or LibraryException or HttpRequestException or TaskCanceledException)
        {
            Message = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyConflictsAsync()
    {
        var rows = Conflicts;
        IsConflictsOpen = false;
        if (service.Token().Token is not { } token)
        {
            return;
        }

        IsBusy = true;
        try
        {
            // A escolha vai num novo plano: o VNDB pode ter mudado desde o primeiro, e o que a pessoa não decidiu segue a regra de sempre.
            var chosen = rows.ToDictionary(r => (r.GameId, r.Field), r => r.UseLocal ? LibrarySyncWinner.Local : LibrarySyncWinner.Remote);
            var plan = await service.PlanAsync(token, LibrarySyncDirection.Both, resolutions: chosen);
            await ApplyAsync(plan, token);
        }
        catch (Exception e) when (e is VndbException or LibraryException or HttpRequestException or TaskCanceledException)
        {
            Message = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void DecideLater()
    {
        IsConflictsOpen = false;
        Message = "Nada foi gravado, nem aqui nem no VNDB.";
    }

    private async Task ApplyAsync(LibrarySyncPlan plan, string token)
    {
        var report = await service.ApplyAsync(plan, token);
        var failed = report.Items.Count(i => i.Action == LibrarySyncAction.Failed);
        var pulled = report.Items.Count(i => i.Action is LibrarySyncAction.Pulled or LibrarySyncAction.Merged);
        var pushed = report.Items.Count(i => i.Action is LibrarySyncAction.Pushed or LibrarySyncAction.Merged);
        lastRun = pulled + pushed == 0 && failed == 0
            ? "nenhuma diferença"
            : string.Join(", ", new[] { Count(pulled, "recebido"), Count(pushed, "enviado"), Count(failed, "com erro") }.Where(t => t is not null));
        Message = report.AbortReason ?? report.Items.FirstOrDefault(i => i.Error is not null)?.Error;
        ShowLastSync();
        ListChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string? Count(int n, string word)
    {
        if (n == 0)
        {
            return null;
        }

        return n == 1 ? $"1 {word}" : $"{n} {word}s";
    }

    private string MaskOf(string? token)
    {
        if (token is { Length: >= 8 })
        {
            return "••••-••••-••••-" + token[^4..];
        }

        return HasToken ? "••••-••••" : "Nenhum token guardado";
    }

    private void ShowToken()
    {
        var resolved = service.Token();
        HasToken = resolved.Token is not null;
        IsFromEnvironment = resolved.Source == VndbTokenSource.Environment;
        TokenMask = MaskOf(resolved.Token);
        TokenSourceText = resolved.Source switch
        {
            VndbTokenSource.Environment => $"O token vem da variável de ambiente {VndbTokenResolver.EnvironmentVariable}, que vale mais que o cofre.",
            VndbTokenSource.Vault => "O token fica no Cofre do Windows, como a chave da Anthropic. Ele nunca vai para a pasta de um projeto.",
            _ => resolved.Notice ?? "Gere um token em vndb.org/u/tokens, com a leitura e a escrita da lista, e guarde aqui. Ele fica no Cofre do Windows, nunca na pasta de um projeto.",
        };
        if (!HasToken)
        {
            ConnectionOk = false;
            AccountText = string.Empty;
            CanRead = false;
            CanWrite = false;
        }

        OnPropertyChanged(nameof(ChangeTokenLabel));
        ShowLastSync();
    }

    private static string DayText(DateTime day, DateTime today)
    {
        if (day == today)
        {
            return "hoje";
        }

        return day == today.AddDays(-1) ? "ontem" : "em " + day.ToString("dd/MM/yyyy", Portuguese);
    }

    private void ShowLastSync()
    {
        int games;
        DateTimeOffset? last;
        try
        {
            (games, last) = service.Summary();
        }
        catch (LibraryException e)
        {
            LastSyncText = string.Empty;
            Message = e.Message;
            return;
        }

        var parts = new List<string>();
        if (last is { } at)
        {
            var local = TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone);
            var today = clock.GetLocalNow().Date;
            parts.Add("Última vez " + DayText(local.Date, today) + " às " + local.ToString("HH:mm", Portuguese));
        }
        else
        {
            parts.Add("Ainda não sincronizou");
        }

        parts.Add(games == 1 ? "1 jogo na lista" : $"{games} jogos na lista");
        if (lastRun is not null)
        {
            parts.Add(lastRun);
        }

        LastSyncText = string.Join(" · ", parts);
    }

    private void ShowAccount(VndbAuthInfo account)
    {
        ConnectionOk = true;
        AccountText = "Conectado como " + account.Username;
        CanRead = account.Permissions.Contains("listread");
        CanWrite = account.Permissions.Contains("listwrite");
    }
}
