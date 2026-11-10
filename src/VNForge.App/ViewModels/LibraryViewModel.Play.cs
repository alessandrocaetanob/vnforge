using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

/// <summary>O que a aba Jogar do jogo aberto precisa da Biblioteca: o jogo do projeto, as sessões, os interruptores por jogo e o "Jogar".</summary>
public sealed partial class LibraryViewModel
{
    // "Played" (em LibraryViewModel.Export.cs) avisa a cada leitura, e a aba Jogar relê as sessões.
    internal string? LocaleEmulatorPath => library.LocaleEmulatorPath;

    internal Task PlayGameAsync(LibraryGame game)
    {
        var card = new LibraryCardViewModel(game, LibraryCards.Describe(game, library.Privacy, translations.GetValueOrDefault(game.Id)));
        return PlayCommand.CanExecute(card) ? PlayCommand.ExecuteAsync(card) : Task.CompletedTask;
    }

    internal void RememberJapaneseLocale(string gameId, bool value) =>
        Remembering = RememberAsync(Remembering, () => editor?.SetJapaneseLocale(gameId, value));

    internal void RememberOpensLauncher(string gameId, bool value) =>
        Remembering = RememberAsync(Remembering, () => editor?.SetOpensLauncher(gameId, value));

    // Sem selecionar o cartão: quem edita aqui é a aba Jogar, e a gaveta da Biblioteca não deve abrir.
    private async Task RememberAsync(Task previous, Func<LibraryGame?> edit)
    {
        await Task.WhenAny(previous);
        await RunEditAsync(edit, null);
    }
}
