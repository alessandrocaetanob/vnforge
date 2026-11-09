using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class StatusRiskTests : IDisposable
{
    private const string Yst00001 = "yst00001";

    private readonly CliHarness _cli = new();
    private readonly string _projectPath;
    private readonly ProjectFolder _project;

    public StatusRiskTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = ProjectFolder.Create(_projectPath, new ProjectConfig(
            "Minha VN",
            new GameConfig(Path.Combine(_cli.Root, "jogo"), "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        _project.SaveText(Yst00001,
        [
            Unit(1, TranslationStatus.Machine),
            (Unit(2, TranslationStatus.Machine) with { Doubt = new TranslationDoubt(DoubtKind.Tone, "Ela fala sério?") }).WithOdd(true),
            Unit(3, TranslationStatus.Approved),
            Unit(4, TranslationStatus.Untranslated),
        ]);
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(
            Path.Combine(_project.CacheDirectory, "editor-suggestions.json"),
            """{"version":1,"suggestions":[{"scriptKey":"yst00001","unitId":"yst00001:000002","kind":"voice","base":"tradução","proposed":"outra","reason":"voz","at":"2026-11-08T10:00:00+00:00"}]}""");
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Unit(int n, TranslationStatus status) =>
        new($"{Yst00001}:{n:D6}", TextUnitKind.Dialogue, "美咲", n == 2 ? @"原文\p" : "原文", status == TranslationStatus.Untranslated ? null : "tradução", status);

    /// <summary>60 aprovações a cada 30 s terminando agora: 120 linhas por hora, sem mexer nas linhas do projeto.</summary>
    private void Aprovar60()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var sessions = Enumerable.Range(0, 60).Aggregate(
            SessionsFile.Empty,
            (file, i) => file.With(new ApprovalRecord("yst99999", $"yst99999:{i:D6}", start.AddSeconds(i * 30))));
        SessionsFile.Save(_project, sessions);
    }

    [Fact]
    public async Task Json_traz_a_fila_do_maior_risco_ao_menor_com_os_sinais_de_cada_linha()
    {
        var result = await _cli.RunAsync(["status", "--risk", "--project", _projectPath, "--json"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("lines").GetInt32());
        Assert.Equal(1, data.GetProperty("high").GetInt32());
        Assert.False(data.TryGetProperty("perHour", out var perHour) && perHour.ValueKind != JsonValueKind.Null, "sem aprovações não há ritmo");
        Assert.False(data.TryGetProperty("minutesToClearHigh", out var minutes) && minutes.ValueKind != JsonValueKind.Null, "sem ritmo não há horas");

        var top = data.GetProperty("top").EnumerateArray().ToList();
        Assert.Equal(["yst00001:000002", "yst00001:000001"], top.Select(t => t.GetProperty("id").GetString()));
        Assert.Equal("high", top[0].GetProperty("level").GetString());
        Assert.True(top[0].GetProperty("score").GetInt32() >= 70);
        Assert.Equal(
            ["ai-doubt", "sounds-odd", "editor-suggestion"],
            top[0].GetProperty("factors").EnumerateArray().Select(f => f.GetProperty("kind").GetString()).Where(k => k is not "qa"));
        Assert.Equal(2, data.GetProperty("top")[0].GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task Top_limita_as_linhas_da_lista_sem_mudar_a_contagem()
    {
        var result = await _cli.RunAsync(["status", "--risk", "--top", "1", "--project", _projectPath, "--json"], TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.Single(data.GetProperty("top").EnumerateArray());
        Assert.Equal(2, data.GetProperty("lines").GetInt32());
    }

    [Fact]
    public async Task Com_ritmo_as_horas_ate_zerar_o_risco_alto_saem_do_sessions_json()
    {
        Aprovar60();

        var result = await _cli.RunAsync(["status", "--risk", "--project", _projectPath, "--json"], TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(120, data.GetProperty("perHour").GetInt32());
        Assert.Equal(1, data.GetProperty("minutesToClearHigh").GetInt32());
    }

    [Fact]
    public async Task Texto_mostra_a_barra_a_frase_das_horas_e_a_tabela_da_fila()
    {
        Aprovar60();

        var result = await _cli.RunAsync(["status", "--risk", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain('\u001b', result.Output);
        var lines = result.Output.TrimEnd().ReplaceLineEndings("\n").Split('\n').Select(l => l.TrimEnd()).ToArray();
        Assert.Equal("Fila por risco · Minha VN", lines[0]);
        Assert.Equal("2 linhas esperando revisão", lines[1]);
        Assert.Matches("^[█▒·]{60}$", lines[2]);
        Assert.Equal("█ Alto 50%  ▒ Médio 0%  · Baixo 50%", lines[3]);
        Assert.Equal("1 linha de risco alto · faltam cerca de 1 min no seu ritmo de 120 linhas por hora", lines[4]);

        var header = Array.FindIndex(lines, l => l.TrimStart().StartsWith("Risco", StringComparison.Ordinal));
        Assert.Equal(["Risco", "Nível", "Linha", "Falante", "Sinais"], lines[header].Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5));
        Assert.Matches(@"^\s*\d+\s+Alto\s+yst00001:000002\s+美咲", lines[header + 1]);
        Assert.Contains("Dúvida da IA", lines[header + 1]);
    }

    [Fact]
    public async Task Sem_ritmo_a_frase_das_horas_some_e_fica_so_a_contagem()
    {
        var result = await _cli.RunAsync(["status", "--risk", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Contains("1 linha de risco alto", result.Output);
        Assert.DoesNotContain("faltam", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("ritmo", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sem_o_arquivo_da_passada_de_editor_a_fila_sai_igual_sem_a_sugestao()
    {
        File.Delete(Path.Combine(_project.CacheDirectory, "editor-suggestions.json"));

        var result = await _cli.RunAsync(["status", "--risk", "--project", _projectPath, "--json"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        var factors = doc.RootElement.GetProperty("data").GetProperty("top")[0].GetProperty("factors").EnumerateArray().Select(f => f.GetProperty("kind").GetString());
        Assert.DoesNotContain("editor-suggestion", factors);
    }

    [Fact]
    public async Task Top_invalido_e_recusado()
    {
        var result = await _cli.RunAsync(["status", "--risk", "--top", "0", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.NotEqual(0, result.ExitCode);
    }
}
