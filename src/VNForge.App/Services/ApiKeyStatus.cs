using VNForge.AI.Settings;

namespace VNForge.App.Services;

/// <summary>Texto da barra de status sobre a chave da Anthropic. Nunca toca no valor da chave.</summary>
public static class ApiKeyStatus
{
    public static string Describe(ResolvedApiKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.Source switch
        {
            ApiKeySource.Environment => $"Chave do Claude na variável {ApiKeyResolver.EnvironmentVariable}",
            ApiKeySource.Vault => "Chave do Claude no Cofre do Windows",
            _ => "Chave do Claude não configurada: a tradução com IA fica desligada",
        };
    }

    /// <summary>Resolve a chave do ambiente ou do cofre; onde o cofre não existe (fora do Windows), cai para só o ambiente.</summary>
    public static ResolvedApiKey Resolve(string configDirectory) =>
        Resolve(() => new DpapiApiKeyStore(configDirectory));

    public static ResolvedApiKey Resolve(Func<IApiKeyStore> openVault)
    {
        ArgumentNullException.ThrowIfNull(openVault);
        try
        {
            return ApiKeyResolver.Resolve(openVault());
        }
        catch (Exception e) when (e is PlatformNotSupportedException or System.Security.Cryptography.CryptographicException or IOException)
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(ApiKeyResolver.EnvironmentVariable);
            return string.IsNullOrWhiteSpace(fromEnvironment)
                ? new ResolvedApiKey(null, ApiKeySource.None)
                : new ResolvedApiKey(fromEnvironment.Trim(), ApiKeySource.Environment);
        }
    }
}
