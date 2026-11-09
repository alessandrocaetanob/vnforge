using System.Globalization;

namespace VNForge.Core.Library;

/// <summary>Uma tag da pessoa. <paramref name="Name"/> já vem normalizada (<see cref="UserTagName.Normalize"/>) e é a chave; <paramref name="Color"/> é <c>#rrggbb</c> ou nulo.</summary>
public sealed record UserTag(string Name, string? Color);

/// <summary>As regras de nome e de cor das tags próprias.</summary>
public static class UserTagName
{
    public const int MaxLength = 40;

    /// <summary>Prefixos que o filtro usa para dizer de quem é a tag (<c>vndb:</c>, <c>minha:</c>). Por isso o nome de tag própria não leva dois-pontos.</summary>
    public const char ScopeSeparator = ':';

    /// <summary>Tira espaços das pontas, junta espaços repetidos em um e põe em minúsculas, então <c>" Pra   TRADUZIR "</c> e <c>pra traduzir</c> são a mesma tag.</summary>
    /// <exception cref="LibraryException">Nome vazio, longo demais, com caractere de controle ou com dois-pontos.</exception>
    public static string Normalize(string? name)
    {
        var text = Collapse(name);
        if (text.Length == 0)
        {
            throw new LibraryException("O nome da tag não pode ficar vazio.");
        }

        if (text.Length > MaxLength)
        {
            throw new LibraryException($"O nome da tag passa de {MaxLength} caracteres: '{text}'.");
        }

        if (text.Any(char.IsControl))
        {
            throw new LibraryException("O nome da tag não pode ter caractere de controle.");
        }

        if (text.Contains(ScopeSeparator, StringComparison.Ordinal))
        {
            throw new LibraryException($"O nome da tag não pode ter dois-pontos ('{text}'): o filtro usa vndb: e minha: para dizer de quem é a tag.");
        }

        return text;
    }

    /// <summary>A mesma limpeza de <see cref="Normalize"/> sem as regras de nome: serve para comparar com o nome de uma tag do VNDB.</summary>
    public static string Collapse(string? name) =>
        string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>A cor como <c>#rrggbb</c> em minúsculas; nulo quando não há cor. Aceita com ou sem <c>#</c>.</summary>
    /// <exception cref="LibraryException">A cor não tem seis dígitos hexadecimais.</exception>
    public static string? NormalizeColor(string? color)
    {
        var text = color?.Trim().TrimStart('#');
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length != 6 || !text.All(char.IsAsciiHexDigit))
        {
            throw new LibraryException($"A cor '{color!.Trim()}' não é válida. Use seis dígitos hexadecimais, como #ff8800.");
        }

        return "#" + text.ToLower(CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// As tags próprias do usuário: o registro (nome e cor) e o conjunto de tags de cada jogo da biblioteca, mais o filtro salvo. Em memória; <see cref="UserTagsFile"/> lê e grava.
/// Valem só para jogos e ficam no banco local, nunca vão para o VNDB. Renomear, mesclar e apagar atualizam os jogos e o filtro salvo juntos.
/// </summary>
public sealed class UserTags
{
    private readonly Dictionary<string, UserTag> _tags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SortedSet<string>> _byGame = new(StringComparer.Ordinal);

    /// <summary>A combinação de tags que a Biblioteca aplica ao abrir. Vazia quando a pessoa não salvou nenhuma.</summary>
    public LibraryTagFilter SavedFilter { get; set; } = LibraryTagFilter.Empty;

    /// <summary>Todas as tags, por nome.</summary>
    public IReadOnlyList<UserTag> Tags => [.. _tags.Values.OrderBy(t => t.Name, StringComparer.Ordinal)];

    /// <summary>Os ids de jogo que têm alguma tag, por id.</summary>
    public IReadOnlyList<string> GameIds => [.. _byGame.Keys.Order(StringComparer.Ordinal)];

    public bool TryGet(string name, out UserTag tag) => _tags.TryGetValue(UserTagName.Normalize(name), out tag!);

    /// <exception cref="LibraryException">A tag não existe.</exception>
    public UserTag Get(string name) =>
        TryGet(name, out var tag) ? tag : throw new LibraryException($"A tag '{UserTagName.Normalize(name)}' não existe. Veja as tags com vnf library tag list.");

    /// <summary>Cria a tag, ou muda a cor quando ela já existe e <paramref name="color"/> foi dada. Sem cor, a cor atual fica.</summary>
    public UserTag Define(string name, string? color = null)
    {
        var key = UserTagName.Normalize(name);
        var normalizedColor = UserTagName.NormalizeColor(color);
        var tag = _tags.TryGetValue(key, out var current)
            ? current with { Color = normalizedColor ?? current.Color }
            : new UserTag(key, normalizedColor);
        _tags[key] = tag;
        return tag;
    }

    /// <summary>As tags do jogo, por nome.</summary>
    public IReadOnlyList<string> TagsOf(string gameId) =>
        _byGame.TryGetValue(gameId, out var names) ? [.. names] : [];

    public bool Has(string gameId, string name) =>
        _byGame.TryGetValue(gameId, out var names) && names.Contains(UserTagName.Collapse(name));

    /// <summary>Os ids dos jogos que usam a tag.</summary>
    /// <exception cref="LibraryException">A tag não existe.</exception>
    public IReadOnlyList<string> GamesWith(string name)
    {
        var key = Get(name).Name;
        return [.. _byGame.Where(pair => pair.Value.Contains(key)).Select(pair => pair.Key).Order(StringComparer.Ordinal)];
    }

    /// <summary>Põe a tag no jogo e cria a tag se ela ainda não existe. Devolve a tag.</summary>
    public UserTag Add(string gameId, string name, string? color = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        var tag = Define(name, color);
        Assign(gameId, tag.Name);
        return tag;
    }

    /// <summary>Tira a tag do jogo; a tag continua no registro. Devolve se o jogo tinha a tag.</summary>
    /// <exception cref="LibraryException">A tag não existe.</exception>
    public bool Remove(string gameId, string name)
    {
        var key = Get(name).Name;
        if (!_byGame.TryGetValue(gameId, out var names) || !names.Remove(key))
        {
            return false;
        }

        if (names.Count == 0)
        {
            _byGame.Remove(gameId);
        }

        return true;
    }

    /// <summary>
    /// Troca o nome da tag em todos os jogos e no filtro salvo. Se <paramref name="to"/> já existe, só mescla com <paramref name="merge"/>: os jogos das duas ficam
    /// com a de destino, que mantém a cor (ou ganha a da origem se não tinha). Devolve quantos jogos tinham a tag de origem.
    /// </summary>
    /// <exception cref="LibraryException">A origem não existe, ou o destino existe e <paramref name="merge"/> é falso.</exception>
    public int Rename(string from, string to, bool merge = false)
    {
        var source = Get(from);
        var target = UserTagName.Normalize(to);
        var games = GamesWith(source.Name);
        if (target == source.Name)
        {
            return games.Count;
        }

        var existing = _tags.TryGetValue(target, out var destination);
        if (existing && !merge)
        {
            throw new LibraryException($"A tag '{target}' já existe. Use --merge para juntar '{source.Name}' nela.");
        }

        _tags.Remove(source.Name);
        _tags[target] = new UserTag(target, existing ? destination!.Color ?? source.Color : source.Color);
        foreach (var id in games)
        {
            var names = _byGame[id];
            names.Remove(source.Name);
            names.Add(target);
        }

        SavedFilter = SavedFilter.Renamed(source.Name, target);
        return games.Count;
    }

    /// <summary>Apaga a tag do registro, de todos os jogos e do filtro salvo. Devolve quantos jogos a tinham.</summary>
    /// <exception cref="LibraryException">A tag não existe.</exception>
    public int Delete(string name)
    {
        var key = Get(name).Name;
        var games = GamesWith(key);
        foreach (var id in games)
        {
            Remove(id, key);
        }

        _tags.Remove(key);
        SavedFilter = SavedFilter.Without(key);
        return games.Count;
    }

    private void Assign(string gameId, string key)
    {
        if (!_byGame.TryGetValue(gameId, out var names))
        {
            names = new SortedSet<string>(StringComparer.Ordinal);
            _byGame[gameId] = names;
        }

        names.Add(key);
    }

    internal void AssignExisting(string gameId, string name)
    {
        var tag = Get(name);
        Assign(gameId, tag.Name);
    }
}
