using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;

namespace VNForge.App.Tests;

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

    [Fact]
    public void Resolve_numa_pasta_vazia_devolve_uma_origem_valida_sem_lancar()
    {
        using var temp = new TempFolder();

        var chave = ApiKeyStatus.Resolve(temp.Path);

        Assert.True(chave.Source is ApiKeySource.None or ApiKeySource.Environment);
    }

    [Fact]
    public void Sem_cofre_na_plataforma_cai_para_so_o_ambiente()
    {
        var chave = ApiKeyStatus.Resolve(() => throw new PlatformNotSupportedException());

        Assert.True(chave.Source is ApiKeySource.None or ApiKeySource.Environment);
    }

    [Fact]
    public void Cofre_com_chave_e_usado_quando_o_ambiente_esta_vazio()
    {
        var chave = ApiKeyStatus.Resolve(() => new FixedVault("do-cofre"));

        Assert.True(chave.Source is ApiKeySource.Vault or ApiKeySource.Environment);
    }

    private sealed class FixedVault(string key) : IApiKeyStore
    {
        public string? Load() => key;

        public void Save(string apiKey) => throw new NotSupportedException();

        public bool Delete() => false;
    }

    [Fact]
    public void Janela_principal_marca_a_chave_como_configurada_so_quando_ha_origem()
    {
        using var temp = new TempFolder();
        var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("c")), new FakeFolderPicker(null), TimeProvider.System);

        var com = new MainWindowViewModel(start, new ResolvedApiKey("k", ApiKeySource.Vault));
        var sem = new MainWindowViewModel(start);

        Assert.True(com.ApiKeyConfigured);
        Assert.False(sem.ApiKeyConfigured);
        Assert.Contains("não configurada", sem.ApiKeyStatus);
    }
}
