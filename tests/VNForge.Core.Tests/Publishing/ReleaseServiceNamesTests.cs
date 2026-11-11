using VNForge.Core;
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

    [Theory]
    [InlineData("jogo2", "jogo", false)]
    public void Pasta_de_saida_dentro_do_jogo_e_reconhecida(string folder, string parent, bool inside)
    {
        var root = Path.Combine(Path.GetTempPath(), "vnf-inside");

        Assert.Equal(inside, ReleaseService.IsInside(Path.Combine(root, folder), Path.Combine(root, parent)));
    }

    [Fact]
    public void Release_recusa_a_pasta_de_saida_dentro_da_pasta_do_jogo_antes_de_criar_qualquer_coisa()
    {
        var root = Path.Combine(Path.GetTempPath(), "vnf-release-" + Guid.NewGuid().ToString("N"));
        try
        {
            var game = Directory.CreateDirectory(Path.Combine(root, "jogo")).FullName;
            var project = ProjectFolder.Create(Path.Combine(root, "projeto"), new ProjectConfig(
                "teste",
                new GameConfig(game, "game.exe"),
                new EngineConfig("yuris"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));
            var stub = Path.Combine(root, "instalador.exe");
            File.WriteAllText(stub, "MZ");
            var output = Path.Combine(game, "releases", "1.0.0");

            var error = Assert.Throws<VNForgeException>(() => new ReleaseService([]).Create(project, new ReleaseOptions("1.0.0", output, InstallerStub: stub), cancellationToken: TestContext.Current.CancellationToken));

            Assert.Contains("pasta do jogo", error.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
