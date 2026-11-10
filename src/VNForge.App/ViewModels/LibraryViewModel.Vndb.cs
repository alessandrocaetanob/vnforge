using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.App.Services;
using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

public sealed partial class LibraryViewModel
{
    private string? listMessageGame;

    /// <summary>A lista do VNDB (token, leitura, gravação e envio). Sem ela a gaveta não mostra o cartão da lista.</summary>
    public VndbListService? ListService { get; set; }

    /// <summary>O que dizer sobre o último envio ou edição da lista do jogo aberto na gaveta (um erro, ou "gravado só aqui").</summary>
    [ObservableProperty]
    public partial string? ListMessage { get; private set; }

    /// <summary>O cartão da lista para o jogo da gaveta: só existe com o serviço ligado e o jogo confirmado no VNDB.</summary>
    internal LibraryVndbListViewModel? ListCardFor(LibraryGame game)
    {
        if (ListService is null || !game.IsConfirmed)
        {
            return null;
        }

        if (game.Id != listMessageGame)
        {
            ListMessage = null;
        }

        return new LibraryVndbListViewModel(game, ListService, this, clock);
    }

    internal void ShowListMessage(string gameId, string? message)
    {
        listMessageGame = gameId;
        ListMessage = message;
    }

    /// <summary>A lista mudou no arquivo (edição ou sincronização): relê a Biblioteca e refaz a gaveta com o que ficou lá.</summary>
    public Task ReloadListAsync() => LoadCoreAsync();
}
