using VNForge.Core.Images;

namespace VNForge.Core.Tests.Images;

[Trait("Risco", "R5")]
public sealed class ImageTextScannerTests : IDisposable
{
    private readonly string _game = Path.Combine(Path.GetTempPath(), "vnforge-scan-" + Guid.NewGuid().ToString("N"));

    public ImageTextScannerTests()
    {
        Directory.CreateDirectory(_game);
        FakeArchiveFormat.Touch(_game, "cg.pak", "ui.pak");
    }

    public void Dispose() => Directory.Delete(_game, recursive: true);

    private static readonly byte[] Title = SyntheticImages.Png(640, 480, shade: 10);
    private static readonly byte[] Button = SyntheticImages.Png(200, 80, shade: 20);
    private static readonly byte[] Landscape = SyntheticImages.Png(1280, 720, shade: 30);

    private static FakeArchiveFormat Format(params (string File, (string Path, byte[] Data)[] Entries)[] files) =>
        new(files.ToDictionary(f => f.File, f => (IReadOnlyDictionary<string, byte[]>)f.Entries.ToDictionary(e => e.Path, e => e.Data)));

    private static FakeArchiveFormat StandardFormat() =>
        Format(("cg.pak", [("titulo.png", Title), ("rua.png", Landscape)]), ("ui.pak", [("botao.png", Button), ("botao2.png", Button)]));

    private static string TextFor(ImageToRead image) => image.Data.Length == Title.Length && image.Data.AsSpan().SequenceEqual(Title)
        ? "タイトル"
        : image.Data.AsSpan().SequenceEqual(Button) ? "はじめから" : string.Empty;

    private ImageScanPlan PlanFor(FakeArchiveFormat format, IReadOnlyList<ImageTextEntry>? existing = null, bool force = false, int? limit = null) =>
        ImageScanPlan.Build(ImageLister.List(_game, [format], null, TestContext.Current.CancellationToken), existing ?? [], force, limit);

    private Task<ImageScanResult> ScanAsync(FakeArchiveFormat format, IImageTextReader reader, ImageScanPlan plan, int concurrency = 2, IProgress<ImageScanProgress>? progress = null, CancellationToken ct = default) =>
        new ImageTextScanner(reader, [format]).ScanAsync(_game, plan, new ImageScanRun { Concurrency = concurrency }, progress, ct);

    [Fact]
    public async Task Falha_sem_resposta_nao_soma_tokens()
    {
        var format = StandardFormat();
        var reader = new FakeImageTextReader(TextFor) { InputTokens = 1500, OutputTokens = 200, FailWhen = i => i.Name.EndsWith("rua.png", StringComparison.Ordinal) };

        var result = await ScanAsync(format, reader, PlanFor(format), concurrency: 1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2 * 1500, result.InputTokens);
    }
}
