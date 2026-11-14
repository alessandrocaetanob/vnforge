using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
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

    /// <summary>No lugar de "Traduzir este jogo": uma engine que o VNForge não traduz só guarda o jogo e a ficha na Biblioteca, e o cabeçalho diz isso em vez de calar.</summary>
    public bool ShowUnsupportedEngine => InProjectlessGame && !CanTranslateProjectlessGame;

    /// <summary>O projeto foi ligado ao jogo da Biblioteca: o aviso sobe e, quando o pedido era abrir o projeto ou o jogo estava na Ficha sem projeto, o projeto abre.</summary>
    private void OnProjectLinked(LibraryGame game)
    {
        Toasts.Show($"{game.Title} ligado ao projeto de tradução.", ToastKind.Success, key: "library-link");
        if (openAfterLink is { } root && SameFolder(game.ProjectPath, root))
        {
            openAfterLink = null;
            ProjectOpening = OpenLibraryProjectAsync(root, game.VndbId, game.Title);
        }
        else if (projectless?.Id == game.Id && game.ProjectPath is { } linked)
        {
            ProjectOpening = OpenLinkedFromSheetAsync(game, linked);
        }
    }

    /// <summary>
    /// Depois de "Ligar a um projeto que já existe" na Ficha sem projeto, abre o projeto ligado: o <see cref="SetProject"/> limpa o jogo sem projeto, e a Ficha, o cabeçalho e as abas passam a ser os do projeto.
    /// </summary>
    private async Task OpenLinkedFromSheetAsync(LibraryGame game, string root)
    {
        await OpenLibraryProjectAsync(root, game.VndbId, game.Title);
        DashboardRefresh = Dashboard.ReloadAsync();
        await DashboardRefresh;
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
        if (Sheet.IsBusy)
        {
            // A Ficha é uma só no app: abrir outro jogo agora mostraria o andamento da tradução de um no outro.
            Toasts.Show("A ficha de outro jogo ainda está sendo traduzida. Espere terminar para abrir este.", ToastKind.Info, key: "game-sheet");
            return;
        }

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
