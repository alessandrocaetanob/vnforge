using VNForge.Core.Engines;
using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class RunSummariesTests : IDisposable
{
    private const string Ysbin = "ysbin.ypf";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-summaries-" + Guid.NewGuid().ToString("N"));

    public RunSummariesTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string relative, int bytes)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1,5 KB")]
    [InlineData(43_200_000, "41,2 MB")]
    [InlineData(1_400_000_000, "1,3 GB")]
    public void Tamanho_usa_a_unidade_certa_com_virgula_decimal(long bytes, string expected)
    {
        Assert.Equal(expected, RunSummaries.FormatBytes(bytes));
    }

    [Theory]
    [InlineData(6.8, "6,8 s")]
    [InlineData(72, "1 min 12 s")]
    [InlineData(3900, "1 h 05 min")]
    public void Duracao_cresce_de_segundos_para_horas(double seconds, string expected)
    {
        Assert.Equal(expected, RunSummaries.FormatDuration(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Painel_da_extracao_traz_scripts_linhas_tamanho_do_pacote_e_duracao()
    {
        var game = Path.Combine(_root, "jogo");
        Write(Path.Combine("jogo", Ysbin), 2_097_152);
        var result = new ExtractionResult(
            game,
            Path.Combine(_root, ".cache"),
            [new UnpackedArchive(Ysbin, "ysbin", 40, 9_000_000)],
            [new ExtractedScript("yst00001", "Prólogo", 3), new ExtractedScript("yst00002", null, 2)],
            new Dictionary<TextUnitKind, int> { [TextUnitKind.Dialogue] = 4, [TextUnitKind.Choice] = 1 },
            ["yst00002.ybn: algo estranho"]);
        var harness = ConsoleHarness.Plain();

        var text = harness.Render(RunSummaries.ExtractPanel(result, TimeSpan.FromSeconds(6.8)));

        Assert.Contains("Resultado da extração", text);
        Assert.Contains("Scripts", text);
        Assert.Contains("5 (4 diálogos, 1 escolha)", text);
        Assert.Contains(Ysbin, text);
        Assert.Contains("2,0 MB · 40 arquivos", text);
        Assert.Contains("Avisos", text);
        Assert.Contains("6,8 s", text);
        Assert.False(text.Contains('\u001b'));
    }

    [Fact]
    public void Painel_da_extracao_de_projeto_ja_extraido_mostra_a_reextracao()
    {
        var result = new ExtractionResult(_root, _root, [], [new ExtractedScript("yst00001", null, 1)], new Dictionary<TextUnitKind, int>(), [])
        {
            Reextraction = new ReextractionSummary(Kept: 7, Outdated: 2, Added: 1, Removed: 0),
        };

        var text = ConsoleHarness.Plain().Render(RunSummaries.ExtractPanel(result, TimeSpan.FromSeconds(1)));

        Assert.Contains("7 mantidas · 2 desatualizadas · 1 novas · 0 removidas", text);
    }

    [Fact]
    public void Painel_do_build_traz_scripts_pacote_com_o_original_acentuacao_e_duracao()
    {
        var game = Path.Combine(_root, "jogo");
        var build = Path.Combine(_root, "build");
        Write(Path.Combine("jogo", Ysbin), 2_097_152);
        Write(Path.Combine("build", Ysbin), 2_202_009);
        var encoding = new EncodingReport("cp1252", 10, [new EncodingIssue("yst00001:000001", "yst00001", 3, "é", "e"), new EncodingIssue("yst00001:000002", "yst00001", 1, "ã", "a")]);
        var result = new BuildResult(game, build, [new RepackedArchive(Ysbin, 14)], 312, [new BuiltScript("yst00001", "yst00001.ybn", 8)], [], encoding, QaReport.Empty);
        var harness = ConsoleHarness.Plain();

        var text = harness.Render(RunSummaries.BuildPanel(result, TimeSpan.FromSeconds(6.8)));

        Assert.Contains("Resultado do build", text);
        Assert.Contains("1 reconstruídos de 312", text);
        Assert.Contains("2,1 MB (original 2,0 MB)", text);
        Assert.Contains("cp1252 · 2 caracteres trocados", text);
        Assert.Contains("6,8 s", text);
    }

    [Fact]
    public void Painel_do_build_sem_troca_de_caracteres_diz_que_nenhum_foi_trocado()
    {
        var result = new BuildResult(_root, _root, [], 3, [], [], new EncodingReport("sjis", 0, []), QaReport.Empty) { ReusedScripts = 3 };

        var text = ConsoleHarness.Plain().Render(RunSummaries.BuildPanel(result, TimeSpan.FromSeconds(1)));

        Assert.Contains("0 reconstruídos de 3 · 3 reaproveitados", text);
        Assert.Contains("sjis · nenhum caractere trocado", text);
    }

    [Fact]
    public void Tabela_do_inject_lista_cada_arquivo_com_o_que_acontece_e_os_tamanhos()
    {
        var target = Write(Path.Combine("jogo", Ysbin), 1024);
        var build = Write(Path.Combine("projeto", "build", Ysbin), 2048);
        var fresh = Write(Path.Combine("projeto", "build", "novo.ypf"), 100);
        var plan = new InjectionPlan(
            Path.Combine(_root, "jogo"),
            Path.Combine(_root, "backup"),
            [
                new PlannedFile(Ysbin, build, target, "b", "o", null, "o"),
                new PlannedFile("novo.ypf", fresh, Path.Combine(_root, "jogo", "novo.ypf"), "b", null, null, null),
            ],
            UnchangedCount: 2);
        var harness = ConsoleHarness.Plain(width: 300);

        var lines = harness.Render(RunSummaries.InjectTable(plan)).Split('\n', StringSplitOptions.TrimEntries);

        Assert.StartsWith("Jogo: ", lines[0]);
        Assert.Contains(lines, l => l.Contains("O que acontece") && l.Contains("Atual") && l.Contains("Depois"));
        Assert.Contains(lines, l => l.StartsWith(Ysbin, StringComparison.Ordinal) && l.Contains("substitui o original") && l.Contains("1,0 KB") && l.Contains("2,0 KB"));
        Assert.Contains(lines, l => l.StartsWith("novo.ypf", StringComparison.Ordinal) && l.Contains("arquivo novo") && l.Contains("100 B"));
        Assert.Contains(lines, l => l.Contains("2 arquivo(s) de build/ já estão iguais"));
    }

    [Fact]
    public void Tabela_do_restore_diz_o_que_volta_o_que_sai_o_que_fica_e_o_que_ja_nao_existe()
    {
        Write(Path.Combine("jogo", Ysbin), 2048);
        var injected = Write(Path.Combine("jogo", "novo.ypf"), 100);
        Write(Path.Combine("jogo", "mexido.ypf"), 300);
        Write(Path.Combine("backup", "2026-10-24_1200", Ysbin), 1024);
        var backup = Path.Combine(_root, "backup", "2026-10-24_1200");
        var manifest = new BackupManifest(
            BackupManifest.CurrentVersion,
            DateTimeOffset.UnixEpoch,
            Path.Combine(_root, "jogo"),
            [
                new BackupEntry(Ysbin, true, "o", "i"),
                new BackupEntry("novo.ypf", false, null, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(injected)))),
                new BackupEntry("mexido.ypf", false, null, "hash-da-injecao"),
                new BackupEntry("sumiu.ypf", false, null, "x"),
            ]);
        var plan = new RestorePlan(Path.Combine(_root, "jogo"), backup, manifest, new Dictionary<string, string>());
        var harness = ConsoleHarness.Plain(width: 300);

        var lines = harness.Render(RunSummaries.RestoreTable(plan)).Split('\n', StringSplitOptions.TrimEntries);

        Assert.Contains(lines, l => l.StartsWith("Backup: ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("Jogo: ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith(Ysbin, StringComparison.Ordinal) && l.Contains("volta ao original") && l.Contains("2,0 KB") && l.Contains("1,0 KB"));
        Assert.Contains(lines, l => l.StartsWith("novo.ypf", StringComparison.Ordinal) && l.Contains("removido (criado pela injeção)") && l.Contains("100 B"));
        Assert.Contains(lines, l => l.StartsWith("mexido.ypf", StringComparison.Ordinal) && l.Contains("mantido (alterado depois da injeção)") && l.Contains("300 B"));
        Assert.Contains(lines, l => l.StartsWith("sumiu.ypf", StringComparison.Ordinal) && l.Contains("nada a fazer"));
    }
}
