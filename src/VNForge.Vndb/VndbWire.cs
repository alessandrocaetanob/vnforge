using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using VNForge.Core.Vndb;

namespace VNForge.Vndb;

/// <summary>
/// Os formatos de resposta da API Kana do VNDB (api.vndb.org/kana) e a conversão para os tipos do Core. Os nomes seguem o JSON em snake_case; campo que a API não manda
/// fica no valor padrão. Só os campos que o app usa estão aqui.
/// </summary>
internal static class VndbWire
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public sealed class Page<T>
    {
        public List<T> Results { get; init; } = [];

        public bool More { get; init; }
    }

    public sealed class Image
    {
        public string Url { get; init; } = string.Empty;

        public string? Thumbnail { get; init; }

        public int[]? Dims { get; init; }

        public double Sexual { get; init; }

        public double Violence { get; init; }
    }

    public sealed class Tag
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string Category { get; init; } = string.Empty;

        public double Rating { get; init; }

        public int Spoiler { get; init; }

        public bool Lie { get; init; }
    }

    public sealed class Title
    {
        public string Lang { get; init; } = string.Empty;

        // Na API o campo se chama "title"; a propriedade usa outro nome para não colidir com o tipo.
        [JsonPropertyName("title")]
        public string Text { get; init; } = string.Empty;

        public string? Latin { get; init; }

        public bool Official { get; init; }

        public bool Main { get; init; }
    }

    public sealed class VisualNovel
    {
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Name { get; init; } = string.Empty;

        public string? Alttitle { get; init; }

        public List<Title>? Titles { get; init; }

        public List<string>? Aliases { get; init; }

        public string? Olang { get; init; }

        public int? Devstatus { get; init; }

        public string? Released { get; init; }

        public List<string>? Languages { get; init; }

        public List<string>? Platforms { get; init; }

        public Image? Image { get; init; }

        public int? Length { get; init; }

        public int? LengthMinutes { get; init; }

        public int LengthVotes { get; init; }

        public double? Rating { get; init; }

        public string? Description { get; init; }

        public List<Tag>? Tags { get; init; }
    }

    public sealed class Trait
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string GroupId { get; init; } = string.Empty;

        public string GroupName { get; init; } = string.Empty;

        public int Spoiler { get; init; }

        public bool Lie { get; init; }
    }

    public sealed class ReleaseRef
    {
        public string? Id { get; init; }
    }

    public sealed class Appearance
    {
        public string Id { get; init; } = string.Empty;

        public string Role { get; init; } = string.Empty;

        public int Spoiler { get; init; }

        public ReleaseRef? Release { get; init; }
    }

    public sealed class Character
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string? Original { get; init; }

        public List<string>? Aliases { get; init; }

        public string? Description { get; init; }

        public Image? Image { get; init; }

        public string?[]? Gender { get; init; }

        public List<Appearance>? Vns { get; init; }

        public List<Trait>? Traits { get; init; }
    }

    public sealed class ReleaseLanguage
    {
        public string Lang { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string? Text { get; init; }

        public string? Latin { get; init; }

        public bool Mtl { get; init; }

        public bool Main { get; init; }
    }

    public sealed class ReleaseVn
    {
        public string Id { get; init; } = string.Empty;

        public string Rtype { get; init; } = string.Empty;
    }

    public sealed class Release
    {
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Name { get; init; } = string.Empty;

        public string? Alttitle { get; init; }

        public List<ReleaseLanguage>? Languages { get; init; }

        public List<string>? Platforms { get; init; }

        public string? Released { get; init; }

        public int? Minage { get; init; }

        public bool Patch { get; init; }

        public bool Freeware { get; init; }

        public bool Official { get; init; }

        public string? Engine { get; init; }

        public int? Voiced { get; init; }

        public List<ReleaseVn>? Vns { get; init; }
    }

    public sealed class AuthInfo
    {
        public string Id { get; init; } = string.Empty;

        public string Username { get; init; } = string.Empty;

        public List<string>? Permissions { get; init; }
    }

    public sealed class ListLabel
    {
        public int Id { get; init; }

        public string Label { get; init; } = string.Empty;
    }

    public sealed class ListVn
    {
        [JsonPropertyName("title")]
        public string? Name { get; init; }
    }

    public sealed class ListEntry
    {
        public string Id { get; init; } = string.Empty;

        public long Added { get; init; }

        public long Lastmod { get; init; }

        public long? Voted { get; init; }

        public int? Vote { get; init; }

        public string? Started { get; init; }

        public string? Finished { get; init; }

        public string? Notes { get; init; }

        public List<ListLabel>? Labels { get; init; }

        public ListVn? Vn { get; init; }
    }

    public static VndbImage? ToCore(Image? image) =>
        image is null || string.IsNullOrEmpty(image.Url)
            ? null
            : new VndbImage(
                image.Url,
                image.Thumbnail,
                image.Dims is { Length: 2 } ? image.Dims[0] : null,
                image.Dims is { Length: 2 } ? image.Dims[1] : null,
                image.Sexual,
                image.Violence);

    public static VndbVisualNovel ToCore(VisualNovel vn) =>
        new(
            vn.Id,
            vn.Name,
            vn.Alttitle,
            [.. (vn.Titles ?? []).Select(t => new VndbTitle(t.Lang, t.Text, t.Latin, t.Official, t.Main))],
            [.. vn.Aliases ?? []],
            vn.Olang,
            vn.Devstatus,
            vn.Released,
            [.. vn.Languages ?? []],
            [.. vn.Platforms ?? []],
            ToCore(vn.Image),
            vn.Length,
            vn.LengthMinutes,
            vn.LengthVotes,
            vn.Description,
            [.. (vn.Tags ?? []).Select(t => new VndbTag(t.Id, t.Name, t.Category, t.Rating, t.Spoiler, t.Lie))],
            vn.Rating);

    public static VndbCharacter ToCore(Character c) =>
        new(
            c.Id,
            c.Name,
            c.Original,
            [.. c.Aliases ?? []],
            c.Description,
            ToCore(c.Image),
            c.Gender is { Length: > 0 } ? new VndbGender(c.Gender[0], c.Gender.Length > 1 ? c.Gender[1] : c.Gender[0]) : null,
            [.. (c.Vns ?? []).Select(v => new VndbCharacterAppearance(v.Id, v.Role, v.Spoiler, v.Release?.Id))],
            [.. (c.Traits ?? []).Select(t => new VndbTrait(t.Id, t.Name, t.GroupId, t.GroupName, t.Spoiler, t.Lie))]);

    public static VndbRelease ToCore(Release r) =>
        new(
            r.Id,
            r.Name,
            r.Alttitle,
            [.. (r.Languages ?? []).Select(l => new VndbReleaseLanguage(l.Lang, l.Text, l.Latin, l.Mtl, l.Main))],
            [.. r.Platforms ?? []],
            r.Released,
            r.Minage,
            r.Patch,
            r.Freeware,
            r.Official,
            r.Engine,
            r.Voiced,
            [.. (r.Vns ?? []).Select(v => new VndbReleaseVisualNovel(v.Id, v.Rtype))]);

    public static VndbListEntry ToCore(ListEntry e) =>
        new(
            e.Id,
            e.Vn?.Name,
            DateTimeOffset.FromUnixTimeSeconds(e.Added),
            DateTimeOffset.FromUnixTimeSeconds(e.Lastmod),
            e.Voted is { } voted ? DateTimeOffset.FromUnixTimeSeconds(voted) : null,
            e.Vote,
            ParseDate(e.Started),
            ParseDate(e.Finished),
            e.Notes,
            [.. (e.Labels ?? []).Select(l => new VndbListLabel(l.Id, l.Label))]);

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
