using System.Text.Json;
using VNForge.AI.Settings;
using VNForge.Core.Images;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Images;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;

namespace VNForge.Cli.Tests;

/// <summary><c>vnf images scan</c>: o inventário das imagens com texto (#534), com um leitor falso no lugar do Claude.</summary>
public sealed class ImagesScanCommandTests : IDisposable
{
    private static readonly byte[] Title = SyntheticImages.Png(640, 480, shade: 10);
    private static readonly byte[] Button = SyntheticImages.Png(200, 80, shade: 20);
    private static readonly byte[] Street = SyntheticImages.Jpeg(1280, 720);

    private readonly CliHarness _cli = new();
    private readonly ProjectFolder _project;
    private readonly string _projectPath;
    private readonly FakeImageTextReader _reader;

    public ImagesScanCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = AiTestProject.Create(_cli.Root, _projectPath);
        var game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(game);
        var ypf = new SyntheticYpfBuilder(500)
            .AddFile(@"ui\titulo.png", Title, SyntheticYpfFileType.Png)
            .AddFile(@"ui\botao.png", Button, SyntheticYpfFileType.Png, compress: true)
            .AddFile(@"ui\icone.png", SyntheticImages.Png(16, 16), SyntheticYpfFileType.Png)
            .AddFile(@"bg\rua.jpg", Street, SyntheticYpfFileType.Png)
            .AddFile(@"bg\tela.bmp", SyntheticImages.Bmp(640, 480), SyntheticYpfFileType.Png)
            .AddFile(@"se\porta.ogg", [1, 2, 3, 4, 5], SyntheticYpfFileType.Ogg)
            .Build();
        File.WriteAllBytes(Path.Combine(game, "ui.ypf"), ypf.Bytes);

        _reader = new FakeImageTextReader(image => image.Data.AsSpan().SequenceEqual(Title) ? "ニューゲーム" : image.Data.AsSpan().SequenceEqual(Button) ? "つづきから" : string.Empty);
        _cli.Cli.ImageTextReaderFactory = _ => _reader;
        Directory.CreateDirectory(_cli.UserConfigDirectory);
    }

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Task<RunResult> Scan(params string[] extra) => _cli.RunAsync(["images", "scan", "--project", _projectPath, .. extra], Token);

    private static JsonElement Data(RunResult result)
    {
        using var document = JsonDocument.Parse(result.Output);
        return document.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task Dry_run_mostra_o_levantamento_e_o_custo_sem_chamar_a_ia_nem_gravar()
    {
        var result = await Scan("--dry-run");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("3 imagens para a IA", result.Output);
        Assert.Contains("custo estimado US$", result.Output);
        Assert.Empty(_reader.Received);
        Assert.False(File.Exists(_project.ImageInventoryPath));
    }

    [Fact]
    public async Task Dry_run_em_json_traz_as_contagens_por_motivo()
    {
        var result = await Scan("--dry-run", "--json");

        var data = Data(result);
        Assert.True(data.GetProperty("dryRun").GetBoolean());
        Assert.Equal(1, data.GetProperty("archives").GetInt32());
        Assert.Equal(3, data.GetProperty("candidates").GetInt32());
        Assert.Equal(3, data.GetProperty("toRead").GetInt32());
        var skipped = data.GetProperty("skipped");
        Assert.Equal(1, skipped.GetProperty("tooSmall").GetInt32());
        Assert.Equal(1, skipped.GetProperty("unsupportedFormat").GetInt32());
        Assert.True(data.GetProperty("estimate").GetProperty("dollars").GetDecimal() > 0m);
    }

    [Fact]
    public async Task Le_as_imagens_e_grava_o_inventario_com_o_texto_e_o_lugar()
    {
        var result = await Scan("--yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(3, _reader.Received.Count);
        var inventory = _project.LoadImageInventory();
        Assert.Equal(3, inventory.Count);
        Assert.Equal(
            [("ui.ypf", "ui/botao.png", "つづきから"), ("ui.ypf", "ui/titulo.png", "ニューゲーム")],
            inventory.Where(e => e.HasText).Select(e => (e.Archive, e.Path, e.Text)));
        Assert.Contains("2 imagens com texto", result.Output);
        Assert.Contains("images.json", result.Output);
    }

    [Fact]
    public async Task Json_traz_o_numero_de_imagens_com_texto_e_a_lista()
    {
        var result = await Scan("--yes", "--json");

        Assert.Equal(0, result.ExitCode);
        var data = Data(result);
        Assert.Equal(2, data.GetProperty("withText").GetInt32());
        Assert.Equal(3, data.GetProperty("read").GetInt32());
        var first = data.GetProperty("images").EnumerateArray().First();
        Assert.Equal("ui.ypf", first.GetProperty("archive").GetString());
        Assert.Equal("ui/botao.png", first.GetProperty("path").GetString());
        Assert.Equal("つづきから", first.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Segunda_execucao_nao_chama_a_ia_de_novo()
    {
        await Scan("--yes");
        _cli.SetInput(string.Empty);

        var second = await Scan("--yes");

        Assert.Equal(0, second.ExitCode);
        Assert.Equal(3, _reader.Received.Count);
        Assert.Contains("já estão no inventário", second.Output);
        Assert.Equal(2, _project.LoadImageInventory().Count(e => e.HasText));
    }

    [Fact]
    public async Task Force_le_tudo_de_novo()
    {
        await Scan("--yes");

        await Scan("--yes", "--force");

        Assert.Equal(6, _reader.Received.Count);
    }

    [Fact]
    public async Task Min_pixels_deixa_entrar_a_imagem_pequena()
    {
        var result = await Scan("--yes", "--min-pixels", "100");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(4, _reader.Received.Count);
    }

    [Fact]
    public async Task Pedido_acima_do_aviso_pergunta_e_a_resposta_nao_nao_gasta_nada()
    {
        File.WriteAllText(Path.Combine(_cli.UserConfigDirectory, AppBudgetFile.FileName), """{ "exchangeRate": 5, "exchangeRateOn": "2026-10-25", "warnAbove": 0.0001 }""");
        _cli.SetInput("n\n");

        var result = await Scan();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("não foi confirmada", result.Output);
        Assert.Contains("[s/N]", result.Error);
        Assert.Empty(_reader.Received);
        Assert.False(File.Exists(_project.ImageInventoryPath));
    }

    [Fact]
    public async Task Pedido_acima_do_aviso_segue_quando_a_pessoa_confirma()
    {
        File.WriteAllText(Path.Combine(_cli.UserConfigDirectory, AppBudgetFile.FileName), """{ "exchangeRate": 5, "exchangeRateOn": "2026-10-25", "warnAbove": 0.0001 }""");
        _cli.SetInput("s\n");

        var result = await Scan();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(3, _reader.Received.Count);
    }

    [Fact]
    public async Task Recusa_o_pedido_que_passaria_do_limite_de_gasto()
    {
        File.WriteAllText(Path.Combine(_cli.UserConfigDirectory, AppBudgetFile.FileName), """{ "exchangeRate": 5, "exchangeRateOn": "2026-10-25", "spendLimit": 0.0001 }""");

        var result = await Scan("--yes");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("limite de gasto", result.Output);
        Assert.Empty(_reader.Received);
    }

    [Fact]
    public async Task Imagem_que_falha_sai_no_resumo_e_o_resto_fica_gravado()
    {
        _reader.FailWhen = image => image.Name.EndsWith("rua.jpg", StringComparison.Ordinal);

        var result = await Scan("--yes");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ui.ypf/bg/rua.jpg", result.Output);
        Assert.Equal(2, _project.LoadImageInventory().Count);
    }

    [Fact]
    public async Task Imagem_recusada_pelo_modelo_e_avisada_e_nao_entra_no_inventario()
    {
        _reader.RefuseWhen = image => image.Name.EndsWith("rua.jpg", StringComparison.Ordinal);

        var result = await Scan("--yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1 imagem recusada", result.Output);
        Assert.DoesNotContain(_project.LoadImageInventory(), e => e.Path == "bg/rua.jpg");
    }

    [Fact]
    public async Task Limit_corta_as_leituras()
    {
        var result = await Scan("--yes", "--limit", "1");

        Assert.Equal(0, result.ExitCode);
        Assert.Single(_reader.Received);
    }

    [Fact]
    public async Task Modelo_desconhecido_e_recusado()
    {
        var result = await Scan("--model", "gpt-5");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Modelo desconhecido", result.Error + result.Output);
    }

    [Fact]
    public async Task Sem_projeto_o_erro_e_em_portugues()
    {
        var result = await _cli.RunAsync(["images", "scan", "--project", Path.Combine(_cli.Root, "nao-existe")], Token);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("A pasta do projeto não existe", result.Error + result.Output);
    }
}
