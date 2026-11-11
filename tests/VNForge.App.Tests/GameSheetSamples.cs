using VNForge.AI.Sheet;
using VNForge.Core.Factsheet;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Tests;

/// <summary>
/// A ficha de "Amaoto no Hakoniwa" do quadro da #554, inventada e gravada numa pasta de configuração de teste: sete personagens (um com spoiler grande), oito tags (duas com spoiler)
/// e a tradução pelo provedor falso, com um texto em cada etapa. Nada vem do VNDB de verdade.
/// </summary>
internal static class GameSheetSamples
{
    public const string Id = "v31201";

    public static readonly DateTimeOffset Now = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);

    public static readonly IReadOnlyDictionary<string, string> Portuguese = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["synopsis"] = "Numa cidade onde chove quase o ano inteiro, Kenta volta à escola em que o irmão mais velho estudou e encontra, no jardim do telhado, uma caixa de música que só toca quando alguém mente. O clube de leitura decide descobrir quem a deixou ali. A cada capítulo a chuva aperta e uma mentira velha vem à tona.",
        ["character:c1"] = "O protagonista. Volta à escola do irmão.",
        ["character:c2"] = "Colega de turma de Kenta. Fala pouco, lê no telhado em dias de chuva e guarda a chave do jardim.",
        ["character:c3"] = "Amigo de infância de Kenta, sempre com uma resposta pronta.",
        ["character:c4"] = "Professora do clube de leitura.",
        ["character:c5"] = "Presidente do conselho estudantil.",
        ["character:c6"] = "Veterano do clube, calmo.",
        ["character:c7"] = "Quem é ela? [spoiler]A irmã de Aoi.[/spoiler]",
        ["tag:g1"] = "Drama escolar",
        ["tag:g2"] = "Mistério",
        ["tag:g3"] = "Slice of life",
        ["tag:g4"] = "Chuva",
        ["tag:g5"] = "Clube de leitura",
        ["tag:g6"] = "Caixa de música",
        ["tag:g7"] = "Viagem no tempo",
        ["tag:g8"] = "Final surpresa",
        ["trait:i1"] = "Cabelo curto",
        ["trait:i2"] = "Esportista",
        ["trait:i3"] = "Cabelo longo",
        ["trait:i4"] = "Quieta",
        ["trait:i5"] = "Estudante",
        ["trait:i6"] = "Rata de biblioteca",
        ["trait:i7"] = "Óculos",
        ["trait:i8"] = "Sarcástico",
        ["trait:i9"] = "Professora",
        ["trait:i10"] = "Rígida",
        ["trait:i11"] = "Presidente",
        ["trait:i12"] = "Séria",
        ["trait:i13"] = "Veterano",
        ["trait:i14"] = "Calmo",
        ["trait:i15"] = "Irmã de Kenta",
        ["trait:i16"] = "Mente",
    };

    private static VndbTrait Trait(int n, string name, string group = "Hair", int spoiler = 0) => new($"i{n}", name, "i" + group, group, spoiler, false, Sexual: false);

    private static VndbImage Image(int n) => new($"https://t.vndb.org/ch/{n}.jpg", null, 100, 140, 0, 0);

    private static VndbCharacter Character(int n, string name, string? original, string? gender, string role, int spoiler, string? description, VndbTrait[] traits, bool image = true) =>
        new($"c{n}", name, original, [], description, image ? Image(n) : null, gender is null ? null : new VndbGender(gender, gender), [new VndbCharacterAppearance(Id, role, spoiler, null)], traits);

    public static VndbVisualNovel Game() =>
        new(Id, "Amaoto no Hakoniwa", "雨音の箱庭", [], [], "ja", 0, "2019-05-31", ["ja"], ["win"], null, 3, 1080, 47,
            "A town where it rains all year. [b]Kenta[/b] finds a music box that only plays when someone lies.",
            [new VndbTag("g1", "School Drama", "cont", 2.8, 0, false), new VndbTag("g2", "Mystery", "cont", 2.6, 0, false), new VndbTag("g3", "Slice of Life", "cont", 2.5, 0, false),
             new VndbTag("g4", "Rain", "cont", 2.4, 0, false), new VndbTag("g5", "Reading Club", "cont", 2.3, 0, false), new VndbTag("g6", "Music Box", "cont", 2.2, 0, false),
             new VndbTag("g7", "Time Travel", "cont", 2.0, 2, false), new VndbTag("g8", "Twist Ending", "cont", 1.9, 1, false)],
            Rating: 79);

    public static VndbCharacter[] Characters() =>
    [
        Character(1, "Kenta Aizawa", "相沢 健太", "m", "main", 0, "The protagonist.", [Trait(1, "Short Hair"), Trait(2, "Athlete", "Role")]),
        Character(2, "Aoi Sakura", "佐倉 葵", "f", "primary", 0, "Kenta's classmate. She rarely speaks, reads on the roof on rainy days and keeps the key to the garden.",
            [Trait(3, "Long Hair"), Trait(4, "Quiet", "Personality"), Trait(5, "Student", "Role"), Trait(6, "Bookworm", "Personality"), Trait(15, "Kenta's Sister", "Role", 2), Trait(16, "Mind", "Personality", 1)]),
        Character(3, "Ren Hayase", "早瀬 蓮", "m", "primary", 0, "Kenta's childhood friend.", [Trait(7, "Glasses", "Clothes"), Trait(8, "Sarcastic", "Personality")]),
        Character(4, "Misaki Tōdō", "藤堂 美咲", "f", "side", 0, "The reading club teacher.", [Trait(9, "Teacher", "Role"), Trait(10, "Strict", "Personality")]),
        Character(5, "Tsubaki Inoue", "井上 椿", "f", "side", 0, "The student council president.", [Trait(11, "President", "Role"), Trait(12, "Serious", "Personality")]),
        Character(6, "Haruto Mine", "峰 春人", "m", "side", 0, "A senior in the club.", [Trait(13, "Senior", "Role"), Trait(14, "Calm", "Personality")]),
        Character(7, "Ghost", null, null, "side", 2, "Who is she? [spoiler]Aoi's sister.[/spoiler]", [], image: false),
    ];

    public static FakeVndbCatalog Catalog() => new([Game()], Characters());

    /// <summary>A mesma ficha com uma tag erótica (Nudity) e o retrato de Kenta marcado como sexual, para o modo adulto da Biblioteca (#631).</summary>
    public static FakeVndbCatalog AdultCatalog()
    {
        var game = Game() with { Tags = [.. Game().Tags, new VndbTag("g9", "Nudity", "ero", 2.5, 0, false)] };
        var characters = Characters();
        characters[0] = characters[0] with { Image = new VndbImage("https://t.vndb.org/ch/1.jpg", null, 100, 140, 2, 0) };
        return new FakeVndbCatalog([game], characters);
    }

    public static FakeSheetTranslator Translator() => new(item => Portuguese.GetValueOrDefault(item.Key) ?? "pt: " + item.Source);

    /// <summary>Busca e traduz a ficha com os provedores falsos e leva cada texto à etapa do quadro: Kenta aprovado, Aoi revisada, Ren traduzido à mão, os outros pela IA.</summary>
    public static async Task SeedAsync(string configDirectory, bool translate = true, CancellationToken ct = default, IVndbCatalog? catalog = null)
    {
        var store = new SheetStore(configDirectory);
        await SheetFetcher.FetchAsync(catalog ?? Catalog(), store, Id, new FixedClock(Now), ct);
        if (!translate)
        {
            return;
        }

        await new SheetTranslationRunner(Translator(), store).RunAsync(Id, cancellationToken: ct);
        SheetEditor.Review(store, Id, [SheetField.Synopsis, SheetField.OfCharacter("c1"), SheetField.OfTrait("i1"), SheetField.OfTrait("i2"), SheetField.OfCharacter("c2"), SheetField.OfTrait("i3"), SheetField.OfTrait("i4"), SheetField.OfTrait("i5"), SheetField.OfTrait("i6")]);
        SheetEditor.Approve(store, Id, [SheetField.OfCharacter("c1"), SheetField.OfTrait("i1"), SheetField.OfTrait("i2")]);
        SheetEditor.Edit(store, Id, SheetField.OfCharacter("c3"), Portuguese["character:c3"]);
        SheetEditor.Edit(store, Id, SheetField.OfTrait("i7"), Portuguese["trait:i7"]);
        SheetEditor.Edit(store, Id, SheetField.OfTrait("i8"), Portuguese["trait:i8"]);
    }
}
