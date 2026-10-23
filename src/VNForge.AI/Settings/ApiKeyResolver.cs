namespace VNForge.AI.Settings;

/// <summary>De onde veio a chave de API.</summary>
public enum ApiKeySource
{
    None,
    Environment,
    Vault,
}

/// <summary>A chave resolvida e sua origem.</summary>
public sealed record ResolvedApiKey(string? Key, ApiKeySource Source);

/// <summary>Resolve a chave de API: a variável de ambiente <c>ANTHROPIC_API_KEY</c> tem prioridade; depois, o cofre do usuário.</summary>
public static class ApiKeyResolver
{
    public const string EnvironmentVariable = "ANTHROPIC_API_KEY";

    public static ResolvedApiKey Resolve(IApiKeyStore store, Func<string, string?>? readEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        var fromEnvironment = (readEnvironment ?? Environment.GetEnvironmentVariable)(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return new ResolvedApiKey(fromEnvironment.Trim(), ApiKeySource.Environment);
        }

        var fromVault = store.Load();
        return string.IsNullOrWhiteSpace(fromVault)
            ? new ResolvedApiKey(null, ApiKeySource.None)
            : new ResolvedApiKey(fromVault, ApiKeySource.Vault);
    }
}
