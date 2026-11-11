namespace VNForge.Core.Library;

/// <summary>A coluna "Suporte" da primeira varredura.</summary>
public enum LibraryScanSupport
{
    /// <summary>O VNForge traduz a engine.</summary>
    Translates,

    /// <summary>A engine foi reconhecida e o VNForge ainda não a traduz.</summary>
    NoSupportYet,

    /// <summary>Nenhuma engine conhecida na pasta.</summary>
    NoEngine,
}

/// <summary>A coluna "VNDB" da primeira varredura.</summary>
public enum LibraryScanVndb
{
    /// <summary>Ainda na fila para o casamento.</summary>
    Waiting,

    /// <summary>O casamento está procurando este jogo.</summary>
    Searching,

    /// <summary>Casado com um jogo do VNDB.</summary>
    Matched,

    /// <summary>Há candidatos e a pessoa ainda confirma.</summary>
    Confirm,

    /// <summary>O VNDB não achou o jogo.</summary>
    NoMatch,
}

/// <summary>Uma linha da tabela da primeira varredura: a pasta (só o nome), a engine e os dois estados.</summary>
public sealed record LibraryScanRow(string Id, string Folder, string Engine, LibraryScanSupport Support, LibraryScanVndb Vndb);

/// <summary>"Até agora": quantos jogos a varredura reconheceu e traduz, quantos reconheceu sem traduzir, quantos não reconheceu e quantos esperam a confirmação do VNDB.</summary>
public sealed record LibraryScanTally(int Recognized, int NoSupportYet, int Unrecognized, int ToConfirm);

public static class LibraryScanReport
{
    /// <param name="searching">A linha está sendo procurada agora no VNDB (só vale para o jogo que ainda não foi buscado).</param>
    public static LibraryScanRow Row(LibraryGame game, bool searching = false)
    {
        ArgumentNullException.ThrowIfNull(game);
        var vndb = game switch
        {
            { IsConfirmed: true } => LibraryScanVndb.Matched,
            { Candidates.Count: > 0 } => LibraryScanVndb.Confirm,
            { SearchedAt: not null } => LibraryScanVndb.NoMatch,
            _ => searching ? LibraryScanVndb.Searching : LibraryScanVndb.Waiting,
        };
        var support = game.Engine.Source switch
        {
            LibraryEngineSource.Engine => LibraryScanSupport.Translates,
            LibraryEngineSource.Signature => LibraryScanSupport.NoSupportYet,
            _ => LibraryScanSupport.NoEngine,
        };
        return new LibraryScanRow(game.Id, GameLibrary.FolderName(game), game.Engine.Source == LibraryEngineSource.Unknown ? "Não reconhecida" : game.Engine.Name, support, vndb);
    }

    public static LibraryScanTally Tally(IEnumerable<LibraryGame> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        int recognized = 0, noSupport = 0, unrecognized = 0, toConfirm = 0;
        foreach (var game in games)
        {
            switch (game.Engine.Source)
            {
                case LibraryEngineSource.Engine:
                    recognized++;
                    break;
                case LibraryEngineSource.Signature:
                    noSupport++;
                    break;
                default:
                    unrecognized++;
                    break;
            }

            toConfirm += !game.IsConfirmed && game.Candidates.Count > 0 ? 1 : 0;
        }

        return new LibraryScanTally(recognized, noSupport, unrecognized, toConfirm);
    }
}
