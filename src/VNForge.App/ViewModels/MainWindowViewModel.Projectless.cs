using CommunityToolkit.Mvvm.Input;
using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

/// <summary>A Ficha de um jogo da Biblioteca que ainda não tem projeto de tradução (#690).</summary>
public sealed partial class MainWindowViewModel
{
    private LibraryGame? projectless;
    private LibraryGameContext? projectlessContext;

    /// <summary>O jogo da Biblioteca cuja Ficha está aberta sem projeto; nulo quando a Ficha é a de um projeto ou não há Ficha aberta.</summary>
    public LibraryGame? ProjectlessGame => InProjectlessGame ? projectless : null;

    /// <summary>A Ficha aberta é a de um jogo sem projeto: o cabeçalho mostra "Sem projeto" e as ações do jogo, e a aba Ficha é a única.</summary>
    public bool InProjectlessGame => projectless is not null && selectedNav?.Key == NavKey.Sheet;

    /// <summary>O nome do jogo no cabeçalho: o do jogo da Biblioteca sem projeto, ou o do projeto aberto.</summary>
    public string GameHeaderTitle => projectlessContext is { } context && InProjectlessGame ? context.Title : ProjectTitle;

    /// <summary>"Traduzir este jogo" do cabeçalho só existe para uma engine que o VNForge traduz; as outras só guardam o jogo e a ficha na Biblioteca.</summary>
    public bool CanTranslateProjectlessGame => ProjectlessGame is { Engine.Translatable: true };

    /// <summary>"Ligar a um projeto que já existe" do cabeçalho da Ficha: o fluxo é o da #688, que liga este botão depois de entrar. Enquanto isso o botão fica escondido.</summary>
    public bool ShowLinkExistingProject => false;

    [RelayCommand]
    private void LinkExistingProjectFromSheet()
    {
        // Sem efeito até a #688 entrar: o botão está escondido (ShowLinkExistingProject) e o coordenador o liga ao LinkExistingProjectCommand dela.
    }

    /// <summary>"Traduzir este jogo": abre o assistente de novo projeto com a pasta do jogo e o que a Biblioteca já sabe dele.</summary>
    [RelayCommand(CanExecute = nameof(CanTranslateProjectlessGame))]
    private void TranslateProjectlessGame()
    {
        if (projectless is { } game && projectlessContext is { } context)
        {
            ShowWizard(NewProjectWizard, game.Folder, context);
        }
    }

    private void OpenProjectlessSheet(LibraryGame game)
    {
        var context = LibraryContext(game);
        projectless = game;
        projectlessContext = context;
        GameOriginalTitle = context.OriginalTitle ?? string.Empty;
        GameCover = context.Cover;
        Sheet.Open(game.VndbId, context.Title);
        Sheet.ListCard = Library.ListCardForProject(null, game.VndbId);
        OnPropertyChanged(nameof(GameCoverRevealed));
        sheetItem.IsEnabled = true;
        SelectedNav = sheetItem;
        TranslateProjectlessGameCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Sai da Ficha sem projeto: o cabeçalho, a capa e a Ficha voltam a ser os do projeto aberto, se há um, e a aba Ficha volta a pedir um projeto. O <see cref="Sheet"/> é um só no app,
    /// então a Ficha do projeto se relê do disco.
    /// </summary>
    private void LeaveProjectlessSheet()
    {
        projectless = null;
        projectlessContext = null;
        if (CurrentProject is { } project)
        {
            ReadVndbLink(project.Root);
        }
        else
        {
            GameOriginalTitle = string.Empty;
            GameCover = null;
            Sheet.Open(null, string.Empty);
            sheetItem.IsEnabled = false;
        }

        TranslateProjectlessGameCommand.NotifyCanExecuteChanged();
    }
}
