using VNForge.Core.Projects;
using VNForge.Core.Publishing;

namespace VNForge.Core.Tests.Publishing;

public sealed class ReleaseServiceNamesTests
{
    private static ProjectConfig Config(string name, GameTitleConfig? title = null) => new(
        name,
        new GameConfig("jogo", "game.exe") { Title = title },
        new EngineConfig("yuris"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("cp1252"),
        AiConfig.Default);

    [Fact]
    public void Nome_do_instalador_usa_o_titulo_traduzido_o_idioma_e_a_versao()
    {
        var config = Config("amaoto", new GameTitleConfig("雨音の箱庭", "Amaoto no Hakoniwa"));

        Assert.Equal("Amaoto-no-Hakoniwa-PT-BR-1.2.0", ReleaseService.DefaultInstallerName(config, "1.2.0"));
    }

    [Fact]
    public void Sem_titulo_o_nome_do_projeto_serve()
    {
        Assert.Equal("meu-jogo-PT-BR-2", ReleaseService.DefaultInstallerName(Config("meu jogo"), "2"));
    }
}
