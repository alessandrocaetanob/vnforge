using System.Text.Json;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class StatusCommandTests : IDisposable
{
    private const string Yst00001 = "yst00001";

    private readonly CliHarness _cli = new();
    private readonly string _projectPath;

    public StatusCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        var project = ProjectFolder.Create(_projectPath, new ProjectConfig(
            "Minha VN",
            new GameConfig(Path.Combine(_cli.Root, "jogo"), "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        project.SaveText(Yst00001,
        [
            Unit(Yst00001, 1, "美咲", TranslationStatus.Approved),
            Unit(Yst00001, 2, "美咲", TranslationStatus.Untranslated),
            Unit(Yst00001, 3, null, TranslationStatus.Machine),
        ]);
        project.SaveText("yst00002", [Unit("yst00002", 1, "健", TranslationStatus.Reviewed)]);
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Unit(string key, int n, string? speaker, TranslationStatus status) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, speaker, "原文", status == TranslationStatus.Untranslated ? null : "tradução", status);

    [Fact]
    public async Task Json_traz_total_arquivos_e_personagens_que_batem_com_os_text_json()
    {
        var result = await _cli.RunAsync(["status", "--project", _projectPath, "--json"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var data = doc.RootElement.GetProperty("data");

        var total = data.GetProperty("total");
        Assert.Equal(4, total.GetProperty("lines").GetInt32());
        Assert.Equal(1, total.GetProperty("untranslated").GetInt32());
        Assert.Equal(1, total.GetProperty("machine").GetInt32());
        Assert.Equal(0, total.GetProperty("translated").GetInt32());
        Assert.Equal(1, total.GetProperty("reviewed").GetInt32());
        Assert.Equal(1, total.GetProperty("approved").GetInt32());
        Assert.Equal(75, total.GetProperty("percent").GetDouble());

        var files = data.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal([Yst00001, "yst00002"], files.Select(f => f.GetProperty("key").GetString()));
        Assert.Equal([3, 1], files.Select(f => f.GetProperty("lines").GetInt32()));

        var characters = data.GetProperty("characters").EnumerateArray().ToList();
        Assert.Equal(["美咲", "健", null], characters.Select(c => c.GetProperty("speaker").GetString()));
        Assert.Equal([2, 1, 1], characters.Select(c => c.GetProperty("lines").GetInt32()));
    }

    [Fact]
    public async Task Texto_mostra_titulo_barra_legenda_e_as_duas_tabelas()
    {
        var project = ProjectFolder.Open(_projectPath);
        project.SaveCharacters([new CharacterEntry("美咲", "Misaki")]);

        var result = await _cli.RunAsync(["status", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain('\u001b', result.Output);
        var lines = result.Output.TrimEnd().ReplaceLineEndings("\n").Split('\n').Select(l => l.TrimEnd()).ToArray();
        Assert.Equal("Minha VN · PT-BR", lines[0]);
        Assert.Equal("4 linhas · 75,0 % com tradução", lines[1]);
        Assert.Matches("^[█▓▒░·]{60}$", lines[2]);
        Assert.Equal("█ Aprovadas 1  ▓ Revisadas 1  ▒ Traduzidas 0  ░ IA 1  · Pendentes 1", lines[3]);

        var scriptHeader = Array.FindIndex(lines, l => l.TrimStart().StartsWith("Script", StringComparison.Ordinal));
        Assert.Equal(["Script", "Linhas", "Progresso", "%"], lines[scriptHeader].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Matches(@"^\s*yst00001\s+3\s+\S{20}\s+66,6 %$", lines[scriptHeader + 1]);
        Assert.Matches(@"^\s*yst00002\s+1\s+\S{20}\s+100,0 %$", lines[scriptHeader + 2]);

        var characterHeader = Array.FindIndex(lines, l => l.TrimStart().StartsWith("Personagem", StringComparison.Ordinal));
        Assert.True(characterHeader > scriptHeader);
        Assert.Contains("美咲 → Misaki", lines[characterHeader + 1]);
        Assert.Contains("健", lines[characterHeader + 2]);
        Assert.Contains("(sem falante)", lines[characterHeader + 3]);
    }

    [Fact]
    public async Task Terminal_interativo_pinta_a_barra_com_as_cores_do_app()
    {
        using var cli = new CliHarness(terminal: CliTerminal.TrueColor(width: 100));

        var result = await cli.RunAsync(["status", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("38;2;95;191;127m", result.Output); // aprovada
        Assert.Contains("38;2;154;127;214m", result.Output); // IA
        Assert.Contains("38;2;58;66;80m", result.Output); // pendente
    }

    [Fact]
    public async Task No_color_tira_o_ansi_mesmo_em_terminal_interativo()
    {
        using var cli = new CliHarness(terminal: CliTerminal.TrueColor(width: 100));

        var result = await cli.RunAsync(["status", "--project", _projectPath, "--no-color"], TestContext.Current.CancellationToken);

        Assert.DoesNotContain('\u001b', result.Output);
        Assert.Contains("█ Aprovadas 1", result.Output);
    }

    [Fact]
    public async Task Characters_json_invalido_nao_derruba_o_status_em_texto()
    {
        File.WriteAllText(ProjectFolder.Open(_projectPath).CharactersPath, "isto nao e json");

        var result = await _cli.RunAsync(["status", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("美咲", result.Output);
        Assert.DoesNotContain("→", result.Output);
    }

    [Fact]
    public async Task Projeto_sem_linhas_mostra_a_barra_toda_pendente()
    {
        var empty = ProjectFolder.Create(Path.Combine(_cli.Root, "vazio"), ProjectFolder.Open(_projectPath).LoadConfig());

        var result = await _cli.RunAsync(["status", "--project", empty.Root], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("0 linhas · 0,0 % com tradução", result.Output);
        Assert.Contains(new string('·', 60), result.Output);
    }

    [Fact]
    public async Task Pasta_sem_projeto_sai_com_1()
    {
        var result = await _cli.RunAsync(["status", "--project", _cli.WorkingDirectory], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
    }
}
