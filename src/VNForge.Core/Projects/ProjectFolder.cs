using System.Text;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>Uma pasta de projeto de tradução: <c>vnforge.json</c> mais <c>text/</c>, <c>.cache/</c>, <c>build/</c> e <c>backup/</c>.</summary>
public sealed class ProjectFolder
{
    public const string ConfigFileName = "vnforge.json";

    public const string FormattingFileName = "formatting.json";

    public const string GlossaryFileName = "glossary.json";

    public const string CharactersFileName = "characters.json";

    public const string ScriptNamesFileName = "scripts.json";

    private const string InvalidUtf8Reason = "o arquivo não está em UTF-8 válido.";

    private const string TextFileExtension = ".json";

    private static readonly UTF8Encoding Utf8SemBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding Utf8Estrito = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private const string GitIgnoreContent = "# Gerado pelo VNForge: arquivos do jogo e backups dos originais nunca vão para o git\n.cache/\nbuild/\nbackup/\n";

    private ProjectFolder(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string ConfigPath => Path.Combine(Root, ConfigFileName);

    public string TextDirectory => Path.Combine(Root, "text");

    public string CacheDirectory => Path.Combine(Root, ".cache");

    /// <summary><c>.cache/game</c>: os pacotes do jogo descompactados pelo <c>vnf extract</c>, uma subpasta por pacote.</summary>
    public string GameCacheDirectory => Path.Combine(CacheDirectory, "game");

    /// <summary>Cópia dos <c>text/*.json</c> que a re-extração trocou ou removeu (<c>.cache/text-anterior/</c>); só a da última re-extração que mudou algo.</summary>
    public string TextBackupDirectory => Path.Combine(CacheDirectory, "text-anterior");

    public string BuildDirectory => Path.Combine(Root, "build");

    public string BackupDirectory => Path.Combine(Root, "backup");

    public static ProjectFolder Create(string root, ProjectConfig config)
    {
        var folder = new ProjectFolder(root);
        if (File.Exists(folder.ConfigPath))
        {
            throw new ProjectConfigException($"Já existe um {ConfigFileName} em '{folder.Root}'; o projeto não foi sobrescrito.");
        }

        var json = ProjectConfigSerializer.Serialize(config);
        folder.EnsureDirectories();
        try
        {
            folder.WriteConfig(json, overwrite: false);
            folder.WriteGitIgnoreIfMissing();
        }
        catch (IOException) when (File.Exists(folder.ConfigPath))
        {
            throw new ProjectConfigException($"Já existe um {ConfigFileName} em '{folder.Root}'; o projeto não foi sobrescrito.");
        }

        return folder;
    }

    public static ProjectFolder Open(string root)
    {
        var folder = new ProjectFolder(root);
        if (!File.Exists(folder.ConfigPath))
        {
            throw new ProjectConfigException($"{ConfigFileName} não encontrado em '{folder.Root}'; a pasta não é um projeto do VNForge.");
        }

        return folder;
    }

    public ProjectConfig LoadConfig()
    {
        string json;
        try
        {
            json = Utf8Estrito.GetString(ProjectFileIo.ReadAllBytes(ConfigPath));
        }
        catch (DecoderFallbackException)
        {
            throw new ProjectConfigException("$", InvalidUtf8Reason);
        }

        return ProjectConfigSerializer.Parse(json.TrimStart((char)0xFEFF));
    }

    public void SaveConfig(ProjectConfig config) =>
        WriteConfig(ProjectConfigSerializer.Serialize(config), overwrite: true);

    /// <summary><c>formatting.json</c>, o perfil de formatação aplicado no build. É opcional.</summary>
    public string FormattingPath => Path.Combine(Root, FormattingFileName);

    /// <summary>Lê o <c>formatting.json</c>; sem o arquivo, devolve <see cref="FormattingProfile.Default"/> (nenhuma regra ligada).</summary>
    /// <exception cref="FormattingProfileException">Arquivo fora de UTF-8, malformado ou com valor inválido.</exception>
    public FormattingProfile LoadFormatting()
    {
        if (!File.Exists(FormattingPath))
        {
            return FormattingProfile.Default;
        }

        try
        {
            return FormattingProfileSerializer.Parse(Utf8Estrito.GetString(ProjectFileIo.ReadAllBytes(FormattingPath)).TrimStart((char)0xFEFF));
        }
        catch (DecoderFallbackException)
        {
            throw new FormattingProfileException("$", InvalidUtf8Reason);
        }
    }

    public void SaveFormatting(FormattingProfile profile) =>
        WriteAtomically(FormattingPath, FormattingProfileSerializer.Serialize(profile), overwrite: true);

    /// <summary><c>glossary.json</c>, os termos fixos do projeto. É opcional.</summary>
    public string GlossaryPath => Path.Combine(Root, GlossaryFileName);

    /// <summary><c>characters.json</c>, os personagens do projeto. É opcional.</summary>
    public string CharactersPath => Path.Combine(Root, CharactersFileName);

    /// <summary>Lê o <c>glossary.json</c>; sem o arquivo, devolve uma lista vazia.</summary>
    /// <exception cref="GlossaryFileException">Arquivo fora de UTF-8, malformado ou com valor inválido.</exception>
    public IReadOnlyList<GlossaryTerm> LoadGlossary() =>
        File.Exists(GlossaryPath) ? GlossarySerializer.Parse(ReadGlossaryFile(GlossaryPath), GlossaryPath) : [];

    /// <summary>Grava o <c>glossary.json</c> (UTF-8 sem BOM, LF), na ordem recebida; a gravação é atômica.</summary>
    /// <exception cref="GlossaryFileException">Termo original vazio ou repetido; nada é gravado.</exception>
    public void SaveGlossary(IReadOnlyList<GlossaryTerm> terms) =>
        WriteAtomically(GlossaryPath, GlossarySerializer.Serialize(terms, GlossaryPath), overwrite: true);

    /// <summary>Lê o <c>characters.json</c>; sem o arquivo, devolve uma lista vazia.</summary>
    /// <exception cref="GlossaryFileException">Arquivo fora de UTF-8, malformado ou com valor inválido.</exception>
    public IReadOnlyList<CharacterEntry> LoadCharacters() =>
        File.Exists(CharactersPath) ? CharacterSerializer.Parse(ReadGlossaryFile(CharactersPath), CharactersPath) : [];

    /// <summary>Grava o <c>characters.json</c> (UTF-8 sem BOM, LF), na ordem recebida; a gravação é atômica.</summary>
    /// <exception cref="GlossaryFileException">Nome original vazio ou repetido; nada é gravado.</exception>
    public void SaveCharacters(IReadOnlyList<CharacterEntry> characters) =>
        WriteAtomically(CharactersPath, CharacterSerializer.Serialize(characters, CharactersPath), overwrite: true);

    /// <summary><c>scripts.json</c>, os nomes legíveis dos scripts (o que a engine leu do jogo e o que o usuário escolheu). É opcional.</summary>
    public string ScriptNamesPath => Path.Combine(Root, ScriptNamesFileName);

    /// <summary>Lê o <c>scripts.json</c>; sem o arquivo, devolve <see cref="ScriptNames.Empty"/>.</summary>
    /// <exception cref="GlossaryFileException">Arquivo fora de UTF-8, malformado ou com valor inválido.</exception>
    public ScriptNames LoadScriptNames() =>
        File.Exists(ScriptNamesPath) ? ScriptNamesSerializer.Parse(ReadGlossaryFile(ScriptNamesPath), ScriptNamesPath) : ScriptNames.Empty;

    /// <summary>Grava o <c>scripts.json</c> (UTF-8 sem BOM, LF), ordenado pela chave; a gravação é atômica.</summary>
    public void SaveScriptNames(ScriptNames names) =>
        WriteAtomically(ScriptNamesPath, ScriptNamesSerializer.Serialize(names), overwrite: true);

    /// <summary>
    /// Lê o <c>scripts.json</c>, aplica <paramref name="change"/> e grava o resultado, tudo sob a trava do projeto (<see cref="ProjectTextLock"/>): duas
    /// mudanças seguidas dentro do app (a extração e um agrupamento, por exemplo) não perdem uma à outra. Não grava quando nada mudou, para que ida e volta
    /// sem alteração deixe o arquivo como estava. A trava vale dentro do processo; a CLI e o app rodando ao mesmo tempo não se travam.
    /// </summary>
    /// <returns>Os nomes depois da mudança.</returns>
    /// <exception cref="GlossaryFileException">O <c>scripts.json</c> atual é inválido; nada é gravado.</exception>
    public ScriptNames UpdateScriptNames(Func<ScriptNames, ScriptNames> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (ProjectTextLock.For(this))
        {
            var current = LoadScriptNames();
            var updated = change(current);
            if (ScriptNamesSerializer.Serialize(updated) != ScriptNamesSerializer.Serialize(current))
            {
                SaveScriptNames(updated);
            }

            return updated;
        }
    }

    /// <summary>Dá ao script de chave <paramref name="key"/> o nome <paramref name="name"/> e grava o <c>scripts.json</c>; texto vazio volta ao nome que a engine leu do jogo.</summary>
    /// <exception cref="GlossaryFileException">O <c>scripts.json</c> atual é inválido; nada é gravado.</exception>
    public void RenameScript(string key, string? name) =>
        UpdateScriptNames(names => names.Rename(key, name));

    private static string ReadGlossaryFile(string path)
    {
        try
        {
            return Utf8Estrito.GetString(ProjectFileIo.ReadAllBytes(path)).TrimStart((char)0xFEFF);
        }
        catch (DecoderFallbackException ex)
        {
            throw new GlossaryFileException(path, InvalidUtf8Reason, ex);
        }
    }

    /// <summary><c>.cache/state.json</c>: hashes dos originais do jogo e estado de cada etapa. Apagar o <c>.cache/</c> zera o estado.</summary>
    public string StatePath => Path.Combine(CacheDirectory, "state.json");

    /// <summary>Lê o <c>state.json</c>; sem o arquivo, devolve <see cref="PipelineState.Empty"/>.</summary>
    /// <exception cref="VNForgeException">Arquivo fora de UTF-8, malformado ou de uma versão que esta não conhece.</exception>
    public PipelineState LoadState()
    {
        if (!File.Exists(StatePath))
        {
            return PipelineState.Empty;
        }

        try
        {
            return PipelineState.Parse(Utf8Estrito.GetString(ProjectFileIo.ReadAllBytes(StatePath)).TrimStart((char)0xFEFF), StatePath);
        }
        catch (DecoderFallbackException ex)
        {
            throw new VNForgeException($"O estado do pipeline não está em UTF-8 válido: {StatePath}. Apague o .cache/ para recomeçar.", ex);
        }
    }

    /// <summary>Grava o <c>state.json</c> (UTF-8 sem BOM, LF). Com o mesmo conteúdo já no disco, não toca no arquivo.</summary>
    public void SaveState(PipelineState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var json = state.Serialize();
        if (File.Exists(StatePath) && ProjectFileIo.ReadAllBytes(StatePath).AsSpan().SequenceEqual(Utf8SemBom.GetBytes(json)))
        {
            return;
        }

        Directory.CreateDirectory(CacheDirectory);
        WriteAtomically(StatePath, json, overwrite: true);
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(TextDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(BuildDirectory);
        Directory.CreateDirectory(BackupDirectory);
    }

    private void WriteGitIgnoreIfMissing()
    {
        var path = Path.Combine(Root, ".gitignore");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, GitIgnoreContent, Utf8SemBom);
        }
    }

    /// <summary>Caminho de <c>text/&lt;chave&gt;.json</c>, o arquivo das unidades do script de chave <paramref name="key"/>.</summary>
    /// <exception cref="ArgumentException">Chave vazia, com maiúscula, separador de pasta, <c>:</c>, caractere inválido em nome de arquivo ou terminada em ponto.</exception>
    public string TextPath(string key)
    {
        ValidateKey(key);
        return Path.Combine(TextDirectory, key + TextFileExtension);
    }

    /// <summary>
    /// Grava as unidades do script de chave <paramref name="key"/> em <c>text/&lt;chave&gt;.json</c> (UTF-8 sem BOM, LF), na ordem recebida.
    /// Gravar o mesmo conteúdo duas vezes gera arquivos idênticos. A gravação é atômica: o arquivo anterior só é trocado quando o novo está completo.
    /// </summary>
    /// <exception cref="ArgumentException">Chave inválida (ver <see cref="TextPath"/>).</exception>
    /// <exception cref="TextFileException">Id que não começa com <c>chave:</c> ou repetido; nada é gravado.</exception>
    public void SaveText(string key, IReadOnlyList<TextUnit> units)
    {
        var path = TextPath(key);
        TextFileSerializer.ValidateIds(units, key, path);
        var json = TextFileSerializer.Serialize(units, path);
        Directory.CreateDirectory(TextDirectory);
        WriteAtomically(path, json, overwrite: true);
    }

    /// <summary>Lê as unidades do script de chave <paramref name="key"/>.</summary>
    /// <exception cref="ArgumentException">Chave inválida (ver <see cref="TextPath"/>).</exception>
    /// <exception cref="TextFileException">Arquivo ausente, fora de UTF-8, fora do formato ou com ids de outro script.</exception>
    public IReadOnlyList<TextUnit> LoadText(string key)
    {
        var path = TextPath(key);
        return TextFileSerializer.Parse(ReadTextFile(path), path, key);
    }

    /// <summary>Lê todos os <c>text/*.json</c> da pasta, ordenados pela chave (nome do arquivo sem extensão, em minúsculas). Pasta <c>text/</c> ausente ou vazia dá um resultado vazio.</summary>
    /// <exception cref="TextFileException">Algum arquivo inválido, ou dois arquivos com a mesma chave.</exception>
    public IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> LoadAllText()
    {
        if (!Directory.Exists(TextDirectory))
        {
            return new Dictionary<string, IReadOnlyList<TextUnit>>();
        }

        return LoadTextFiles(Directory.EnumerateFiles(TextDirectory).Where(IsTextFile));
    }

    /// <summary>
    /// Lê arquivos de texto pelo caminho, ordenados pela chave de cada um (nome sem extensão, em minúsculas). Dois arquivos com a mesma chave
    /// (por exemplo <c>A.json</c> e <c>a.json</c> num sistema que diferencia maiúsculas) é erro, porque os ids de um colidiriam com os do outro.
    /// </summary>
    /// <exception cref="TextFileException">Chave repetida ou arquivo inválido.</exception>
    public static IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> LoadTextFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var byKey = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            var key = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (!byKey.TryAdd(key, path))
            {
                throw new TextFileException(path, $"a chave '{key}' já vem de '{byKey[key]}'; dois scripts não podem ter a mesma chave.");
            }
        }

        var result = new SortedDictionary<string, IReadOnlyList<TextUnit>>(StringComparer.Ordinal);
        foreach (var (key, path) in byKey)
        {
            result[key] = TextFileSerializer.Parse(ReadTextFile(path), path, key);
        }

        return result;
    }

    private static bool IsTextFile(string path) =>
        string.Equals(Path.GetExtension(path), TextFileExtension, StringComparison.OrdinalIgnoreCase);

    private static string ReadTextFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new TextFileException(path, "arquivo não encontrado.");
        }

        try
        {
            return Utf8Estrito.GetString(ProjectFileIo.ReadAllBytes(path)).TrimStart((char)0xFEFF);
        }
        catch (DecoderFallbackException ex)
        {
            throw new TextFileException(path, InvalidUtf8Reason, ex);
        }
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key != key.Trim()
            || !string.Equals(key, key.ToLowerInvariant(), StringComparison.Ordinal)
            || key.EndsWith('.')
            || key.Contains(':')
            || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || key.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException($"A chave de script '{key}' não serve de nome de arquivo: use minúsculas, sem pasta, sem ':' e sem ponto no fim.", nameof(key));
        }
    }

    private void WriteConfig(string json, bool overwrite) =>
        WriteAtomically(ConfigPath, json, overwrite);

    private static void WriteAtomically(string path, string content, bool overwrite)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, content, Utf8SemBom);
            ProjectFileIo.Move(temporary, path, overwrite);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
