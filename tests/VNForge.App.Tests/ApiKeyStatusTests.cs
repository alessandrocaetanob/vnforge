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
