using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

/// <summary>Projeto vazio que os testes de <c>vnf translate</c> e <c>vnf review</c> dividem.</summary>
internal static class AiTestProject
{
    public static ProjectFolder Create(string root, string path) =>
        ProjectFolder.Create(path, new ProjectConfig(
            "Minha VN",
            new GameConfig(Path.Combine(root, "jogo"), "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
}
