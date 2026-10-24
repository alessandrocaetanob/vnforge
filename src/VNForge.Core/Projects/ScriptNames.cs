namespace VNForge.Core.Projects;

/// <summary>Os nomes de um script em <c>scripts.json</c>: o que a engine leu do jogo e o que o usuário escolheu. Ao menos um dos dois existe.</summary>
/// <param name="Key">Chave do script (<c>yst00012</c>), a mesma de <c>text/&lt;chave&gt;.json</c>.</param>
/// <param name="Detected">Nome legível que a engine achou no jogo (no YU-RIS, o arquivo do caminho na <c>yst_list</c>, como <c>manha</c> para <c>userscript\01_cap1\manha.yst</c>); a extração o atualiza.</param>
/// <param name="Name">Nome que o usuário deu ao script; a extração nunca o troca.</param>
public sealed record ScriptNameEntry(string Key, string? Detected, string? Name);

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

            if (string.IsNullOrWhiteSpace(entry.Detected) && string.IsNullOrWhiteSpace(entry.Name))
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

    /// <summary>Troca o nome do usuário. Texto vazio ou só com espaços volta ao nome da engine; um script sem nenhum nome some da lista.</summary>
    public ScriptNames Rename(string key, string? name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var trimmed = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var detected = _byKey.TryGetValue(key, out var current) ? current.Detected : null;
        var others = Entries.Where(e => !string.Equals(e.Key, key, StringComparison.Ordinal));
        return detected is null && trimmed is null
            ? new ScriptNames(others)
            : new ScriptNames(others.Append(new ScriptNameEntry(key, detected, trimmed)));
    }

    /// <summary>
    /// Aplica o que uma extração leu do jogo (chave para nome legível, nulo quando a engine não sabe). O nome da engine de cada script
    /// passa a ser o novo; o do usuário fica. Quem saiu do jogo perde o nome da engine e só continua na lista se o usuário o tinha renomeado.
    /// </summary>
    public ScriptNames WithDetected(IReadOnlyDictionary<string, string?> detected)
    {
        ArgumentNullException.ThrowIfNull(detected);
        var result = Entries
            .Where(e => e.Name is not null)
            .ToDictionary(e => e.Key, e => e with { Detected = null }, StringComparer.Ordinal);
        foreach (var (key, name) in detected)
        {
            var custom = result.TryGetValue(key, out var existing) ? existing.Name : null;
            var clean = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            if (clean is null && custom is null)
            {
                continue;
            }

            result[key] = new ScriptNameEntry(key, clean, custom);
        }

        return new ScriptNames(result.Values);
    }
}
