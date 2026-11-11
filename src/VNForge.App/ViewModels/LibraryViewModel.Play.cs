using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

/// <summary>O que a aba Jogar do jogo aberto precisa da Biblioteca: o jogo do projeto, as sessões, os interruptores por jogo e o "Jogar".</summary>
public sealed partial class LibraryViewModel
{
    // "Played" (em LibraryViewModel.Export.cs) avisa a cada leitura, e a aba Jogar relê as sessões.
    internal string? LocaleEmulatorPath => library.LocaleEmulatorPath;

    private (DateTimeOffset At, bool ViaLocaleEmulator)? playingStart;

    /// <summary>Quando a partida em curso abriu; nulo sem partida.</summary>
    internal DateTimeOffset? PlayingSince => PlayingId is null ? null : playingStart?.At;

    internal bool PlayingViaLocale => PlayingId is not null && playingStart?.ViaLocaleEmulator == true;

    private void RecordPlayStart(GameLaunch launch) => playingStart = (clock.GetUtcNow(), launch.ViaLocaleEmulator);

    internal async Task PlayGameAsync(LibraryGame game)
    {
        // O interruptor que a pessoa acabou de mexer precisa estar gravado antes de o "Jogar" ler o library.json.
        await Task.WhenAny(Remembering);
        var card = new LibraryCardViewModel(game, LibraryCards.Describe(game, library.Privacy, translations.GetValueOrDefault(game.Id)));
        if (PlayCommand.CanExecute(card))
        {
            await PlayCommand.ExecuteAsync(card);
        }
    }

    internal void RememberJapaneseLocale(string gameId, bool value) =>
        Remembering = RememberAsync(Remembering, () => editor?.SetJapaneseLocale(gameId, value));

    internal void RememberOpensLauncher(string gameId, bool value) =>
        Remembering = RememberAsync(Remembering, () => editor?.SetOpensLauncher(gameId, value));

    // Sem selecionar o cartão: quem edita aqui é a aba Jogar, e a gaveta da Biblioteca não deve abrir.
    private async Task RememberAsync(Task previous, Func<LibraryGame?> edit)
    {
        await Task.WhenAny(previous);

        // A releitura que vem depois de gravar zera o aviso: o que já estava na tela (a sessão que não gravou, um erro de outra operação) tem de ficar.
        var problem = ErrorMessage;
        if (await RunEditAsync(edit, null))
        {
            ErrorMessage ??= problem;
        }
        else
        {
            // Não gravou: a aba Jogar volta o interruptor ao valor que está no library.json.
            PlayEditRejected?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A gravação de um interruptor da aba Jogar falhou; a aba relê o jogo para mostrar o valor que ficou gravado.</summary>
    internal event EventHandler? PlayEditRejected;
}
