using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using VNForge.Core.Library;
using VNForge.Core.Text;

namespace VNForge.Core.Factsheet;

/// <summary>
/// As fichas em disco, na pasta <c>sheets</c> da pasta de configuração do usuário, ao lado do <c>library.json</c>: um arquivo por jogo do VNDB (<c>games/v17.json</c>), um por
/// personagem (<c>characters/c123.json</c>, compartilhado pelos jogos em que ele aparece) e <c>terms.json</c> com os nomes de tag e de traço traduzidos. Ficam fora do
/// <c>library.json</c> para que varrer ou casar a biblioteca nunca toque nelas, e para que ler a lista não leia todos os personagens.
/// Um arquivo ilegível não vira ficha vazia: a leitura recusa, para a próxima gravação não o sobrescrever. As gravações vão por <see cref="Update{T}"/>,
/// que trava a pasta (<c>.lock</c>) e relê antes de gravar; a leitura sozinha não trava, porque cada arquivo é trocado por inteiro.
/// </summary>
public sealed partial class SheetStore(string directory)
{
    public const string FolderName = "sheets";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Action<int> _wait = Thread.Sleep;

    public string Root { get; } = Path.Combine(directory, FolderName);

    public string TermsPath => Path.Combine(Root, "terms.json");

    public string GamePath(string visualNovelId) => Path.Combine(Root, "games", RequireId(visualNovelId, 'v') + ".json");

    public string CharacterPath(string characterId) => Path.Combine(Root, "characters", RequireId(characterId, 'c') + ".json");

    /// <summary>A ficha do jogo, ou nula quando ainda não foi buscada.</summary>
    /// <exception cref="LibraryException">O id não é de um jogo do VNDB, ou o arquivo existe e não pôde ser lido.</exception>
    public GameSheet? LoadGame(string visualNovelId) =>
        Read<GameSheet>(GamePath(visualNovelId), "a ficha do jogo", sheet =>
            string.IsNullOrWhiteSpace(sheet.VisualNovelId) || sheet.Title is null
                ? throw new LibraryException("falta o id ou o título do jogo.")
                : sheet with { Tags = sheet.Tags ?? [], CharacterIds = sheet.CharacterIds ?? [] });

    /// <exception cref="LibraryException">O arquivo não pôde ser gravado.</exception>
    public void SaveGame(GameSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        Write(GamePath(sheet.VisualNovelId), sheet, "A ficha do jogo");
    }

    /// <summary>O personagem, ou nulo quando nenhuma ficha o trouxe.</summary>
    /// <exception cref="LibraryException">O id não é de um personagem do VNDB, ou o arquivo existe e não pôde ser lido.</exception>
    public CharacterSheet? LoadCharacter(string characterId) =>
        Read<CharacterSheet>(CharacterPath(characterId), "o personagem", Normalize);

    /// <summary>Os personagens que existem, na ordem dos ids pedidos; id sem arquivo é pulado.</summary>
    public IReadOnlyList<CharacterSheet> LoadCharacters(IEnumerable<string> characterIds)
    {
        ArgumentNullException.ThrowIfNull(characterIds);
        return [.. characterIds.Select(LoadCharacter).OfType<CharacterSheet>()];
    }

    /// <summary>Todos os personagens guardados, por id. A página de personagens (#558) parte daqui.</summary>
    public IReadOnlyList<CharacterSheet> LoadAllCharacters()
    {
        var folder = Path.Combine(Root, "characters");
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var ids = Directory.EnumerateFiles(folder, "c*.json").Select(Path.GetFileNameWithoutExtension).Where(id => id is not null && IdPattern('c').IsMatch(id)).Order(StringComparer.Ordinal);
        return LoadCharacters(ids!);
    }

    /// <exception cref="LibraryException">O arquivo não pôde ser gravado.</exception>
    public void SaveCharacter(CharacterSheet character)
    {
        ArgumentNullException.ThrowIfNull(character);
        Write(CharacterPath(character.Id), character, "O personagem");
    }

    /// <summary>As traduções de tags e traços; vazias quando o arquivo não existe.</summary>
    /// <exception cref="LibraryException">O arquivo existe e não pôde ser lido ou não segue o formato.</exception>
    public SheetTerms LoadTerms()
    {
        var terms = new SheetTerms();
        var file = Read<TermsFile>(TermsPath, "as traduções de tags e traços", f => f);
        foreach (var entry in file?.Terms ?? [])
        {
            if (entry is null || !Enum.TryParse<SheetTermKind>(entry.Kind, ignoreCase: true, out var kind) || string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Source))
            {
                throw new LibraryException($"O arquivo '{TermsPath}' tem um termo sem tipo, id ou original.");
            }

            terms.Put(new SheetTerm(kind, entry.Id, entry.Context, new SheetText(entry.Source, entry.Target, entry.Status, entry.Outdated)));
        }

        return terms;
    }

    /// <exception cref="LibraryException">O arquivo não pôde ser gravado.</exception>
    public void SaveTerms(SheetTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        List<TermEntry?> entries = [.. terms.All.Select(t => new TermEntry(SheetTerms.KindName(t.Kind), t.Id, t.Context, t.Text.Source, t.Text.Target, t.Text.Status, t.Text.Outdated))];
        Write(TermsPath, new TermsFile(entries), "As traduções de tags e traços");
    }

    /// <summary>
    /// Aplica <paramref name="change"/> com a pasta travada: quem gravou no meio (outro <c>vnf</c>, o app) não perde o que gravou, desde que <paramref name="change"/> leia
    /// o que vai mudar de dentro dela. Quem traduz um lote longo chama <c>Update</c> por lote, e não segura a trava até o fim.
    /// </summary>
    /// <exception cref="LibraryException">Outro processo segurou a pasta por mais de <see cref="LibraryFile.LockTimeout"/>.</exception>
    public T Update<T>(Func<SheetStore, T> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Directory.CreateDirectory(Root);
        using var gate = FileLock.Acquire(Path.Combine(Root, ".lock"), LibraryFile.LockTimeout, $"As fichas ('{Root}') estão em uso por outro processo. Tente de novo em instantes.");
        return change(this);
    }

    /// <inheritdoc cref="Update{T}(Func{SheetStore, T})"/>
    public void Update(Action<SheetStore> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Update(store =>
        {
            change(store);
            return 0;
        });
    }

    private static CharacterSheet Normalize(CharacterSheet character) =>
        string.IsNullOrWhiteSpace(character.Id) || character.Name is null
            ? throw new LibraryException("falta o id ou o nome do personagem.")
            : character with { Aliases = character.Aliases ?? [], Traits = character.Traits ?? [], Appearances = character.Appearances ?? [] };

    private static string RequireId(string id, char prefix) =>
        id is not null && IdPattern(prefix).IsMatch(id)
            ? id
            : throw new LibraryException($"'{id}' não é um id do VNDB de {(prefix == 'v' ? "jogo" : "personagem")} (esperado {prefix}1234).");

    private T? Read<T>(string path, string what, Func<T, T> check)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope<T>>(File.ReadAllText(path), Json);
            if (envelope?.Data is null)
            {
                throw new LibraryException($"O arquivo '{path}' está vazio.");
            }

            if (envelope.Version != CurrentVersion)
            {
                throw new LibraryException($"O arquivo '{path}' é da versão {envelope.Version} das fichas; esta versão do VNForge lê a {CurrentVersion}.");
            }

            return check(envelope.Data);
        }
        catch (JsonException e)
        {
            throw new LibraryException($"O arquivo '{path}' ({what}) não está no formato esperado: {e.Message}", e);
        }
        catch (LibraryException e) when (!e.Message.Contains(path, StringComparison.Ordinal))
        {
            throw new LibraryException($"O arquivo '{path}' ({what}): {e.Message}", e);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException($"O arquivo '{path}' não pôde ser lido: {e.Message}", e);
        }
    }

    private void Write<T>(string path, T data, string what)
    {
        try
        {
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(new Envelope<T>(CurrentVersion, data), Json), _wait);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException($"{what} não pôde ser gravado em '{path}': {e.Message}", e);
        }
    }

    private static Regex IdPattern(char prefix) => prefix == 'v' ? GameId() : CharacterId();

    [GeneratedRegex(@"^v\d{1,9}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex GameId();

    [GeneratedRegex(@"^c\d{1,9}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex CharacterId();

    private sealed record Envelope<T>(int Version, T? Data);

    private sealed record TermsFile(List<TermEntry?>? Terms);

    private sealed record TermEntry(string? Kind, string? Id, string? Context, string? Source, string? Target, TranslationStatus Status, bool Outdated);
}
