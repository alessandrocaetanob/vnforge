using System.Globalization;
using System.Text;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;

namespace VNForge.Core.Vndb;

/// <summary>O que <see cref="VndbProjectLinker.ApplyCharacters"/> fez em <c>characters.json</c>.</summary>
/// <param name="Matched">Nomes do jogo (<c>ES.CHAR.NAME</c>) que casaram com um personagem do VNDB.</param>
/// <param name="Added">Personagens que não estavam em <c>characters.json</c> e entraram.</param>
/// <param name="Filled">Personagens que já estavam e ganharam algum campo que faltava (gênero, romaji, apelidos, descrição, id do VNDB ou imagem).</param>
public sealed record VndbApplyResult(int Matched, int Added, int Filled)
{
    public static VndbApplyResult None { get; } = new(0, 0, 0);
}

/// <summary>
/// Liga um projeto ao jogo do VNDB: acha os candidatos pelo título lido do jogo, guarda o confirmado em <c>vndb.json</c> e leva os personagens que casam
/// com os nomes do jogo para <c>characters.json</c>. É o Core do passo "Ficha do VNDB" do assistente de novo projeto e do <c>vnf vndb</c>.
/// </summary>
public static class VndbProjectLinker
{
    private const int MinimumFolderNameKey = 4;

    /// <summary>
    /// Os jogos do VNDB que casam com <paramref name="query"/>, do que mais casa para o que menos, com os que combinam também com a pasta ou com o executável
    /// do jogo na frente. Sem resultado devolve uma lista vazia.
    /// </summary>
    /// <param name="folderName">Nome da pasta do jogo, ou nulo.</param>
    /// <param name="exeName">Nome do executável do jogo (com ou sem <c>.exe</c>), ou nulo.</param>
    /// <exception cref="VndbException">O VNDB não respondeu.</exception>
    public static async Task<IReadOnlyList<VndbCandidate>> FindCandidatesAsync(
        IVndbCatalog catalog,
        string query,
        string? folderName,
        string? exeName,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var found = await catalog.SearchVisualNovelsAsync(query, limit, cancellationToken).ConfigureAwait(false);
        var local = new[] { folderName, exeName is null ? null : Path.GetFileNameWithoutExtension(exeName) }
            .Select(Key)
            .Where(k => k.Length >= MinimumFolderNameKey)
            .ToList();
        var candidates = found.Select(vn => new VndbCandidate(vn, MatchesLocal(vn, local) ? VndbEvidence.FolderAndExe : VndbEvidence.TitleOnly));
        return [.. candidates.OrderByDescending(c => c.Evidence)];
    }

    /// <summary>Busca o jogo e os personagens do VNDB e monta o que o projeto guarda.</summary>
    /// <exception cref="VndbLinkException">O VNDB não tem o jogo.</exception>
    /// <exception cref="VndbException">O VNDB não respondeu.</exception>
    public static async Task<VndbLink> BuildAsync(IVndbCatalog catalog, string visualNovelId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var vn = await catalog.GetVisualNovelAsync(visualNovelId, cancellationToken).ConfigureAwait(false)
            ?? throw new VndbLinkException($"O VNDB não tem o jogo {visualNovelId}.");
        var characters = await catalog.GetCharactersAsync(visualNovelId, cancellationToken).ConfigureAwait(false);
        return From(vn, characters);
    }

    /// <summary>O mesmo que <see cref="BuildAsync"/>, a partir do jogo e dos personagens que a tela já tem.</summary>
    public static VndbLink From(VndbVisualNovel vn, VndbResults<VndbCharacter> characters)
    {
        ArgumentNullException.ThrowIfNull(vn);
        ArgumentNullException.ThrowIfNull(characters);
        return new VndbLink(
            vn.Id,
            vn.Title,
            vn.AltTitle,
            VndbText.Clean(vn.Description),
            [.. characters.Items.Select(c => Linked(c, vn.Id))],
            characters.Truncated);
    }

    /// <summary>Liga o jogo ao projeto (<c>vndb.json</c>) e, se o texto já foi extraído, leva os personagens para <c>characters.json</c>.</summary>
    public static async Task<VndbApplyResult> LinkAsync(ProjectFolder project, IVndbCatalog catalog, string visualNovelId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.SaveVndbLink(await BuildAsync(catalog, visualNovelId, cancellationToken).ConfigureAwait(false));
        return ApplyCharacters(project);
    }

    /// <summary>
    /// Casa os nomes do jogo (<see cref="GameCharacters.Names"/>, as chamadas de <c>ES.CHAR.NAME</c>) com os personagens de <c>vndb.json</c> e leva para
    /// <c>characters.json</c> gênero, romaji, apelidos, descrição, id do VNDB e imagem. O que o usuário já escreveu fica: só campos vazios são preenchidos. Sem <c>vndb.json</c> ou
    /// sem texto extraído não faz nada.
    /// </summary>
    /// <exception cref="VndbLinkException"><c>vndb.json</c> inválido.</exception>
    /// <exception cref="GlossaryFileException"><c>characters.json</c> inválido.</exception>
    public static VndbApplyResult ApplyCharacters(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.LoadVndbLink() is { } link ? ApplyCharacters(project, link.Characters) : VndbApplyResult.None;
    }

    /// <summary>
    /// O mesmo que <see cref="ApplyCharacters(ProjectFolder)"/> com os personagens dados, que não precisam vir do <c>vndb.json</c> (a ficha do jogo, por exemplo, traz a descrição em português).
    /// Um campo preenchido fica; <paramref name="replaceDescription"/>, quando dado, diz se a descrição que já está em <c>characters.json</c> (primeiro argumento) deve dar lugar
    /// à do personagem (segundo), o que só vale quando o personagem traz descrição.
    /// </summary>
    /// <exception cref="GlossaryFileException"><c>characters.json</c> inválido.</exception>
    public static VndbApplyResult ApplyCharacters(
        ProjectFolder project, IReadOnlyList<VndbLinkedCharacter> linkedCharacters, Func<string, VndbLinkedCharacter, bool>? replaceDescription = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(linkedCharacters);
        var names = GameCharacters.Names(project.LoadAllText().Values.SelectMany(units => units));
        var matches = Match(names.Select(n => n.Source), linkedCharacters);
        if (matches.Count == 0)
        {
            return VndbApplyResult.None;
        }

        var characters = project.LoadCharacters().ToList();
        var added = 0;
        var filled = 0;
        foreach (var (name, vndb) in matches)
        {
            var index = characters.FindIndex(c => CharacterBook.NameComparer.Equals(c.Source, name));
            var current = index >= 0 ? characters[index] : names.First(n => n.Source == name);
            var next = current with
            {
                Gender = current.Gender == CharacterGender.Unknown ? vndb.Gender : current.Gender,
                Romaji = current.Romaji ?? vndb.Name,
                Aliases = current.Aliases is { Count: > 0 } || vndb.Aliases.Count == 0 ? current.Aliases : vndb.Aliases,
                Description = current.Description is null || (vndb.Description is not null && replaceDescription?.Invoke(current.Description, vndb) == true)
                    ? vndb.Description ?? current.Description
                    : current.Description,
                VndbId = current.VndbId ?? vndb.Id,
                Image = current.Image ?? vndb.Image,
            };
            if (index < 0)
            {
                characters.Add(next);
                added++;
            }
            else if (!SameFields(current, next))
            {
                characters[index] = next;
                filled++;
            }
        }

        if (added + filled > 0)
        {
            project.SaveCharacters(characters);
        }

        return new VndbApplyResult(matches.Count, added, filled);
    }

    /// <summary>
    /// Para cada nome do jogo, o personagem do VNDB que ele é: o nome inteiro na escrita original, depois uma parte dele (sobrenome ou nome), depois um apelido.
    /// A comparação ignora espaço, largura e ponto médio. Nome que empata entre dois personagens do mesmo nível fica de fora, porque casar com o errado
    /// trocaria o gênero de um personagem por outro.
    /// </summary>
    public static IReadOnlyList<(string GameName, VndbLinkedCharacter Character)> Match(IEnumerable<string> gameNames, IReadOnlyList<VndbLinkedCharacter> characters)
    {
        ArgumentNullException.ThrowIfNull(gameNames);
        ArgumentNullException.ThrowIfNull(characters);
        var result = new List<(string, VndbLinkedCharacter)>();
        foreach (var gameName in gameNames)
        {
            var key = Key(gameName);
            if (key.Length == 0)
            {
                continue;
            }

            var best = characters
                .Select(c => (Character: c, Level: LevelOf(c, key)))
                .Where(m => m.Level is not null)
                .GroupBy(m => m.Level!.Value)
                .OrderBy(g => g.Key)
                .FirstOrDefault();
            if (best is not null && best.Count() == 1)
            {
                result.Add((gameName, best.First().Character));
            }
        }

        return result;
    }

    /// <summary>Gênero do VNDB (<c>m</c>, <c>f</c>, <c>o</c>, <c>a</c>) como o <c>characters.json</c> o guarda. Vale o real, não o que aparece sem spoiler, porque a tradução concorda com o real.</summary>
    public static CharacterGender GenderOf(VndbGender? gender) =>
        (gender?.Spoiler ?? gender?.Visible) switch
        {
            "m" => CharacterGender.Male,
            "f" => CharacterGender.Female,
            "o" or "a" => CharacterGender.Other,
            _ => CharacterGender.Unknown,
        };

    private static VndbLinkedCharacter Linked(VndbCharacter c, string visualNovelId) =>
        new(
            c.Id,
            c.Name,
            string.IsNullOrWhiteSpace(c.Original) ? null : c.Original.Trim(),
            [.. c.Aliases.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim())],
            GenderOf(c.Gender),
            VndbText.Clean(c.Description),
            RoleOf(c, visualNovelId),
            VndbImageRef.Portrait(c.Image));

    private static readonly string[] Roles = ["main", "primary", "side", "appears"];

    private static string RoleOf(VndbCharacter c, string visualNovelId) =>
        c.Appearances
            .Where(a => a.VisualNovelId == visualNovelId)
            .Select(a => a.Role)
            .OrderBy(role => Array.IndexOf(Roles, role) is var i and >= 0 ? i : Roles.Length)
            .FirstOrDefault() ?? "appears";

    private static int? LevelOf(VndbLinkedCharacter c, string key)
    {
        if (c.Original is { } original)
        {
            if (Key(original) == key)
            {
                return 0;
            }

            var parts = original.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && parts.Any(p => Key(p) == key))
            {
                return 1;
            }
        }

        return c.Aliases.Any(a => Key(a) == key) ? 2 : null;
    }

    private static bool SameFields(CharacterEntry a, CharacterEntry b) =>
        a.Gender == b.Gender && a.Romaji == b.Romaji && a.Description == b.Description
        && a.VndbId == b.VndbId && a.Image == b.Image
        && (a.Aliases ?? []).SequenceEqual(b.Aliases ?? [], StringComparer.Ordinal);

    private static bool MatchesLocal(VndbVisualNovel vn, List<string> local)
    {
        if (local.Count == 0)
        {
            return false;
        }

        return Names(vn).Select(Key).Where(k => k.Length >= MinimumFolderNameKey)
            .Any(name => local.Any(l => l.Contains(name, StringComparison.Ordinal) || name.Contains(l, StringComparison.Ordinal)));
    }

    private static IEnumerable<string> Names(VndbVisualNovel vn)
    {
        yield return vn.Title;
        if (vn.AltTitle is not null)
        {
            yield return vn.AltTitle;
        }

        foreach (var alias in vn.Aliases)
        {
            yield return alias;
        }

        foreach (var title in vn.Titles)
        {
            yield return title.Title;
            if (title.Latin is not null)
            {
                yield return title.Latin;
            }
        }
    }

    /// <summary>Letras e números do texto na largura normal e em minúsculas: sem espaço, pontuação nem ponto médio.</summary>
    private static string Key(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit))
        {
            sb.Append(char.ToLower(c, CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
