using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

public sealed class GameCapturesFileTests : IDisposable
{
    // 09/11/2026 23:30 UTC é 20:30 em Brasília (-3): o nome do arquivo usa a hora de quem capturou.
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 23, 30, 15, TimeSpan.Zero);

    private readonly TempFolder _dir = new();
    private readonly FakeTimeProvider _clock = NewClock();

    public void Dispose() => _dir.Dispose();

    private static FakeTimeProvider NewClock()
    {
        var clock = new FakeTimeProvider(Start);
        clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT"));
        return clock;
    }

    private GameCapturesFile NewFile() => new(_dir.Path, _clock);

    private static WindowImage Solid(byte red, byte green, byte blue, int width = 4, int height = 3)
    {
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            (bgra[i], bgra[i + 1], bgra[i + 2]) = (blue, green, red);
        }

        return new WindowImage(width, height, bgra);
    }

    [Fact]
    public void A_captura_de_uma_janela_de_teste_e_gravada_com_a_data_no_nome_e_no_indice()
    {
        var capturer = new FakeCapturer(Solid(200, 100, 50));
        var file = NewFile();
        var target = new WindowTarget(Folder: Path.Combine(_dir.Path, "jogo"), TitleContains: "CLANNAD");

        var capture = file.Save("ga1b2c3d4", capturer.Capture(target));

        Assert.Equal(target, capturer.LastTarget);
        Assert.Equal("20261109-203015.png", capture.File);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 20, 30, 15, TimeSpan.FromHours(-3)), capture.CapturedAt);
        Assert.Equal((4, 3), (capture.Width, capture.Height));
        var png = file.PathOf("ga1b2c3d4", capture);
        Assert.Equal(Path.Combine(_dir.Path, "ga1b2c3d4", "20261109-203015.png"), png);
        var (width, height, rgb) = PngTestDecoder.Decode(File.ReadAllBytes(png));
        Assert.Equal((4, 3), (width, height));
        Assert.Equal(new byte[] { 200, 100, 50 }, rgb[..3]);
        Assert.Equal([capture], file.Load().Of("ga1b2c3d4"));
    }

    [Fact]
    public void Duas_capturas_no_mesmo_segundo_nao_se_sobrescrevem()
    {
        var file = NewFile();

        var first = file.Save("g1", Solid(1, 1, 1));
        var second = file.Save("g1", Solid(2, 2, 2));
        var third = file.Save("g1", Solid(3, 3, 3));

        Assert.Equal(["20261109-203015.png", "20261109-203015-2.png", "20261109-203015-3.png"], new[] { first.File, second.File, third.File });
        Assert.Equal(3, file.Load().Of("g1").Count);
        Assert.Equal((byte)2, PngTestDecoder.Decode(File.ReadAllBytes(file.PathOf("g1", second))).Rgb[0]);
    }

    [Fact]
    public void As_capturas_ficam_separadas_por_jogo_e_em_ordem_de_data()
    {
        var file = NewFile();
        file.Save("gb", Solid(1, 1, 1));
        _clock.Advance(TimeSpan.FromMinutes(5));
        file.Save("ga", Solid(2, 2, 2));
        _clock.Advance(TimeSpan.FromMinutes(5));
        file.Save("gb", Solid(3, 3, 3));

        var captures = file.Load();

        Assert.Equal(["ga", "gb"], captures.GameIds);
        Assert.Equal(["20261109-203015.png", "20261109-204015.png"], captures.Of("gb").Select(c => c.File));
        Assert.Equal(["20261109-203515.png"], captures.Of("ga").Select(c => c.File));
    }

    [Fact]
    public void A_nota_da_captura_e_guardada_sem_as_pontas_e_vazia_vira_nada()
    {
        var file = NewFile();

        var withNote = file.Save("g1", Solid(1, 1, 1), "  cena do pôr do sol  ");
        var blank = file.Save("g1", Solid(1, 1, 1), "   ");

        Assert.Equal("cena do pôr do sol", withNote.Note);
        Assert.Null(blank.Note);
        Assert.Equal("cena do pôr do sol", file.Load().Of("g1")[0].Note);
    }

    [Fact]
    public void Remove_tira_do_indice_e_apaga_a_imagem()
    {
        var file = NewFile();
        var kept = file.Save("g1", Solid(1, 1, 1));
        var dropped = file.Save("g1", Solid(2, 2, 2));

        var removed = file.Remove("g1", dropped.File);

        Assert.Equal(dropped, removed!.Capture);
        Assert.Null(removed.Warning);
        Assert.False(File.Exists(file.PathOf("g1", dropped)));
        Assert.True(File.Exists(file.PathOf("g1", kept)));
        Assert.Equal([kept], file.Load().Of("g1"));
        Assert.Null(file.Remove("g1", dropped.File));
    }

    [Fact]
    public void Remove_com_o_indice_que_nao_grava_deixa_a_imagem_e_a_captura()
    {
        var file = NewFile();
        var capture = file.Save("g1", Solid(1, 1, 1));
        var image = file.PathOf("g1", capture);
        // Um diretório no lugar do arquivo de índice faz a gravação falhar depois de a captura já ter saído do modelo.
        File.Delete(file.Path);
        Directory.CreateDirectory(file.Path);
        File.WriteAllText(Path.Combine(file.Path, "x"), "x");

        Assert.ThrowsAny<Exception>(() => file.Remove("g1", capture.File));

        Assert.True(File.Exists(image));
    }

    [Fact]
    public void Remove_que_nao_apaga_a_imagem_avisa_e_nao_desfaz_o_indice()
    {
        var file = NewFile();
        var kept = file.Save("g1", Solid(1, 1, 1));
        _clock.Advance(TimeSpan.FromMinutes(1));
        var stuck = file.Save("g1", Solid(2, 2, 2));
        var image = file.PathOf("g1", stuck);
        // Um diretório com o nome da imagem faz o File.Delete falhar em qualquer sistema.
        File.Delete(image);
        Directory.CreateDirectory(image);
        File.WriteAllText(Path.Combine(image, "x"), "x");

        var removed = file.Remove("g1", stuck.File);

        Assert.Equal(stuck, removed!.Capture);
        Assert.Contains(image, removed.Warning);
        Assert.Contains("não pôde ser apagada", removed.Warning);
        Assert.Equal([kept], file.Load().Of("g1"));
    }

    [Fact]
    public void Indice_ilegivel_e_recusado_e_nenhuma_imagem_sobra()
    {
        var file = NewFile();
        Directory.CreateDirectory(_dir.Path);
        File.WriteAllText(file.Path, "{ quebrado");

        var error = Assert.Throws<LibraryException>(() => file.Save("g1", Solid(1, 1, 1)));

        Assert.Contains(file.Path, error.Message);
        Assert.Equal("{ quebrado", File.ReadAllText(file.Path));
        Assert.False(Directory.Exists(Path.Combine(_dir.Path, "g1")));
    }

    [Fact]
    public void Se_o_indice_nao_pode_ser_gravado_a_imagem_recem_criada_e_apagada()
    {
        var file = NewFile();
        file.Save("g1", Solid(1, 1, 1));
        _clock.Advance(TimeSpan.FromMinutes(1));
        // Um diretório no lugar do arquivo de índice faz a troca falhar depois de a imagem já estar gravada.
        File.Delete(file.Path);
        Directory.CreateDirectory(file.Path);
        File.WriteAllText(Path.Combine(file.Path, "x"), "x");

        Assert.ThrowsAny<Exception>(() => file.Save("g1", Solid(2, 2, 2)));

        Assert.Equal(["20261109-203015.png"], Directory.EnumerateFiles(Path.Combine(_dir.Path, "g1")).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../fora")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("g 1")]
    public void Id_de_jogo_que_nao_serve_de_nome_de_pasta_e_recusado(string gameId)
    {
        Assert.Throws<LibraryException>(() => NewFile().Save(gameId, Solid(1, 1, 1)));
    }

    [Theory]
    [InlineData("..\\..\\fora.png")]
    [InlineData("sub/x.png")]
    [InlineData("x.txt")]
    [InlineData(".png")]
    public void Indice_com_nome_de_arquivo_que_sai_da_pasta_e_recusado(string name)
    {
        var entry = System.Text.Json.JsonSerializer.Serialize(new { file = name, capturedAt = "2026-11-09T20:30:15-03:00", width = 4, height = 3 });
        var json = "{\"version\":1,\"games\":{\"g1\":[" + entry + "]}}";

        var error = Assert.Throws<LibraryException>(() => GameCapturesFile.Parse(json, "captures.json"));

        Assert.Contains("captures.json", error.Message);
    }

    [Fact]
    public void Indice_de_outra_versao_e_recusado()
    {
        var error = Assert.Throws<LibraryException>(() => GameCapturesFile.Parse("{\"version\":9,\"games\":{}}"));

        Assert.Contains("versão 9", error.Message);
    }

    [Fact]
    public void Indice_com_a_mesma_captura_duas_vezes_e_recusado()
    {
        const string json = """{"version":1,"games":{"g1":[{"file":"a.png","capturedAt":"2026-11-09T20:30:15-03:00","width":1,"height":1},{"file":"A.png","capturedAt":"2026-11-09T20:30:16-03:00","width":1,"height":1}]}}""";

        Assert.Throws<LibraryException>(() => GameCapturesFile.Parse(json));
    }

    [Fact]
    public void A_pasta_padrao_fica_nos_dados_do_usuario_e_nao_no_repositorio()
    {
        var directory = GameCapturesFile.DefaultDirectory();

        Assert.True(Path.IsPathRooted(directory));
        Assert.EndsWith(Path.Combine("VNForge", "captures"), directory);
        Assert.DoesNotContain(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), directory, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeCapturer(WindowImage image) : IWindowCapturer
    {
        public WindowTarget? LastTarget { get; private set; }

        public WindowImage Capture(WindowTarget target)
        {
            LastTarget = target;
            return image;
        }
    }
}
