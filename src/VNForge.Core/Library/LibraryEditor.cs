namespace VNForge.Core.Library;

/// <summary>
/// O que a pessoa faz com um jogo da Biblioteca pela gaveta e pelo diálogo do casamento: confirmar o jogo do VNDB, procurar de novo, dizer que nenhum candidato serve, desfazer a
/// confirmação ou tirar o jogo da lista. Cada operação grava o <c>library.json</c> sob a trava e devolve o jogo como ficou. Nada aqui toca a pasta do jogo.
/// </summary>
public sealed class LibraryEditor(LibraryFile file, Func<LibraryMatcher> matcherFactory)
{
    /// <summary>Confirma <paramref name="vndbId"/> (um dos candidatos, ou outro que a pessoa digitou) para o jogo.</summary>
    /// <exception cref="LibraryException">O jogo não existe, ou o VNDB não tem o id.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> parou a espera pela trava do arquivo.</exception>
    /// <exception cref="VNForge.Core.Vndb.VndbException">O VNDB falhou.</exception>
    public async Task<LibraryGame> ConfirmAsync(string gameId, string vndbId, CancellationToken cancellationToken = default)
    {
        var game = await Task.Run(() => Get(gameId), cancellationToken).ConfigureAwait(false);
        var confirmed = await matcherFactory().ConfirmAsync(game, vndbId, cancellationToken).ConfigureAwait(false);
        await Task.Run(() => file.Update(library => Replaced(library, confirmed), cancellationToken), cancellationToken).ConfigureAwait(false);
        return confirmed;
    }

    /// <summary>Procura o jogo no VNDB de novo, mesmo que já tenha sido procurado, e guarda os candidatos novos. Não confirma sozinho.</summary>
    /// <exception cref="LibraryException">O jogo não existe mais na biblioteca.</exception>
    /// <exception cref="VNForge.Core.Vndb.VndbException">O VNDB falhou.</exception>
    public async Task<LibraryGame> SearchAgainAsync(string gameId, CancellationToken cancellationToken = default)
    {
        var game = await Task.Run(() => Get(gameId), cancellationToken).ConfigureAwait(false);
        var searched = await matcherFactory().MatchAsync(game, new LibraryMatchOptions(), cancellationToken).ConfigureAwait(false);
        await Task.Run(() => file.Update(library => Replaced(library, searched), cancellationToken), cancellationToken).ConfigureAwait(false);
        return searched;
    }

    /// <summary>"Não é nenhum deles": esquece os candidatos. O jogo fica sem casamento até a pessoa procurar de novo ou ligar um jogo do VNDB.</summary>
    public LibraryGame DismissCandidates(string gameId) =>
        file.Update(library =>
        {
            var game = library.Find(gameId) with { Candidates = [] };
            library.Replace(game);
            return game;
        });

    /// <summary>Desfaz a confirmação do jogo (os candidatos ficam).</summary>
    public LibraryGame Unconfirm(string gameId) =>
        file.Update(library =>
        {
            var game = LibraryMatcher.Unconfirm(library.Find(gameId));
            library.Replace(game);
            return game;
        });

    /// <summary>Marca (ou desmarca) que o jogo pede a localidade japonesa: o Jogar abre pelo Locale Emulator.</summary>
    public LibraryGame SetJapaneseLocale(string gameId, bool value) =>
        file.Update(library =>
        {
            var game = library.Find(gameId) with { RequiresJapaneseLocale = value };
            library.Replace(game);
            return game;
        });

    /// <summary>Marca (ou desmarca) que o exe do jogo abre um lançador antes do jogo: o tempo passa a seguir o processo do jogo.</summary>
    public LibraryGame SetOpensLauncher(string gameId, bool value) =>
        file.Update(library =>
        {
            var game = library.Find(gameId) with { OpensLauncher = value };
            library.Replace(game);
            return game;
        });

    /// <summary>Tira o jogo da Biblioteca. A pasta dele não é tocada. Devolve se o jogo estava lá.</summary>
    public bool Remove(string gameId) => file.Update(library => library.Remove(gameId));

    private static LibraryGame Replaced(GameLibrary library, LibraryGame game)
    {
        library.Replace(game);
        return game;
    }

    private LibraryGame Get(string gameId) => file.Load().Find(gameId);
}
