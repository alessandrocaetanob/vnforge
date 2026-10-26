namespace VNForge.Core.Projects;

/// <summary>Os nomes de um script em <c>scripts.json</c>: o que a engine leu do jogo e o que o usuário escolheu. Ao menos um dos dois existe.</summary>
/// <param name="Key">Chave do script (<c>yst00012</c>), a mesma de <c>text/&lt;chave&gt;.json</c>.</param>
/// <param name="Detected">Nome legível que a engine achou no jogo (no YU-RIS, o arquivo do caminho na <c>yst_list</c>, como <c>manha</c> para <c>userscript\01_cap1\manha.yst</c>); a extração o atualiza.</param>
/// <param name="Name">Nome que o usuário deu ao script; a extração nunca o troca.</param>
public sealed record ScriptNameEntry(string Key, string? Detected, string? Name)
{
    /// <summary>Pasta do script no jogo (no YU-RIS, a do caminho na <c>yst_list</c>, como <c>userscript/01_cap1</c>, com <c>/</c>); a extração a atualiza. É de onde <see cref="ScriptGrouping"/> propõe os capítulos.</summary>
    public string? Folder { get; init; }

    /// <summary>Grupo que o usuário escolheu para o script ("Manhã de chuva"); a extração nunca o troca e ele vale mais que o proposto pela pasta.</summary>
    public string? Group { get; init; }

    internal bool IsEmpty => string.IsNullOrWhiteSpace(Detected) && string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Group) && string.IsNullOrWhiteSpace(Folder);
}

/// <summary>O que a engine leu do jogo sobre um script: o nome legível e a pasta onde ele está.</summary>
public sealed record DetectedScript(string? Name, string? Folder = null);

/// <summary>
/// Os nomes legíveis dos scripts do projeto (<c>scripts.json</c>). O nome para mostrar é o do usuário, quando há, ou o que a engine leu do jogo;
/// sem nenhum dos dois quem mostra o script usa a chave. Imutável: cada alteração devolve uma cópia.
/// </summary>
public sealed class ScriptNames
{
    private readonly Dictionary<string, ScriptNameEntry> _byKey;

    private ScriptNames(IEnumerable<ScriptNameEntry> entries)
    {
        Entries = entries.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
        _byKey = Entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
    }

    public static ScriptNames Empty { get; } = new([]);

    /// <summary>As entradas, ordenadas pela chave.</summary>
    public IReadOnlyList<ScriptNameEntry> Entries { get; }

    /// <summary>Monta a lista a partir das entradas dadas, em qualquer ordem.</summary>
    /// <exception cref="ArgumentException">Chave repetida, vazia ou com maiúscula, ou entrada sem nenhum nome.</exception>
    public static ScriptNames From(IEnumerable<ScriptNameEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var list = entries.ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in list)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Key != entry.Key.Trim().ToLowerInvariant())
            {
                throw new ArgumentException($"A chave de script '{entry.Key}' é inválida: use minúsculas e sem espaços nas pontas.", nameof(entries));
            }

            if (entry.IsEmpty)
            {
                throw new ArgumentException($"O script '{entry.Key}' está sem nome.", nameof(entries));
            }

            if (!seen.Add(entry.Key))
            {
                throw new ArgumentException($"O script '{entry.Key}' aparece mais de uma vez.", nameof(entries));
            }
        }

        return new ScriptNames(list);
    }

    /// <summary>O nome para mostrar: o do usuário, senão o da engine; nulo quando o script não tem nenhum.</summary>
    public string? DisplayName(string key) =>
        _byKey.TryGetValue(key, out var entry) ? entry.Name ?? entry.Detected : null;

    /// <summary>O nome que o usuário deu ao script, ou nulo.</summary>
    public string? CustomName(string key) =>
        _byKey.TryGetValue(key, out var entry) ? entry.Name : null;

    /// <summary>O nome que a engine leu do jogo para o script (<c>01_prologo</c>), sem o do usuário; nulo quando ela não o conhece.</summary>
    public string? DetectedName(string key) =>
        _byKey.TryGetValue(key, out var entry) ? entry.Detected : null;

    /// <summary>A pasta do script no jogo, ou nula quando a engine não a conhece.</summary>
    public string? Folder(string key) =>
        _byKey.TryGetValue(key, out var entry) ? entry.Folder : null;

    /// <summary>O grupo que o usuário escolheu para o script, ou nulo.</summary>
    public string? Group(string key) =>
        _byKey.TryGetValue(key, out var entry) ? entry.Group : null;

    /// <summary>Troca o nome do usuário. Texto vazio ou só com espaços volta ao nome da engine; um script sem nome, pasta nem grupo some da lista.</summary>
    public ScriptNames Rename(string key, string? name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Change(key, current => current with { Name = Clean(name) });
    }

    /// <summary>Põe o script no grupo dado. Texto vazio tira a escolha do usuário e o script volta ao grupo que a pasta propõe.</summary>
    public ScriptNames WithGroup(string key, string? group)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Change(key, current => current with { Group = Clean(group) });
    }

    /// <summary>
    /// Aplica o que uma extração leu do jogo (chave para nome legível, nulo quando a engine não sabe). O nome da engine de cada script
    /// passa a ser o novo; o do usuário fica. Quem saiu do jogo perde o nome da engine e só continua na lista se o usuário o tinha renomeado ou agrupado.
    /// </summary>
    public ScriptNames WithDetected(IReadOnlyDictionary<string, string?> detected)
    {
        ArgumentNullException.ThrowIfNull(detected);
        return WithDetected(detected.ToDictionary(p => p.Key, p => new DetectedScript(p.Value), StringComparer.Ordinal));
    }

    /// <summary>Como <see cref="WithDetected(IReadOnlyDictionary{string, string})"/>, com a pasta de cada script também.</summary>
    public ScriptNames WithDetected(IReadOnlyDictionary<string, DetectedScript> detected)
    {
        ArgumentNullException.ThrowIfNull(detected);
        var result = Entries
            .Where(e => e.Name is not null || e.Group is not null)
            .ToDictionary(e => e.Key, e => e with { Detected = null, Folder = null }, StringComparer.Ordinal);
        foreach (var (key, script) in detected)
        {
            result.TryGetValue(key, out var existing);
            var entry = new ScriptNameEntry(key, Clean(script.Name), existing?.Name) { Folder = Clean(script.Folder), Group = existing?.Group };
            if (!entry.IsEmpty)
            {
                result[key] = entry;
            }
        }

        return new ScriptNames(result.Values);
    }

    private ScriptNames Change(string key, Func<ScriptNameEntry, ScriptNameEntry> change)
    {
        var changed = change(_byKey.TryGetValue(key, out var found) ? found : new ScriptNameEntry(key, null, null));
        var others = Entries.Where(e => !string.Equals(e.Key, key, StringComparison.Ordinal));
        return new ScriptNames(changed.IsEmpty ? others : others.Append(changed));
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
