using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Publishing;

namespace VNForge.Core.Tests.Publishing;

[Trait("Risco", "R1")]
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
