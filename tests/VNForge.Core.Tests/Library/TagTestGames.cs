using VNForge.Core.Library;

namespace VNForge.Core.Tests.Library;

internal static class TagTestGames
{
    private static readonly DateTimeOffset Added = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Um jogo sem jogo do VNDB confirmado, com o id vindo da pasta.</summary>
    public static LibraryGame Plain(string name) =>
        new(GameLibrary.IdFor("C:\\Jogos\\" + name), "C:\\Jogos\\" + name, null, LibraryEngine.Unknown, name, null, Added);

    /// <summary>Um jogo confirmado, com as tags do VNDB dadas.</summary>
    public static LibraryGame WithVndb(string name, bool adult = false, params LibraryTag[] tags) =>
        Plain(name) with
        {
            VndbId = "v1",
            Confirmation = LibraryConfirmation.Manual,
            Info = new LibraryVndbInfo(name, null, null, null, null, null, null, 0, tags, adult),
        };
}
