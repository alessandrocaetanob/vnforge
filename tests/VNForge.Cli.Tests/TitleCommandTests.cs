using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscfg.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Ypf;
using VNForge.Engines.YuRis.Yscfg;

namespace VNForge.Cli.Tests;

/// <summary>O título da janela do jogo na CLI: <c>vnf extract</c> o lê, <c>vnf title</c> o traduz e <c>vnf build</c> o grava no <c>yscfg.ybn</c> (#427).</summary>
public sealed class TitleCommandTests : IDisposable
{
    private const string OriginalTitle = "テストの題名";
    private const string TitleVerb = "title";
    private const string JsonFlag = "--json";
    private const string ConfigEntry = "ysbin/yscfg.ybn";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();
    private readonly string _game;

    public TitleCommandTests()
    {
        _game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "MinhaVN.exe"), "MZ");
        var script = new SyntheticYstbBuilder(Yscm).WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        script.AddWord("雨が降っていた。", 1).AddReturnCode(0, 2);
        File.WriteAllBytes(
            Path.Combine(_game, "ysbin.ypf"),
            new SyntheticYpfBuilder(500)
                .AddFile("ysc.ybn", Yscm.Bytes)
                .AddFile("yst00001.ybn", script.Build().Bytes)
                .AddFile(ConfigEntry, SyntheticYscfgBuilder.Build(OriginalTitle))
                .Build().Bytes);
    }

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string ProjectPath => Path.Combine(_cli.WorkingDirectory, "minha-vn-ptbr");

    private async Task Init(string encoding = "cp1252")
    {
        var init = await _cli.RunAsync(["init", "minha-vn-ptbr", "--game", _game, "--encoding", encoding], Token);
        Assert.Equal(0, init.ExitCode);
    }

    private Task<RunResult> Run(params string[] args) => _cli.RunAsync([.. args, "--project", ProjectPath], Token);

    private async Task InitAndExtract(string encoding = "cp1252")
    {
        await Init(encoding);
        var extract = await Run("extract");
        Assert.Equal(0, extract.ExitCode);
    }

    private ProjectConfig Config() => ProjectFolder.Open(ProjectPath).LoadConfig();

    [Fact]
    public async Task Extract_le_o_titulo_do_jogo_e_o_mostra_na_saida()
    {
        await Init();

        var result = await Run("extract");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"Título do jogo: {OriginalTitle}", result.Output, StringComparison.Ordinal);
        Assert.Equal(new GameTitleConfig(OriginalTitle, null), Config().Game.Title);
    }

    [Fact]
    public async Task Extract_json_traz_o_titulo()
    {
        await Init();

        var result = await Run("extract", JsonFlag);

        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal(OriginalTitle, doc.RootElement.GetProperty("data").GetProperty("gameTitle").GetString());
    }

    [Fact]
    public async Task Show_antes_da_extracao_diz_para_rodar_o_extract()
    {
        await Init();

        var result = await Run(TitleVerb, "show");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("vnf extract", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_mostra_o_original_e_a_traducao()
    {
        await InitAndExtract();
        await Run(TitleVerb, "set", "Meu Jogo");

        var result = await Run(TitleVerb, "show");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(OriginalTitle, result.Output, StringComparison.Ordinal);
        Assert.Contains("Meu Jogo", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_json_devolve_original_traducao_e_estrategia()
    {
        await InitAndExtract();
        await Run(TitleVerb, "set", "Meu Jogo");

        var result = await Run(TitleVerb, "show", JsonFlag);

        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(OriginalTitle, data.GetProperty("original").GetString());
        Assert.Equal("Meu Jogo", data.GetProperty("translation").GetString());
        Assert.Equal("cp1252", data.GetProperty("encoding").GetString());
        Assert.True(data.GetProperty("writable").GetBoolean());
    }

    [Fact]
    public async Task Set_grava_a_traducao_no_vnforge_json_e_mantem_o_original()
    {
        await InitAndExtract();

        var result = await Run(TitleVerb, "set", "  Meu Título  ");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(new GameTitleConfig(OriginalTitle, "Meu Título"), Config().Game.Title);
    }

    [Fact]
    public async Task Set_antes_da_extracao_falha_e_manda_rodar_o_extract()
    {
        await Init();

        var result = await Run(TitleVerb, "set", "Meu Jogo");

        Assert.Equal(1, result.ExitCode);
        Assert.Null(Config().Game.Title);
    }

    [Fact]
    public async Task Set_com_titulo_em_branco_falha()
    {
        await InitAndExtract();

        var result = await Run(TitleVerb, "set", "   ");

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task Set_com_caractere_que_a_estrategia_nao_grava_salva_e_avisa()
    {
        await InitAndExtract("sjis");

        var result = await Run(TitleVerb, "set", "Título");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Aviso", result.Output, StringComparison.Ordinal);
        Assert.Contains("sjis", result.Output, StringComparison.Ordinal);
        Assert.Equal("Título", Config().Game.Title!.Translation);
    }

    [Fact]
    public async Task Clear_apaga_a_traducao_e_mantem_o_original()
    {
        await InitAndExtract();
        await Run(TitleVerb, "set", "Meu Jogo");

        var result = await Run(TitleVerb, "clear");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(new GameTitleConfig(OriginalTitle, null), Config().Game.Title);
    }

    [Fact]
    public async Task Build_grava_o_titulo_traduzido_no_yscfg_do_pacote_e_so_ele_muda()
    {
        await InitAndExtract();
        await Run(TitleVerb, "set", "Meu Título");

        var result = await Run("build", "--allow-warnings", JsonFlag);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal("Meu Título", doc.RootElement.GetProperty("data").GetProperty("gameTitle").GetString());
        var built = YscfgReader.Read(ReadEntry(Path.Combine(ProjectPath, "build", "ysbin.ypf"), ConfigEntry), "yscfg.ybn");
        Assert.Equal("Meu Título", built.Title);
        var original = SyntheticYscfgBuilder.Build(OriginalTitle);
        Assert.Equal(original.AsSpan(0, built.TitleOffset).ToArray(), ReadEntry(Path.Combine(ProjectPath, "build", "ysbin.ypf"), ConfigEntry).AsSpan(0, built.TitleOffset).ToArray());
        Assert.Equal(GameEncodings.Windows1252.GetBytes("Meu Título"), ReadEntry(Path.Combine(ProjectPath, "build", "ysbin.ypf"), ConfigEntry)[(built.TitleOffset + 2)..]);
    }

    [Fact]
    public async Task Build_sem_traducao_do_titulo_deixa_o_yscfg_identico_ao_do_jogo()
    {
        await InitAndExtract();

        var result = await Run("build", "--allow-warnings");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(SyntheticYscfgBuilder.Build(OriginalTitle), ReadEntry(Path.Combine(ProjectPath, "build", "ysbin.ypf"), ConfigEntry));
    }

    [Fact]
    public async Task Check_acusa_titulo_que_a_estrategia_nao_grava()
    {
        await InitAndExtract("sjis");
        await Run(TitleVerb, "set", "Título");
        _cli.Cli.Qa = VNForge.Core.Qa.QaEngine.Default;

        var result = await Run("check", JsonFlag);

        Assert.Contains("titulo-nao-codificavel", result.Output, StringComparison.Ordinal);
    }

    private static byte[] ReadEntry(string ypf, string entryPath)
    {
        using var stream = File.OpenRead(ypf);
        using var archive = (YpfArchive)new YpfArchiveFormat().Open(stream);
        using var content = archive.OpenEntry(archive.Entries.Single(e => e.Path == entryPath));
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        return buffer.ToArray();
    }
}
