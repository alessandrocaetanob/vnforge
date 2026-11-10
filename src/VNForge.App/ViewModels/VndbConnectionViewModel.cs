using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.ViewModels;

/// <summary>Um campo que mudou nos dois lados, como a tabela do diálogo mostra: o valor daqui, o do VNDB e quem vale (o lado mais recente).</summary>
public sealed record VndbConflictRow(string GameTitle, string Field, string Local, string Remote, bool LocalWins)
{
    public bool RemoteWins => !LocalWins;
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
    private LibrarySyncPlan? pending;
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
        catch (Exception e) when (e is VndbException or HttpRequestException)
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
        catch (Exception e) when (e is VndbException or HttpRequestException)
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
            var conflicts = plan.Items.SelectMany(i => i.Conflicts.Select(c => new VndbConflictRow(i.Game.Title, c.Field, c.Local, c.Remote, c.Winner == LibrarySyncWinner.Local))).ToList();
            if (conflicts.Count == 0)
            {
                await ApplyAsync(plan, token);
                return;
            }

            pending = plan;
            Conflicts = conflicts;
            OnPropertyChanged(nameof(Conflicts));
            var titles = conflicts.Select(c => c.GameTitle).Distinct().ToList();
            ConflictsIntro = titles.Count == 1
                ? $"{titles[0]} mudou aqui e no VNDB desde a última sincronização. Vale o lado mais recente de cada campo."
                : $"{titles.Count} jogos mudaram aqui e no VNDB desde a última sincronização. Vale o lado mais recente de cada campo.";
            IsConflictsOpen = true;
        }
        catch (Exception e) when (e is VndbException or LibraryException or HttpRequestException)
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
        var plan = pending;
        IsConflictsOpen = false;
        pending = null;
        if (plan is null || service.Token().Token is not { } token)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await ApplyAsync(plan, token);
        }
        catch (Exception e) when (e is VndbException or LibraryException or HttpRequestException)
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
        pending = null;
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

    private static string? Count(int n, string word) => n == 0 ? null : n == 1 ? $"1 {word}" : $"{n} {word}s";

    private void ShowToken()
    {
        var resolved = service.Token();
        HasToken = resolved.Token is not null;
        IsFromEnvironment = resolved.Source == VndbTokenSource.Environment;
        TokenMask = resolved.Token is { Length: >= 8 } token ? "••••-••••-••••-" + token[^4..] : HasToken ? "••••-••••" : "Nenhum token guardado";
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

    private void ShowLastSync()
    {
        var (games, last) = service.Summary();
        var parts = new List<string>();
        if (last is { } at)
        {
            var local = TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone);
            var today = clock.GetLocalNow().Date;
            parts.Add("Última vez " + (local.Date == today ? "hoje" : local.Date == today.AddDays(-1) ? "ontem" : "em " + local.ToString("dd/MM/yyyy", Portuguese)) + " às " + local.ToString("HH:mm", Portuguese));
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
