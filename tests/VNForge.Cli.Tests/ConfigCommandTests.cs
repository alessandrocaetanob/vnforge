using System.Text.Json;
using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

public sealed class ConfigCommandTests : IDisposable
{
    private const string Key = "sk-ant-teste-0123456789abcdef";

    private readonly CliHarness _cli = new(Key + Environment.NewLine, realVault: OperatingSystem.IsWindows());

    public void Dispose() => _cli.Dispose();

    private string MakeProject()
    {
        var path = Path.Combine(_cli.Root, "projeto");
        ProjectFolder.Create(path, new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
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

        var result = await _cli.RunAsync(["config", "ai", "--set-key", "--model", "claude-sonnet-5-5", "--project", project], TestContext.Current.CancellationToken);

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
        var result = await _cli.RunAsync(["config", "ai", "--set-key", "--model", "claude-haiku-5-5"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(FilesContaining(_cli.UserConfigDirectory, Key));
        Assert.True(File.Exists(Path.Combine(_cli.UserConfigDirectory, "apikey.dat")));
        Assert.Contains("claude-haiku-5-5", File.ReadAllText(Path.Combine(_cli.UserConfigDirectory, "ai.json")));
        Assert.DoesNotContain("apikey", File.ReadAllText(Path.Combine(_cli.UserConfigDirectory, "ai.json")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Json_mostra_o_modelo_e_a_origem_sem_a_chave()
    {
        await _cli.RunAsync(["config", "ai", "--set-key", "--model", "claude-fable-5-1"], TestContext.Current.CancellationToken);

        var result = await _cli.RunAsync(["config", "ai", "--json"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(Key, result.Output);
        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("claude-fable-5-1", data.GetProperty("model").GetString());
        Assert.True(data.GetProperty("keyConfigured").GetBoolean());
    }

    [Fact]
    public async Task Clear_key_apaga_o_cofre()
    {
        await _cli.RunAsync(["config", "ai", "--set-key"], TestContext.Current.CancellationToken);

        var result = await _cli.RunAsync(["config", "ai", "--clear-key"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(_cli.UserConfigDirectory, "apikey.dat")));
        Assert.Equal(VNForge.AI.Settings.ApiKeySource.None, VNForge.AI.Settings.ApiKeyResolver.Resolve(_cli.Cli.ApiKeyStore, _ => null).Source);
    }

    [Fact]
    public async Task Modelo_desconhecido_sai_com_1_sem_gravar()
    {
        var result = await _cli.RunAsync(["config", "ai", "--model", "gpt-4"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(_cli.UserConfigDirectory, "ai.json")));
    }

    [Fact]
    public async Task Chave_vazia_sai_com_1()
    {
        using var vazio = new CliHarness("\n");

        var result = await vazio.RunAsync(["config", "ai", "--set-key"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(vazio.UserConfigDirectory, "apikey.dat")));
    }
}
