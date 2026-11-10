using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core;
using VNForge.Core.Library;
using VNForge.Core.Vndb;

namespace VNForge.App.ViewModels;

/// <summary>Uma razão do casamento com o tom da pílula: <c>ok</c> para a que concorda com o jogo, <c>pend</c> para a fraca.</summary>
public sealed record LibraryMatchBadgeItem(string Text, string Tone);

/// <summary>Um candidato do diálogo do casamento. <see cref="IsSelected"/> é o que o botão de rádio mostra.</summary>
public sealed partial class LibraryMatchChoiceViewModel(LibraryMatchChoice choice) : ObservableObject
{
    public LibraryMatchChoice Choice { get; } = choice;

    public string VndbId => Choice.VndbId;

    public string Title => Choice.Title;

    public string? OriginalTitle => Choice.OriginalTitle;

    public bool HasOriginalTitle => !string.IsNullOrEmpty(Choice.OriginalTitle);

    public string Details => Choice.Details;

    public bool HasDetails => Choice.Details.Length > 0;

    public VndbImageRef? Image => Choice.Image;

    public IReadOnlyList<LibraryMatchBadgeItem> Badges { get; } = [.. choice.Badges.Select(b => new LibraryMatchBadgeItem(b.Text, b.Strong ? "ok" : "pend"))];

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// O diálogo "Qual é este jogo no VNDB?": os candidatos que o casamento achou para um jogo, o que fazer com eles (confirmar o escolhido, dizer que nenhum serve, procurar de novo) e
/// "Depois". Confirmar leva ao próximo jogo que espera confirmação, um por vez; sem mais nenhum, o diálogo fecha.
/// </summary>
public sealed partial class LibraryMatchViewModel(LibraryViewModel library) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Intro), nameof(FolderText))]
    public partial LibraryGame? Game { get; private set; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand), nameof(NoneOfThemCommand), nameof(SearchCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    public ObservableCollection<LibraryMatchChoiceViewModel> Choices { get; } = [];

    public LibraryMatchChoiceViewModel? Selected => Choices.FirstOrDefault(c => c.IsSelected);

    public string Title => "Qual é este jogo no VNDB?";

    public string FolderText => Game?.Folder ?? string.Empty;

    public string Intro => Choices.Count switch
    {
        0 => "O VNForge não achou nenhum candidato para a pasta",
        1 => "O VNForge achou 1 candidato para a pasta",
        var n => $"O VNForge achou {n} candidatos para a pasta",
    };

    public string IntroEnd => Choices.Count == 0 ? ". Procure no VNDB ou deixe o jogo sem capa." : ". Confirme o certo para trazer capa, sinopse e personagens.";

    /// <summary>Abre o diálogo para <paramref name="game"/>, com o primeiro candidato escolhido.</summary>
    public void Open(LibraryGame game)
    {
        Game = game;
        ErrorMessage = null;
        Choices.Clear();
        foreach (var choice in LibraryMatchChoices.Describe(game))
        {
            Choices.Add(new LibraryMatchChoiceViewModel(choice));
        }

        Select(Choices.FirstOrDefault());
        OnPropertyChanged(nameof(Intro));
        OnPropertyChanged(nameof(IntroEnd));
        ConfirmCommand.NotifyCanExecuteChanged();
        IsOpen = true;
    }

    /// <summary>Marca o candidato como o escolhido; os outros desmarcam.</summary>
    public void Select(LibraryMatchChoiceViewModel? choice)
    {
        foreach (var other in Choices)
        {
            other.IsSelected = ReferenceEquals(other, choice);
        }

        OnPropertyChanged(nameof(Selected));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectChoice(LibraryMatchChoiceViewModel? choice) => Select(choice);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private Task ConfirmAsync() => RunAsync(async game => await library.ConfirmMatchAsync(game.Id, Selected!.VndbId), next: true);

    private bool CanConfirm() => Selected is not null && !IsBusy && Game is not null;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task NoneOfThemAsync() => RunAsync(game => library.DismissCandidatesAsync(game.Id), next: true);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task SearchAsync()
    {
        if (Game is not { } game)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var found = await library.SearchAgainAsync(game.Id);
            if (found is not null)
            {
                Open(found);
            }
        }
        catch (Exception e) when (e is VNForgeException or HttpRequestException)
        {
            ErrorMessage = $"Não foi possível procurar no VNDB: {e.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAct() => !IsBusy && Game is not null;

    /// <summary>"Depois": fecha sem mudar nada. O jogo continua com o aviso na grade.</summary>
    [RelayCommand]
    private void Later() => IsOpen = false;

    private async Task RunAsync(Func<LibraryGame, Task> action, bool next)
    {
        if (Game is not { } game)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await action(game);
            IsOpen = false;
            if (next)
            {
                library.OpenNextConfirmation(game.Id);
            }
        }
        catch (Exception e) when (e is VNForgeException or HttpRequestException)
        {
            ErrorMessage = $"Não foi possível confirmar no VNDB: {e.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
