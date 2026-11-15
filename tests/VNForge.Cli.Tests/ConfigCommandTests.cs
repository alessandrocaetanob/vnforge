using System.Text.Json;
using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R5")]
public sealed class ConfigCommandTests : IDisposable
{
    private const string Config = "config";
    private const string SetKeyOption = "--set-key";
    private const string ModelOption = "--model";

    private const string Key = "sk-ant-teste-0123456789abcdef";

    private readonly CliHarness _cli = new(Key + Environment.NewLine, realVault: OperatingSystem.IsWindows());

    public void Dispose() => _cli.Dispose();

    private string MakeProject()
    {
        var path = Path.Combine(_cli.Root, "projeto");
        ProjectFolder.Create(path, new ProjectConfig(
            "Minha VN",
            new GameConfig(Path.Combine(_cli.Root, "jogo"), "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        return path;
    }

    private static IEnumerable<string> FilesContaining(string folder, string text) =>
        (Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories) : [])
            .Where(f => File.ReadAllText(f, System.Text.Encoding.Latin1).Contains(text, StringComparison.Ordinal));

    [Fact]
    public async Task Nenhum_arquivo_do_projeto_nem_da_pasta_atual_recebe_a_chave()
    {
        var project = MakeProject();

        var result = await _cli.RunAsync([Config, "ai", SetKeyOption, ModelOption, "claude-sonnet-5-5", "--project", project], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(FilesContaining(project, Key));
        Assert.Empty(FilesContaining(_cli.WorkingDirectory, Key));
        Assert.Empty(FilesContaining(_cli.UserLogDirectory, Key));
        Assert.DoesNotContain(Key, result.Output + result.Error);
    }

    [Fact]
    public async Task Chave_fica_cifrada_no_cofre_do_usuario_e_modelo_em_texto()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "O cofre DPAPI só existe no Windows.");
        var result = await _cli.RunAsync([Config, "ai", SetKeyOption, ModelOption, "claude-haiku-5-5"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(FilesContaining(_cli.UserConfigDirectory, Key));
        Assert.True(File.Exists(Path.Combine(_cli.UserConfigDirectory, "apikey.dat")));
        Assert.Contains("claude-haiku-5-5", await File.ReadAllTextAsync(Path.Combine(_cli.UserConfigDirectory, "ai.json"), TestContext.Current.CancellationToken));
        Assert.DoesNotContain("apikey", await File.ReadAllTextAsync(Path.Combine(_cli.UserConfigDirectory, "ai.json"), TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
    }
}
