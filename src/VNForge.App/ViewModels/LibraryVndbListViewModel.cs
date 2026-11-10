using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Library;
using VNForge.Core.Vndb;

namespace VNForge.App.ViewModels;

/// <summary>Um rótulo da lista do VNDB como chip do cartão: ligado ou desligado neste jogo.</summary>
public sealed record VndbLabelChip(int Id, string Text, bool IsOn);

/// <summary>
/// O cartão "Na sua lista do VNDB" da Ficha do jogo: rótulos, voto, datas e notas que o app guarda do jogo. Tudo o que a pessoa muda grava no <c>library.json</c> e só vai ao VNDB
/// na sincronização, exceto o rótulo Terminado quando ela ligou "marcar como terminado no VNDB" em Ajustes.
/// </summary>
public sealed partial class LibraryVndbListViewModel : ObservableObject
{
    private const string DateFormat = "dd/MM/yyyy";

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
    private readonly TimeProvider clock;
    private LibraryGame game;

    public LibraryVndbListViewModel(LibraryGame game, VndbListService service, LibraryViewModel library, TimeProvider? clock = null)
    {
        this.game = game;
        this.service = service;
        this.clock = clock ?? TimeProvider.System;
        Library = library;
        Apply(game);
    }

    public LibraryViewModel Library { get; }

    public IReadOnlyList<VndbLabelChip> Labels { get; private set; } = [];

    public string SyncText { get; private set; } = string.Empty;

    /// <summary>"ok" quando a lista está em dia com o VNDB, "pend" quando falta enviar ou sincronizar (o <c>Tag</c> do selo da Biblioteca).</summary>
    public string SyncTone { get; private set; } = "pend";

    public bool CanMarkFinished { get; private set; }

    public string VndbUrl => "https://vndb.org/" + game.VndbId;

    /// <summary>O voto como a pessoa digita: <c>8</c> ou <c>8,5</c>, de 1 a 10; vazio apaga o voto.</summary>
    [ObservableProperty]
    public partial string VoteInput { get; set; } = string.Empty;

    /// <summary>Datas como <c>03/10/2026</c>; vazio apaga a data.</summary>
    [ObservableProperty]
    public partial string StartedInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FinishedInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NotesText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Message { get; private set; }

    /// <summary>Relê o jogo do arquivo, para quando a lista mudou por fora do cartão (a sincronização em Ajustes).</summary>
    public void Refresh()
    {
        try
        {
            Apply(service.Game(game.Id));
        }
        catch (LibraryException)
        {
            // O jogo saiu da Biblioteca: o cartão fica como estava até a Ficha fechar.
        }
    }

    /// <summary>Sair de um campo grava o que mudou: voto, datas e notas juntos, ou nada se nada mudou. Um valor que não vale fica na caixa e a mensagem diz por quê.</summary>
    [RelayCommand]
    private async Task CommitAsync()
    {
        var fields = game.CurrentList?.Fields ?? LibraryListFields.Empty;
        VndbListUpdate update;
        try
        {
            update = new VndbListUpdate
            {
                Vote = VoteChange(fields.Vote),
                Started = DateChange(StartedInput, fields.Started, "de início"),
                Finished = DateChange(FinishedInput, fields.Finished, "de fim"),
                Notes = NotesChange(fields.Notes),
            };
        }
        catch (ArgumentException e)
        {
            Say(e.Message);
            return;
        }

        if (update is { Vote.Kind: VndbChangeKind.Keep, Started.Kind: VndbChangeKind.Keep, Finished.Kind: VndbChangeKind.Keep, Notes.Kind: VndbChangeKind.Keep })
        {
            return;
        }

        await EditAsync(update);
    }

    [RelayCommand]
    private async Task ToggleLabelAsync(VndbLabelChip? chip)
    {
        if (chip is not null)
        {
            await EditAsync(chip.IsOn ? new VndbListUpdate { LabelsUnset = [chip.Id] } : new VndbListUpdate { LabelsSet = [chip.Id] });
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
            Say("Não consegui abrir o navegador: " + e.Message);
        }
    }

    private VndbChange<int> VoteChange(int? current)
    {
        var text = VoteInput.Trim();
        if (text.Length == 0)
        {
            return current is null ? default : VndbChange<int>.Clear;
        }

        var vote = LibraryListRules.ParseVote(text);
        return vote == current ? default : vote;
    }

    private static VndbChange<DateOnly> DateChange(string input, DateOnly? current, string which)
    {
        var text = input.Trim();
        if (text.Length == 0)
        {
            return current is null ? default : VndbChange<DateOnly>.Clear;
        }

        if (!DateOnly.TryParseExact(text, DateFormat, Portuguese, DateTimeStyles.None, out var date))
        {
            throw new ArgumentException($"A data {which} '{text}' não vale: use dia/mês/ano, como 03/10/2026.");
        }

        return date == current ? default : date;
    }

    private VndbChange<string> NotesChange(string? current)
    {
        var text = NotesText.Trim();
        return text == (current ?? string.Empty) ? default : text.Length == 0 ? VndbChange<string>.Clear : text;
    }

    private Task EditAsync(VndbListUpdate update) => RunAsync(() => service.EditAsync(game.Id, update));

    private async Task RunAsync(Func<Task<string?>> work)
    {
        string? message;
        try
        {
            message = await work();
        }
        catch (Exception e) when (e is LibraryException or ArgumentException)
        {
            Say(e.Message);
            return;
        }

        Say(message);
        Refresh();
        await Library.ReloadListAsync();
    }

    private void Say(string? message) => Message = message;

    private void Apply(LibraryGame current)
    {
        game = current;
        var state = current.CurrentList;
        var fields = state?.Fields ?? LibraryListFields.Empty;
        Labels = [.. Shown.Select(s => new VndbLabelChip(s.Id, s.Text, fields.HasLabel(s.Id)))];
        VoteInput = fields.Vote is { } vote ? LibraryListRules.FormatVote(vote, Portuguese) : string.Empty;
        StartedInput = fields.Started?.ToString(DateFormat, Portuguese) ?? string.Empty;
        FinishedInput = fields.Finished?.ToString(DateFormat, Portuguese) ?? string.Empty;
        NotesText = fields.Notes ?? string.Empty;
        CanMarkFinished = !fields.HasLabel(LibraryListLabels.Finished);
        (SyncText, SyncTone) = state switch
        {
            null => ("Ainda não sincronizada", "pend"),
            { Pending: true } => ("Mudança só aqui", "pend"),
            { SyncedAt: { } at } => ("Sincronizada " + Age(at), "ok"),
            _ => ("Ainda não sincronizada", "pend"),
        };
        OnPropertyChanged(nameof(Labels));
        OnPropertyChanged(nameof(SyncText));
        OnPropertyChanged(nameof(SyncTone));
        OnPropertyChanged(nameof(CanMarkFinished));
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
