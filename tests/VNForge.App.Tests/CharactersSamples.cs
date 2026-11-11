using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Tests;

/// <summary>
/// Três jogos inventados do VNDB, dois na Biblioteca e um fora, e cinco personagens: Hana está nos três, Mika tem papel e spoiler diferentes por edição no jogo 1, Kai só aparece no
/// jogo de fora e Fantasma só em jogo com spoiler grande.
/// </summary>
internal static class CharactersSamples
{
    public const string Moon = "v900101";
    public const string Star = "v900102";
    public const string Outside = "v900103";

    public static readonly DateTimeOffset Now = new(2026, 11, 9, 20, 0, 0, TimeSpan.Zero);

    internal static VndbTrait LongHair => new("i1", "Long Hair", "i10", "Hair", 0, false, Sexual: false);

    internal static VndbTrait Ghost => new("i2", "Ghost", "i20", "Personality", 2, false, Sexual: false);

    internal static VndbVisualNovel Game(string id, string title) =>
        new(id, title, null, [], [], "ja", 2, "2020-01-01", ["ja"], ["win"], null, 3, 1200, 10, "Texto.", []);

    internal static VndbCharacter Character(string id, string name, string? original, string? gender, VndbCharacterAppearance[] appearances, VndbTrait[] traits) =>
        new(id, name, original, name == "Hana Tsukino" ? ["Hanako"] : [], $"{name}.", null, gender is null ? null : new VndbGender(gender, gender), appearances, traits, name == "Hana Tsukino" ? 17 : null);

    /// <summary>Cinco traços a mais para a Hana (sete no total): com mais de seis, o filtro ganha o link "Ver os N traços".</summary>
    internal static VndbTrait[] ExtraTraits => [.. Enumerable.Range(1, 5).Select(n => new VndbTrait($"i{100 + n}", $"Traço {n}", "i30", "Roupa", 0, false, Sexual: false))];

    internal static FakeVndbCatalog Catalog() => Catalog(manyTraits: false);

    /// <summary>Com <paramref name="adultPortrait"/>, o retrato da Hana é sexual no VNDB (nota 2 de 2).</summary>
    internal static FakeVndbCatalog Catalog(bool manyTraits, bool adultPortrait = false) => new(
        [Game(Moon, "Moon Flower"), Game(Star, "Star Garden"), Game(Outside, "Outside Game")],
        [
            Character("c101", "Hana Tsukino", "月野 花", "f", [new(Moon, "main", 0, null), new(Star, "side", 0, null), new(Outside, "primary", 0, null)], [LongHair, Ghost, .. manyTraits ? ExtraTraits : []]) with { Image = adultPortrait ? new VndbImage("https://t.vndb.org/ch/101.jpg", null, 100, 140, 2, 0) : null },
            Character("c102", "Mika Aoba", null, "f", [new(Moon, "main", 2, "r2"), new(Moon, "side", 0, "r1")], [LongHair]),
            Character("c103", "Kai Ono", null, "m", [new(Outside, "main", 0, null)], []),
            Character("c104", "Sora Mori", null, "m", [new(Star, "side", 0, null)], []),
            Character("c105", "Fantasma", null, null, [new(Star, "side", 2, null)], []),
        ]);

    internal static LibraryGame Owned(string id, string folder, string vndbId, string title) =>
        new(id, $"C:\\Jogos\\{folder}", null, LibraryEngine.Unknown, title, null, Now) { VndbId = vndbId, Confirmation = LibraryConfirmation.Manual };

    /// <summary>Grava as fichas dos três jogos e uma Biblioteca com os dois primeiros; <paramref name="matched"/> casa a edição r1 com a pasta do jogo 1.</summary>
    public static async Task SeedAsync(string configDirectory, bool matched = false, bool showSpoilers = false, CancellationToken ct = default, bool manyTraits = false, bool adultPortrait = false)
    {
        var store = new SheetStore(configDirectory);
        foreach (var id in new[] { Moon, Star, Outside })
        {
            await SheetFetcher.FetchAsync(Catalog(manyTraits, adultPortrait), store, id, new FixedClock(Now), ct);
        }

        new LibraryFile(configDirectory).Save(new GameLibrary([Owned("g1", "Moon Flower", Moon, "Moon Flower"), Owned("g2", "Star Garden", Star, "Star Garden")])
        {
            Privacy = new LibraryPrivacy(ShowSpoilers: showSpoilers),
        });
        if (matched)
        {
            new GameEditionsFile(configDirectory).Update(e => e.Set("g1", Moon, ["r1"], Now));
        }
    }
}
