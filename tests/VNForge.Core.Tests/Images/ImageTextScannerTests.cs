using VNForge.Core.Images;

namespace VNForge.Core.Tests.Images;

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
    public async Task Le_cada_imagem_diferente_uma_vez_e_o_inventario_traz_o_texto_e_o_lugar()
    {
        var format = StandardFormat();
        var reader = new FakeImageTextReader(TextFor);

        var result = await ScanAsync(format, reader, PlanFor(format), ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, reader.Received.Count);
        Assert.Equal(3, result.Read);
        Assert.Equal(4, result.Entries.Count);
        var withText = result.Entries.Where(e => e.HasText).OrderBy(e => e.Archive + e.Path, StringComparer.Ordinal).ToList();
        Assert.Equal(
            [("cg.pak", "titulo.png", "タイトル"), ("ui.pak", "botao.png", "はじめから"), ("ui.pak", "botao2.png", "はじめから")],
            withText.Select(e => (e.Archive, e.Path, e.Text)));
        var landscape = result.Entries.Single(e => e.Path == "rua.png");
        Assert.False(landscape.HasText);
        Assert.Equal((1280, 720), (landscape.Width, landscape.Height));
    }

    [Fact]
    public async Task A_traducao_sugerida_pelo_leitor_entra_no_inventario_e_sobrevive_a_uma_nova_execucao()
    {
        var format = StandardFormat();
        var reader = new FakeImageTextReader(TextFor) { Translate = image => image.Data.AsSpan().SequenceEqual(Title) ? "Título" : string.Empty };

        var first = await ScanAsync(format, reader, PlanFor(format), ct: TestContext.Current.CancellationToken);

        Assert.Equal("Título", Assert.Single(first.Entries, e => e.Path == "titulo.png").Translation);
        Assert.All(first.Entries.Where(e => e.Path != "titulo.png"), e => Assert.Null(e.Translation));

        var second = await ScanAsync(format, new FakeImageTextReader(TextFor), PlanFor(format, first.Entries), ct: TestContext.Current.CancellationToken);

        Assert.Equal(first.Entries, second.Entries);
    }

    [Fact]
    public async Task Releitura_com_texto_diferente_e_sem_sugestao_nao_herda_a_traducao_anterior()
    {
        var format = StandardFormat();
        var first = await ScanAsync(format, new FakeImageTextReader(TextFor) { Translate = _ => "Título" }, PlanFor(format), ct: TestContext.Current.CancellationToken);

        var second = await ScanAsync(format, new FakeImageTextReader(_ => "別の文字"), PlanFor(format, first.Entries, force: true), ct: TestContext.Current.CancellationToken);

        var title = second.Entries.Single(e => e.Path == "titulo.png");
        Assert.Equal("別の文字", title.Text);
        Assert.Null(title.Translation);
    }

    [Fact]
    public async Task Limit_corta_o_numero_de_leituras_e_o_resto_fica_sem_entrada()
    {
        var format = StandardFormat();
        var reader = new FakeImageTextReader(TextFor);

        var result = await ScanAsync(format, reader, PlanFor(format, limit: 1), ct: TestContext.Current.CancellationToken);

        Assert.Single(reader.Received);
        Assert.Equal(1, result.Read);
        Assert.True(result.Entries.Count < 4);
    }

    [Fact]
    public async Task Recusa_do_modelo_nao_entra_no_inventario_e_vai_para_a_lista_de_recusas()
    {
        var format = Format(("cg.pak", [("titulo.png", Title), ("rua.png", Landscape)]));
        var reader = new FakeImageTextReader(image => image.Data.AsSpan().SequenceEqual(Title) ? "タイトル" : string.Empty) { RefuseWhen = image => image.Name.EndsWith("rua.png", StringComparison.Ordinal) };

        var result = await ScanAsync(format, reader, PlanFor(format), ct: TestContext.Current.CancellationToken);

        var refused = Assert.Single(result.Refused);
        Assert.Equal(("cg.pak", "rua.png"), (refused.Archive, refused.Path));
        Assert.Equal("titulo.png", Assert.Single(result.Entries).Path);
    }

    [Fact]
    public async Task Falhas_seguidas_param_a_execucao_e_guardam_o_que_ja_foi_lido()
    {
        var files = Enumerable.Range(0, 12).Select(i => ($"img{i:00}.png", SyntheticImages.Png(100 + i, 100, shade: (byte)i))).ToArray();
        var format = Format(("cg.pak", files), ("ui.pak", []));
        var reader = new FakeImageTextReader(_ => "x") { FailWhen = _ => true };

        var result = await ScanAsync(format, reader, PlanFor(format), concurrency: 1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result.AbortReason);
        Assert.Equal(ImageTextScanner.MaxConsecutiveFailures, result.Failed.Count);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task Resposta_cobrada_mas_inutil_entra_na_conta_dos_tokens()
    {
        var format = StandardFormat();
        var reader = new FakeImageTextReader(TextFor) { InputTokens = 1500, OutputTokens = 200, BadReplyWhen = i => i.Name.EndsWith("rua.png", StringComparison.Ordinal) };

        var result = await ScanAsync(format, reader, PlanFor(format), concurrency: 1, ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Failed);
        Assert.Equal(2, result.Read);
        Assert.Equal(3 * 1500, result.InputTokens);
        Assert.Equal(3 * 200, result.OutputTokens);
    }

    [Fact]
    public async Task Falha_sem_resposta_nao_soma_tokens()
    {
        var format = StandardFormat();
        var reader = new FakeImageTextReader(TextFor) { InputTokens = 1500, OutputTokens = 200, FailWhen = i => i.Name.EndsWith("rua.png", StringComparison.Ordinal) };

        var result = await ScanAsync(format, reader, PlanFor(format), concurrency: 1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2 * 1500, result.InputTokens);
    }

    [Fact]
    public async Task Cancelar_devolve_o_parcial_marcado()
    {
        var files = Enumerable.Range(0, 6).Select(i => ($"img{i}.png", SyntheticImages.Png(100 + i, 100, shade: (byte)i))).ToArray();
        var format = Format(("cg.pak", files), ("ui.pak", []));
        using var cts = new CancellationTokenSource();
        var reader = new FakeImageTextReader(_ => "x") { OnRead = n => { if (n == 2) { cts.Cancel(); } } };

        var result = await ScanAsync(format, reader, PlanFor(format), concurrency: 1, ct: cts.Token);

        Assert.True(result.Cancelled);
        Assert.InRange(result.Entries.Count, 2, 5);
    }

    [Fact]
    public async Task Respeita_o_numero_de_chamadas_em_paralelo()
    {
        var files = Enumerable.Range(0, 10).Select(i => ($"img{i}.png", SyntheticImages.Png(100 + i, 100, shade: (byte)i))).ToArray();
        var format = Format(("cg.pak", files), ("ui.pak", []));
        var reader = new FakeImageTextReader(_ => "x") { Delay = TimeSpan.FromMilliseconds(20) };

        await ScanAsync(format, reader, PlanFor(format), concurrency: 3, ct: TestContext.Current.CancellationToken);

        Assert.InRange(reader.MaxInFlight, 2, 3);
    }

    [Fact]
    public async Task Relata_o_progresso_ate_o_total()
    {
        var format = StandardFormat();
        var seen = new List<ImageScanProgress>();
        var progress = new SynchronousProgress(seen.Add);

        await ScanAsync(format, new FakeImageTextReader(TextFor), PlanFor(format), progress: progress, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, seen.Count);
        Assert.All(seen, p => Assert.Equal(3, p.Total));
        Assert.Equal(3, seen.Max(p => p.Done));
    }

    private sealed class SynchronousProgress(Action<ImageScanProgress> report) : IProgress<ImageScanProgress>
    {
        public void Report(ImageScanProgress value) => report(value);
    }
}
