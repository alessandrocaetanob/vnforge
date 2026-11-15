using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Vndb;
using VNForge.Vndb;
using static VNForge.Core.Tests.Library.LibraryCharactersSamples;

namespace VNForge.Core.Tests.Library;

/// <summary>#558: a sincronização enche o registro com o catálogo falso e não volta ao VNDB pelo que já tem.</summary>
[Trait("Risco", "R7")]
public sealed class LibraryCharacterSyncTests : IDisposable
{
    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SheetStore Store => new(_dir.Path);

    private GameEditionsFile EditionsFile => new(_dir.Path);

    private Task<LibraryCharacterSyncResult> Sync(IVndbCatalog catalog, GameLibrary? library = null, bool again = false, IProgress<LibraryCharacterSyncProgress>? progress = null) =>
        LibraryCharacterSync.SyncAsync(catalog, library ?? OwnedLibrary(), Store, EditionsFile, new LibraryCharacterSyncOptions(again, new FixedClock(Now), progress), Ct);

    [Fact]
    public async Task A_ida_e_volta_completa_com_o_registro_em_disco_mostra_os_dois_jogos_e_esconde_o_de_fora()
    {
        await Sync(Catalog());

        var list = LibraryCharacters.List(OwnedLibrary(), Store.LoadAllCharacters(), Store.LoadTerms(), EditionsFile.Load(), new LibraryPrivacy(ShowSpoilers: true));

        var hana = Assert.Single(list.Characters, c => c.Id == "c101");
        Assert.Equal([("Moon Flower", "protagonista"), ("Star Garden", "secundário")], hana.AppearsIn.Select(a => (a.GameTitle, a.Role)));
        var mika = Assert.Single(list.Characters, c => c.Id == "c102");
        Assert.Equal(("main", 2), (mika.AppearsIn[0].RoleCode, mika.AppearsIn[0].Spoiler));
    }

    private sealed class Collect(List<LibraryCharacterSyncProgress> reports) : IProgress<LibraryCharacterSyncProgress>
    {
        public void Report(LibraryCharacterSyncProgress value) => reports.Add(value);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
