using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Vndb;

/// <summary>Jogos e personagens inventados do VNDB para os testes de ligação ao projeto.</summary>
internal static class VndbSamples
{
    public const string VisualNovelId = "v900001";

    public static VndbVisualNovel Game(string id = VisualNovelId, string title = "Amaoto no Hakoniwa", string? alt = "雨音の箱庭", string? description = null, params string[] aliases) =>
        new(
            id,
            title,
            alt,
            alt is null ? [] : [new VndbTitle("ja", alt, title, true, true)],
            aliases,
            "ja",
            0,
            "2019-05-31",
            ["ja"],
            ["win"],
            null,
            3,
            1500,
            42,
            description,
            []);

    public static VndbCharacter Character(
        string id,
        string name,
        string? original,
        string? gender,
        string role = "side",
        string? description = null,
        string visualNovelId = VisualNovelId,
        VndbImage? image = null,
        params string[] aliases) =>
        new(
            id,
            name,
            original,
            aliases,
            description,
            image,
            gender is null ? null : new VndbGender(gender, gender),
            [new VndbCharacterAppearance(visualNovelId, role, 0, null)],
            []);

    public static VndbCharacter[] Cast { get; } =
    [
        Character("c1", "Kenta Aizawa", "相沢 健太", "m", "main", "O [b]protagonista[/b].", aliases: ["Ken", "Kenta-kun"]),
        Character("c2", "Aoi Sakura", "佐倉 葵", "f", "primary", aliases: ["Aoi-chan"]),
        Character("c3", "Ren Hayase", "早瀬 蓮", "m"),
        Character("c4", "Misaki Todo", "藤堂 美咲", "f", aliases: ["Todo-sensei"]),
        Character("c5", "Sem escrita original", null, "f"),
    ];
}
