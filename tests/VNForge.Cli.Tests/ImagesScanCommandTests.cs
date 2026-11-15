using System.Text.Json;
using VNForge.AI.Cost;
using VNForge.AI.Settings;
using VNForge.Core.Images;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Images;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;

namespace VNForge.Cli.Tests;

/// <summary><c>vnf images scan</c>: o inventário das imagens com texto (#534), com um leitor falso no lugar do Claude.</summary>
[Trait("Risco", "R5")]
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
    public async Task Recusa_o_pedido_que_passaria_do_limite_de_gasto()
    {
        File.WriteAllText(Path.Combine(_cli.UserConfigDirectory, AppBudgetFile.FileName), """{ "exchangeRate": 5, "exchangeRateOn": "2026-10-25", "spendLimit": 0.0001 }""");

        var result = await Scan("--yes");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("limite de gasto", result.Output);
        Assert.Empty(_reader.Received);
    }
}
