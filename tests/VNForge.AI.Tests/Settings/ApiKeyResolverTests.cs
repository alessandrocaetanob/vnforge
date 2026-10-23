using VNForge.AI.Settings;

namespace VNForge.AI.Tests.Settings;

public sealed class ApiKeyResolverTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vnforge-ai-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Variavel_de_ambiente_tem_prioridade_sobre_o_cofre()
    {
        var store = new DpapiApiKeyStore(_dir);
        store.Save("do-cofre");

        var resolved = ApiKeyResolver.Resolve(store, _ => " do-ambiente ");

        Assert.Equal(new ResolvedApiKey("do-ambiente", ApiKeySource.Environment), resolved);
    }

    [Fact]
    public void Sem_variavel_usa_o_cofre_e_sem_nenhum_devolve_none()
    {
        var store = new DpapiApiKeyStore(_dir);
        Assert.Equal(ApiKeySource.None, ApiKeyResolver.Resolve(store, _ => null).Source);

        store.Save("do-cofre");

        Assert.Equal(new ResolvedApiKey("do-cofre", ApiKeySource.Vault), ApiKeyResolver.Resolve(store, _ => ""));
    }

    [Fact]
    public void Arquivo_do_cofre_nao_contem_a_chave_em_texto_puro()
    {
        var store = new DpapiApiKeyStore(_dir);
        store.Save("sk-ant-segredo");

        Assert.DoesNotContain("sk-ant-segredo", File.ReadAllText(store.Path, System.Text.Encoding.Latin1));
        Assert.Equal("sk-ant-segredo", store.Load());
        Assert.True(store.Delete());
        Assert.Null(store.Load());
    }

    [Fact]
    public void Modelo_invalido_no_arquivo_e_erro_esperado()
    {
        Directory.CreateDirectory(_dir);
        var file = new AiUserSettingsFile(_dir);
        File.WriteAllText(file.Path, """{"model":"outro"}""");

        Assert.Throws<AiSettingsException>(file.Load);
    }
}
