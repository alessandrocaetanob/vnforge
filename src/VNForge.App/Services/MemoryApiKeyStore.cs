using VNForge.AI.Settings;

namespace VNForge.App.Services;

/// <summary>Cofre só em memória: a chave some quando o app fecha. Serve de padrão onde não há DPAPI e nos testes.</summary>
public sealed class MemoryApiKeyStore : IApiKeyStore
{
    private string? key;

    public string? Load() => key;

    public void Save(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        key = apiKey;
    }

    public bool Delete()
    {
        var had = key is not null;
        key = null;
        return had;
    }
}
