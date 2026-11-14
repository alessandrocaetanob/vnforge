using System.Security.Cryptography;
using System.Text;
using VNForge.Core.Injection;
using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public sealed class ProjectGameRelocatorMixedTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static OriginalFile Of(string name, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new OriginalFile(name, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    // Um arquivo original e outro com a injeção na mesma pasta é um jogo em estado que nem a restauração nem o teste esperam: a troca é recusada sem gravar nada.
    [Fact]
    public void Pasta_com_um_arquivo_original_e_outro_traduzido_e_recusada_e_o_game_path_fica_como_estava()
    {
        var game = Path.Combine(_temp.Path, "antigo");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "a.ypf"), "a-original");
        File.WriteAllText(Path.Combine(game, "b.ypf"), "b-original");
        var project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig("Teste", new GameConfig(game.Replace('\\', '/'), "jogo.exe"), new EngineConfig("yuris"), new LanguagesConfig("ja", "pt-BR"), new EncodingConfig("fold"), AiConfig.Default));
        project.SaveState(new PipelineState([Of("a.ypf", "a-original"), Of("b.ypf", "b-original")], new Dictionary<string, StageState>(), game));
        Directory.CreateDirectory(project.BuildDirectory);
        File.WriteAllText(Path.Combine(project.BuildDirectory, "a.ypf"), "a-traduzido");
        File.WriteAllText(Path.Combine(project.BuildDirectory, "b.ypf"), "b-traduzido");
        new GameInjector().Inject(GameInjector.Plan(project));

        var novo = Path.Combine(_temp.Path, "novo");
        Directory.CreateDirectory(novo);
        File.WriteAllText(Path.Combine(novo, "a.ypf"), "a-traduzido");
        File.WriteAllText(Path.Combine(novo, "b.ypf"), "b-original");

        Assert.True(ProjectGameRelocator.Inspect(project, novo).Mixed);
        var refused = Assert.Throws<VNForgeException>(() => ProjectGameRelocator.Move(project, novo));

        Assert.Contains(ProjectGameRelocator.MixedMessage, refused.Message);
        Assert.Equal(game.Replace('\\', '/'), project.LoadConfig().Game.Path);
    }
}
