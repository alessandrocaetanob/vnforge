using System.Text.Json;
using VNForge.AI.Sheet;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Cli.Tests;

/// <summary>#558: <c>vnf library characters</c> com o catálogo falso do VNDB e o provedor falso de IA da ficha.</summary>
[Trait("Risco", "R9")]
public sealed class LibraryCharactersCommandTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static VndbVisualNovel Game(string id, string title) =>
        new(id, title, null, [], [], "ja", 2, "2004-04-28", ["ja"], ["win"], null, 4, 2700, 80, "Texto.", [new VndbTag("g10", "Comedy", "cont", 2.5, 0, false)]);

    private static VndbCharacter Tomoya() =>
        new("c1", "Tomoya Okazaki", "岡崎 朋也", ["Tomo"], "The hero. [spoiler]He is a ghost.[/spoiler]", null, new VndbGender("m", "f"),
            [new VndbCharacterAppearance("v4", "main", 0, null), new VndbCharacterAppearance("v5", "side", 1, null), new VndbCharacterAppearance("v6", "primary", 0, null)],
            [new VndbTrait("i1", "Long Hair", "i10", "Hair", 0, false, false), new VndbTrait("i2", "Amnesia", "i20", "Personality", 1, false, false)]);

    private static VndbCharacter Sister() =>
        new("c2", "Ghost Sister", null, [], "Who is she?", null, null, [new VndbCharacterAppearance("v4", "side", 2, null)], []);

    private static VndbCharacter Kyou() =>
        new("c3", "Kyou Fujibayashi", null, [], "A classmate.", null, new VndbGender("f", "f"),
            [new VndbCharacterAppearance("v5", "main", 0, "r10"), new VndbCharacterAppearance("v5", "side", 2, "r11")],
            [new VndbTrait("i1", "Long Hair", "i10", "Hair", 0, false, false)]);

    private static VndbCharacter Outsider() =>
        new("c9", "Only Elsewhere", null, [], "Not here.", null, null, [new VndbCharacterAppearance("v6", "main", 0, null)], []);

    private static VndbRelease Release(string id, string title) =>
        new(id, title, null, [], ["win"], "2008-01-01", null, false, false, true, null, null, [new VndbReleaseVisualNovel("v5", "complete")]);

    private FakeVndbCatalog Prepare(string secondFolder = "Little Busters", bool confirmed = true)
    {
        static LibraryGame Owned(string id, string folder, string title, string? vndb) =>
            new(id, $"C:\\Jogos\\{folder}", null, LibraryEngine.Unknown, title, null, T0) { VndbId = vndb, Confirmation = vndb is null ? LibraryConfirmation.None : LibraryConfirmation.Manual };

        new LibraryFile(_cli.UserConfigDirectory).Save(new GameLibrary([Owned("g1", "CLANNAD", "CLANNAD", confirmed ? "v4" : null), Owned("g2", secondFolder, "Little Busters", confirmed ? "v5" : null)]));
        var catalog = new FakeVndbCatalog(
            [Game("v4", "CLANNAD"), Game("v5", "Little Busters"), Game("v6", "Outside")],
            [Tomoya(), Sister(), Kyou(), Outsider()],
            [Release("r10", "Little Busters"), Release("r11", "Little Busters Perfect Edition")]);
        _cli.Cli.VndbCatalogFactory = () => catalog;
        _cli.Cli.SheetTranslatorFactory = (_, _) => new FakeSheetTranslator(item => item.Source == "Long Hair" ? "Cabelo longo" : FakeSheetTranslator.Prefix + item.Source);
        return catalog;
    }

    private async Task<RunResult> Run(params string[] args) => await _cli.RunAsync(args, Ct);

    private static JsonElement Data(RunResult result) => JsonDocument.Parse(result.Output).RootElement.GetProperty("data");

    private async Task Ready(bool translate = false)
    {
        await Run("library", "characters", "sync");
        if (translate)
        {
            await Run("library", "sheet", "translate", "CLANNAD", "--yes");
            await Run("library", "sheet", "translate", "g2", "--yes");
        }
    }

    [Theory]
    [InlineData(AdultMode.Blur, false)]
    public async Task Json_nao_traz_o_endereco_do_retrato_adulto_fora_do_Mostrar(AdultMode mode, bool exposed)
    {
        var face = new VndbImage("https://t.vndb.org/ch/5.jpg", null, 100, 140, 2, 0);
        var character = new VndbCharacter("c5", "Adult Face", null, [], "Texto.", face, null, [new VndbCharacterAppearance("v4", "main", 0, null)], []);
        new LibraryFile(_cli.UserConfigDirectory).Save(new GameLibrary(
            [new LibraryGame("g1", "C:\\Jogos\\CLANNAD", null, LibraryEngine.Unknown, "CLANNAD", null, T0) { VndbId = "v4", Confirmation = LibraryConfirmation.Manual }],
            new LibraryPrivacy(Adult: mode)));
        _cli.Cli.VndbCatalogFactory = () => new FakeVndbCatalog([Game("v4", "CLANNAD")], [character], []);
        await Run("library", "characters", "sync");
        await Run("library", "sheet", "fetch", "CLANNAD");

        var list = await Run("library", "characters", "list", "--json");
        var show = await Run("library", "characters", "show", "c5", "--json");
        var sheet = await Run("library", "sheet", "show", "CLANNAD", "--json");

        foreach (var output in new[] { list.Output, show.Output, sheet.Output })
        {
            Assert.Equal(exposed, output.Contains("t.vndb.org/ch/5.jpg", StringComparison.Ordinal));
        }
    }
}
