using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Core.Tests.Library;

/// <summary>
/// Três jogos inventados do VNDB, dois deles na biblioteca e um fora, e personagens ligados por <c>vns</c>: Hana está nos três, Mika tem papel e spoiler diferentes por edição
/// no jogo 1, e Kai só aparece no jogo de fora.
/// </summary>
internal static class LibraryCharactersSamples
{
    public const string Moon = "v900101";
    public const string Star = "v900102";
    public const string Outside = "v900103";

    public static readonly DateTimeOffset Now = new(2026, 11, 9, 20, 0, 0, TimeSpan.Zero);

    public static VndbTrait LongHair { get; } = new("i1", "Long Hair", "i10", "Hair", 0, false, Sexual: false);

    public static VndbTrait Ghost { get; } = new("i2", "Ghost", "i20", "Personality", 2, false, Sexual: false);

    public static VndbVisualNovel Game(string id, string title) =>
        new(id, title, null, [], [], "ja", 2, "2020-01-01", ["ja"], ["win"], null, 3, 1200, 10, "Texto.", []);

    public static VndbCharacter Hana() =>
        new("c101", "Hana Tsukino", "月野 花", ["Hanako"], "A florist. [spoiler]She is a ghost.[/spoiler]", null, new VndbGender("f", "f"),
            [new VndbCharacterAppearance(Moon, "main", 0, null), new VndbCharacterAppearance(Star, "side", 1, null), new VndbCharacterAppearance(Outside, "primary", 0, null)],
            [LongHair, Ghost]);

    public static VndbCharacter Mika() =>
        new("c102", "Mika Aoba", null, [], "A student.", null, new VndbGender("f", "f"),
            [
                new VndbCharacterAppearance(Moon, "main", 0, "r1"),
                new VndbCharacterAppearance(Moon, "side", 2, "r2"),
                new VndbCharacterAppearance(Moon, "primary", 1, null),
            ],
            [LongHair]);

    public static VndbCharacter Kai() =>
        new("c103", "Kai Ono", null, [], "Only elsewhere.", null, null, [new VndbCharacterAppearance(Outside, "main", 0, null)], []);

    public static VndbCharacter Secret() =>
        new("c104", "Secret Person", null, [], "Hidden.", null, null, [new VndbCharacterAppearance(Star, "side", 2, null)], []);

    public static VndbRelease Release(string id, string title, string visualNovelId = Moon, bool patch = false) =>
        new(id, title, null, [], ["win"], "2020-01-01", null, patch, false, true, null, null, [new VndbReleaseVisualNovel(visualNovelId, "complete")]);

    public static FakeVndbCatalog Catalog() =>
        new(
            [Game(Moon, "Moon Flower"), Game(Star, "Star Garden"), Game(Outside, "Outside Game")],
            [Hana(), Mika(), Kai(), Secret()],
            [Release("r1", "Moon Flower"), Release("r2", "Moon Flower Deluxe Pack"), Release("r3", "Star Garden", Star), Release("r4", "Moon Flower", patch: true)]);

    public static LibraryGame Owned(string id, string folderName, string vndbId, string title) =>
        new(id, $"C:\\Jogos\\{folderName}", null, LibraryEngine.Unknown, title, null, Now) { VndbId = vndbId, Confirmation = LibraryConfirmation.Manual };

    public static GameLibrary OwnedLibrary() =>
        new([Owned("g1", "Moon Flower", Moon, "Moon Flower"), Owned("g2", "Star Garden", Star, "Star Garden")]);

    public static async Task<SheetStore> FilledAsync(string directory, FakeVndbCatalog? catalog = null)
    {
        var store = new SheetStore(directory);
        var source = catalog ?? Catalog();
        foreach (var id in new[] { Moon, Star })
        {
            await SheetFetcher.FetchAsync(source, store, id, cancellationToken: TestContext.Current.CancellationToken);
        }

        return store;
    }
}
