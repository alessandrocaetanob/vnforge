using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Library;
using VNForge.Core.Vndb;

namespace VNForge.App.ViewModels;

/// <summary>Um rótulo da lista do VNDB como chip da gaveta: ligado ou desligado neste jogo.</summary>
public sealed record VndbLabelChip(int Id, string Text, bool IsOn);

/// <summary>
/// O cartão "Na sua lista do VNDB" da gaveta do jogo: rótulos, voto, datas e notas que o app guarda do jogo, e o botão que o marca como terminado. Mudar um rótulo ou as notas
/// grava só aqui; "Marcar como terminado" também envia ao VNDB quando há token.
/// </summary>
public sealed partial class LibraryVndbListViewModel : ObservableObject
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Os rótulos do quadro, na ordem dele.</summary>
    private static readonly (int Id, string Text)[] Shown =
    [
        (LibraryListLabels.Playing, "Jogando"),
        (LibraryListLabels.Finished, "Terminado"),
        (LibraryListLabels.Dropped, "Abandonado"),
        (LibraryListLabels.Wishlist, "Quero jogar"),
    ];

    private readonly VndbListService service;
    private readonly LibraryGame game;
    private readonly TimeProvider clock;
    private readonly string notes;

    public LibraryVndbListViewModel(LibraryGame game, VndbListService service, LibraryViewModel library, TimeProvider? clock = null)
    {
        this.game = game;
        this.service = service;
        Library = library;
        this.clock = clock ?? TimeProvider.System;
        var state = game.CurrentList;
        var fields = state?.Fields ?? LibraryListFields.Empty;
        Labels = [.. Shown.Select(s => new VndbLabelChip(s.Id, s.Text, fields.HasLabel(s.Id)))];
        VoteText = fields.Vote is { } vote ? $"{LibraryListRules.FormatVote(vote, Portuguese)} de 10" : "sem voto";
        StartedText = fields.Started is { } started ? started.ToString("dd/MM/yyyy", Portuguese) : "ainda não começou";
        HasStarted = fields.Started is not null;
        FinishedText = fields.Finished is { } finished ? finished.ToString("dd/MM/yyyy", Portuguese) : "ainda não terminou";
        HasFinished = fields.Finished is not null;
        notes = fields.Notes ?? string.Empty;
        NotesText = notes;
        CanMarkFinished = !fields.HasLabel(LibraryListLabels.Finished);
        (SyncText, SyncTone) = state switch
        {
            null => ("Ainda não sincronizada", "pend"),
            { Pending: true } => ("Mudança só aqui", "pend"),
            { SyncedAt: { } at } => ("Sincronizada " + Age(at), "ok"),
            _ => ("Ainda não sincronizada", "pend"),
        };
    }

    public IReadOnlyList<VndbLabelChip> Labels { get; }

    public string VoteText { get; }

    public string StartedText { get; }

    public bool HasStarted { get; }

    public string FinishedText { get; }

    public bool HasFinished { get; }

    public string SyncText { get; }

    /// <summary>"ok" quando a lista está em dia com o VNDB, "pend" quando falta enviar ou sincronizar (o <c>Tag</c> do selo da Biblioteca).</summary>
    public string SyncTone { get; }

    public bool CanMarkFinished { get; }

    public string VndbUrl => "https://vndb.org/" + game.VndbId;

    [ObservableProperty]
    public partial string NotesText { get; set; }

    /// <summary>A Biblioteca guarda a mensagem do último envio, porque a gaveta é refeita a cada mudança.</summary>
    public LibraryViewModel Library { get; }

    /// <summary>As notas só gravam quando a pessoa sai do campo e o texto mudou.</summary>
    [RelayCommand]
    private async Task CommitNotesAsync()
    {
        var text = NotesText.Trim();
        if (text != notes)
        {
            await RunAsync(() =>
            {
                service.Edit(game.Id, new VndbListUpdate { Notes = text.Length == 0 ? VndbChange<string>.Clear : text });
                return Task.FromResult<string?>(null);
            });
        }
    }

    [RelayCommand]
    private async Task ToggleLabelAsync(VndbLabelChip? chip)
    {
        if (chip is not null)
        {
            await RunAsync(() =>
            {
                service.Edit(game.Id, chip.IsOn ? new VndbListUpdate { LabelsUnset = [chip.Id] } : new VndbListUpdate { LabelsSet = [chip.Id] });
                return Task.FromResult<string?>(null);
            });
        }
    }

    [RelayCommand]
    private Task MarkFinishedAsync() => RunAsync(() => service.MarkFinishedAsync(game.Id));

    [RelayCommand]
    private void OpenInVndb()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(VndbUrl) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Library.ShowListMessage(game.Id, "Não consegui abrir o navegador: " + e.Message);
        }
    }

    private async Task RunAsync(Func<Task<string?>> work)
    {
        string? message;
        try
        {
            message = await work();
        }
        catch (Exception e) when (e is LibraryException or ArgumentException)
        {
            Library.ShowListMessage(game.Id, e.Message);
            return;
        }

        Library.ShowListMessage(game.Id, message);
        await Library.ReloadListAsync();
    }

    private string Age(DateTimeOffset at)
    {
        var elapsed = clock.GetUtcNow() - at;
        return elapsed < TimeSpan.FromMinutes(1) ? "agora"
            : elapsed < TimeSpan.FromHours(1) ? $"há {(int)elapsed.TotalMinutes} min"
            : elapsed < TimeSpan.FromDays(1) ? $"há {(int)elapsed.TotalHours} h"
            : $"há {(int)elapsed.TotalDays} dias";
    }
}
