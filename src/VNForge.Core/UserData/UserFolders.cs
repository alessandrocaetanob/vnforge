using System.Text.Json;
using VNForge.Core.Library;

namespace VNForge.Core.UserData;

/// <summary>De onde veio a pasta que vale, da fonte mais forte para a mais fraca.</summary>
public enum UserFolderSource
{
    /// <summary><c>VNFORGE_CONFIG_DIR</c> ou <c>VNFORGE_CACHE_DIR</c>: vale para o CLI em CI, scripts e testes, e mais que o app.</summary>
    Environment,

    /// <summary>O ponteiro <c>pasta.json</c> que o app grava ao confirmar a primeira pergunta ou uma troca.</summary>
    Pointer,

    /// <summary>O padrão do Windows: <c>%APPDATA%/VNForge</c> e <c>%LOCALAPPDATA%/VNForge</c>.</summary>
    Default,
}

/// <summary>As duas pastas do usuário: os dados e a configuração, e o cache com as capturas e os registros.</summary>
public enum UserFolderKind
{
    Config,
    Cache,
}

public sealed record UserFolder(string Path, UserFolderSource Source);

/// <summary>Uma pasta escolhida que não pode ser usada agora; <see cref="Message"/> é em português e vai direto à pessoa.</summary>
public sealed record UserFolderProblem(UserFolderKind Kind, UserFolderSource Source, string Path, string Message);

/// <summary>
/// Onde o VNForge guarda o que é do usuário, achado por uma regra só (o app e o <c>vnf</c> usam o mesmo Core). Ordem de busca, para cada uma das duas pastas:
/// a variável de ambiente (<see cref="ConfigVariable"/>, <see cref="CacheVariable"/>), o ponteiro <c>pasta.json</c> em <c>%LOCALAPPDATA%/VNForge</c> e o padrão do Windows.
/// O ponteiro fica no Local, e não no Roaming, porque o caminho vale por máquina. As chaves (<c>apikey.dat</c>, <c>vndb-token.dat</c>) ficam na pasta de dados, cifradas pelo
/// DPAPI da conta, e se movem com ela. Achar a pasta nunca cria nem grava nada: quem grava o ponteiro é <see cref="UserFolderMover"/>.
/// </summary>
public sealed class UserFolders
{
    public const string ConfigVariable = "VNFORGE_CONFIG_DIR";

    public const string CacheVariable = "VNFORGE_CACHE_DIR";

    public const string PointerFileName = "pasta.json";

    /// <summary>Quem já usou o app deixou ao menos um destes na pasta padrão; sem nenhum, nem variável nem ponteiro, a pergunta da primeira abertura aparece. <c>ai.json</c> e os registros não contam, para um teste do CLI antes do app não esconder a pergunta.</summary>
    private static readonly string[] UsageMarkers = [LibraryFile.FileName, "recent.json", "app.json", "apikey.dat"];

    private UserFolders(UserFolder config, UserFolder cache, string defaultConfig, string defaultCache, string pointerPath, string? storedConfig, string? storedCache)
    {
        Config = config;
        Cache = cache;
        DefaultConfig = defaultConfig;
        DefaultCache = defaultCache;
        PointerPath = pointerPath;
        StoredConfig = storedConfig;
        StoredCache = storedCache;
    }

    /// <summary>Os dados e a configuração: a lista de jogos, os recentes, as fichas, a memória de tradução, o dicionário e as chaves.</summary>
    public UserFolder Config { get; }

    /// <summary>O cache do VNDB, as capturas de tela dos jogos e os registros de erro.</summary>
    public UserFolder Cache { get; }

    public string DefaultConfig { get; }

    public string DefaultCache { get; }

    /// <summary>O <c>pasta.json</c>, sempre no <c>%LOCALAPPDATA%/VNForge</c> real, mesmo com o cache em outro lugar.</summary>
    public string PointerPath { get; }

    /// <summary>O que o ponteiro guarda hoje para os dados (nulo sem ponteiro ou sem a chave).</summary>
    public string? StoredConfig { get; }

    /// <summary>O que o ponteiro guarda hoje para o cache (nulo sem ponteiro ou sem a chave).</summary>
    public string? StoredCache { get; }

    public string CaptureDirectory => System.IO.Path.Combine(Cache.Path, "captures");

    public string LogDirectory => System.IO.Path.Combine(Cache.Path, "logs");

    public string ImageCacheDirectory => System.IO.Path.Combine(Cache.Path, "cache", "vndb-images");

    public UserFolder Of(UserFolderKind kind) => kind == UserFolderKind.Config ? Config : Cache;

    /// <summary>Os valores a gravar no ponteiro quando <paramref name="kind"/> passa a ser <paramref name="path"/>: o outro lado guarda o que o ponteiro tinha, ou o padrão.</summary>
    public (string Config, string Cache) PointerWith(UserFolderKind kind, string path) =>
        kind == UserFolderKind.Config ? (path, StoredCache ?? DefaultCache) : (StoredConfig ?? DefaultConfig, path);

    /// <summary>
    /// A primeira pasta que não pode ser usada: uma variável ou um ponteiro para pasta que não existe (disco ou unidade desconectada). A pasta padrão que ainda não existe
    /// é normal e não conta. Os dados vêm antes do cache.
    /// </summary>
    public UserFolderProblem? Problem
    {
        get
        {
            foreach (var kind in new[] { UserFolderKind.Config, UserFolderKind.Cache })
            {
                var folder = Of(kind);
                if (folder.Source == UserFolderSource.Default || Directory.Exists(folder.Path))
                {
                    continue;
                }

                var message = folder.Source == UserFolderSource.Environment
                    ? $"{(kind == UserFolderKind.Config ? ConfigVariable : CacheVariable)} aponta para {folder.Path}, que não existe. Crie a pasta ou ajuste a variável."
                    : $"A pasta {(kind == UserFolderKind.Config ? "de dados" : "de cache")} {folder.Path} não está disponível. Conecte a unidade e tente de novo.";
                return new UserFolderProblem(kind, folder.Source, folder.Path, message);
            }

            return null;
        }
    }

    /// <summary>
    /// A pergunta da primeira abertura: só quando as duas pastas são o padrão (sem variável, sem ponteiro) e a pasta padrão não tem nenhum arquivo de quem já usou o app.
    /// Quem atualiza de uma versão sem a pergunta tem esses arquivos, então nada é perguntado, movido nem gravado.
    /// </summary>
    public bool MustAsk =>
        Config.Source == UserFolderSource.Default
        && Cache.Source == UserFolderSource.Default
        && !UsageMarkers.Any(name => File.Exists(System.IO.Path.Combine(Config.Path, name)));

    /// <summary>A regra aplicada ao sistema de hoje: as variáveis do processo e as pastas do Windows.</summary>
    public static UserFolders Resolve() =>
        Resolve(
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>
    /// A regra com as fontes na mão de quem chama (o teste passa uma pasta temporária e as variáveis que quer). <paramref name="roaming"/> e <paramref name="local"/> vazios ou
    /// relativos valem como a pasta temporária, para nunca gravar relativo ao diretório atual.
    /// </summary>
    public static UserFolders Resolve(Func<string, string?> environment, string? roaming, string? local)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var defaultConfig = System.IO.Path.Combine(Rooted(roaming), "VNForge");
        var localRoot = System.IO.Path.Combine(Rooted(local), "VNForge");
        var pointerPath = System.IO.Path.Combine(localRoot, PointerFileName);
        var (storedConfig, storedCache) = UserFolderPointer.Read(pointerPath);

        return new UserFolders(
            Choose(environment(ConfigVariable), storedConfig, defaultConfig),
            Choose(environment(CacheVariable), storedCache, localRoot),
            defaultConfig,
            localRoot,
            pointerPath,
            storedConfig,
            storedCache);
    }

    private static string Rooted(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && System.IO.Path.IsPathRooted(folder) ? folder : System.IO.Path.GetTempPath();

    private static UserFolder Choose(string? variable, string? stored, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(variable))
        {
            return new UserFolder(System.IO.Path.GetFullPath(variable.Trim()), UserFolderSource.Environment);
        }

        return !string.IsNullOrWhiteSpace(stored) && System.IO.Path.IsPathRooted(stored)
            ? new UserFolder(stored, UserFolderSource.Pointer)
            : new UserFolder(fallback, UserFolderSource.Default);
    }
}

/// <summary>O <c>pasta.json</c>: <c>{ "dados": "D:\\Dados\\VNForge", "cache": "..." }</c>. Arquivo ilegível vale como sem ponteiro: a pasta antiga não é tocada e a pergunta volta.</summary>
public static class UserFolderPointer
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static (string? Config, string? Cache) Read(string pointerPath)
    {
        try
        {
            if (!File.Exists(pointerPath))
            {
                return (null, null);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(pointerPath));
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? (Text(document.RootElement, "dados"), Text(document.RootElement, "cache"))
                : (null, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>Grava o ponteiro trocando o arquivo por inteiro e conferindo o resultado, como toda gravação do Core.</summary>
    public static void Save(string pointerPath, string config, string cache) =>
        AtomicFile.WriteAllText(pointerPath, JsonSerializer.Serialize(new Dictionary<string, string> { ["dados"] = config, ["cache"] = cache }, Json));

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
