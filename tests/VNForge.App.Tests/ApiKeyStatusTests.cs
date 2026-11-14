using VNForge.AI.Settings;
using VNForge.App.Services;

namespace VNForge.App.Tests;

[Trait("Risco", "R5")]
public class ApiKeyStatusTests
{
    [Theory]
    [InlineData(ApiKeySource.Environment, "Chave do Claude na variável ANTHROPIC_API_KEY")]
    [InlineData(ApiKeySource.Vault, "Chave do Claude no Cofre do Windows")]
    [InlineData(ApiKeySource.None, "Chave do Claude não configurada: a tradução com IA fica desligada")]
    public void Describe_diz_a_origem_sem_mostrar_a_chave(ApiKeySource origem, string esperado)
    {
        var texto = ApiKeyStatus.Describe(new ResolvedApiKey("segredo", origem));

        Assert.Equal(esperado, texto);
        Assert.DoesNotContain("segredo", texto);
    }

    private sealed class FixedVault(string key) : IApiKeyStore
    {
        public string? Load() => key;

        public void Save(string apiKey) => throw new NotSupportedException();

        public bool Delete() => false;
    }
}
