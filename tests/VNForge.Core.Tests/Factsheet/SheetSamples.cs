using VNForge.Core.Factsheet;
using VNForge.Core.Vndb;
using VNForge.Core.Tests.Vndb;
using VNForge.Vndb;

namespace VNForge.Core.Tests.Factsheet;

/// <summary>Jogos, personagens e traços inventados do VNDB para os testes da ficha, e um tradutor que não chama IA nenhuma.</summary>
internal static class SheetSamples
{
    public const string Game1 = "v900001";
    public const string Game2 = "v900002";

    public static VndbTag Comedy { get; } = new("g10", "Comedy", "cont", 2.5, 0, false);

    public static VndbTag Twist { get; } = new("g11", "Plot Twist", "cont", 2.0, 2, false);

    public static VndbTag Lewd { get; } = new("g12", "Sexual Content", "ero", 2.4, 0, false);

    public static VndbTrait Hair { get; } = new("i1", "Long Hair", "i10", "Hair", 0, false);

    public static VndbTrait Secret { get; } = new("i2", "Amnesia", "i20", "Personality", 1, false);

    public static VndbTrait Fake { get; } = new("i3", "Tsundere", "i20", "Personality", 0, true);

    public static VndbTrait Adult { get; } = new("i4", "Virgin", "i30", "Sexual Content", 0, false);

    public static VndbVisualNovel Game(string id = Game1, string title = "Amaoto no Hakoniwa", string? description = "A rainy town. [spoiler]He dies.[/spoiler] Kenta [b]returns[/b].") =>
        VndbSamples.Game(id, title, "雨音の箱庭", description) with { Tags = [Comedy, Twist, Lewd] };

    public static VndbCharacter Kenta(string visualNovelId = Game1, int spoiler = 0, string role = "main") =>
        new("c1", "Kenta Aizawa", "相沢 健太", ["Ken"], "The hero. [spoiler]He is a ghost.[/spoiler]", Image(0.1, 0.0), new VndbGender("m", "m"),
            [new VndbCharacterAppearance(visualNovelId, role, spoiler, null)], [Hair, Secret, Fake, Adult]);

    public static VndbCharacter Aoi(string visualNovelId = Game1, int spoiler = 0) =>
        new("c2", "Aoi Sakura", "佐倉 葵", [], "Childhood friend.", Image(1.5, 0.0), new VndbGender("f", "m"),
            [new VndbCharacterAppearance(visualNovelId, "primary", spoiler, null)], [Hair]);

    public static VndbCharacter Ghost(string visualNovelId = Game1) =>
        new("c3", "Ghost", null, [], "Who is she? [spoiler]Aoi's sister.[/spoiler]", null, null,
            [new VndbCharacterAppearance(visualNovelId, "side", 2, null)], []);

    public static VndbImage Image(double sexual, double violence) => new("https://t.vndb.org/ch/1.jpg", null, 100, 200, sexual, violence);

    public static FakeVndbCatalog Catalog(params VndbVisualNovel[] games) =>
        new(games.Length == 0 ? [Game()] : games, [Kenta(), Aoi(), Ghost()]);
}

/// <summary>Um tradutor que escreve "PT: " na frente do original e guarda os pedidos. Os marcadores de spoiler ficam onde estavam.</summary>
internal sealed class StubSheetTranslator(Func<SheetTranslationItem, string?>? translate = null) : ISheetTranslator
{
    public List<SheetTranslationRequest> Requests { get; } = [];

    public IEnumerable<string> Keys => Requests.SelectMany(r => r.Items).Select(i => i.Key);

    public Task<IReadOnlyList<SheetTranslation>> TranslateAsync(SheetTranslationRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        IReadOnlyList<SheetTranslation> reply =
        [
            .. request.Items
                .Select(i => (i.Key, Text: translate is null ? "PT: " + i.Source : translate(i)))
                .Where(t => t.Text is not null)
                .Select(t => new SheetTranslation(t.Key, t.Text!)),
        ];
        return Task.FromResult(reply);
    }
}
