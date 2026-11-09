using System.Text.Json;
using VNForge.Core.Library;

namespace VNForge.Cli.Tests;

public sealed class LibraryCapturesCommandTests : IDisposable
{
    // 09/11/2026 23:30:15 UTC é 20:30:15 em Brasília (-3).
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 23, 30, 15, TimeSpan.Zero);

    private readonly CliHarness _cli = new();
    private readonly BrasiliaClock _clock = new(Start);
    private readonly FakeCapturer _capturer = new();

    public LibraryCapturesCommandTests()
    {
        _cli.Cli.Clock = _clock;
        _cli.Cli.WindowCapturer = _capturer;
    }

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<RunResult> Run(params string[] args) => await _cli.RunAsync(args, Ct);

    private static JsonElement Data(RunResult result) => JsonDocument.Parse(result.Output).RootElement.GetProperty("data");

    private string CapturesRoot => Path.Combine(_cli.Root, "user-captures");

    private LibraryGame AddGame(string folderName = "CLANNAD", string title = "CLANNAD")
    {
        var folder = Path.Combine(_cli.Root, "colecao", folderName);
        Directory.CreateDirectory(folder);
        var game = new LibraryGame(GameLibrary.IdFor(folder), folder, $"{folderName}.exe", LibraryEngine.Unknown, title, null, Start);
        var file = new LibraryFile(_cli.UserConfigDirectory);
        var library = file.Load();
        library.Merge([game], Path.Combine(_cli.Root, "colecao"));
        file.Save(library);
        return game;
    }

    // ---- notes ----

    [Fact]
    public async Task Notes_sem_nota_diz_como_escrever()
    {
        AddGame();

        var result = await Run("library", "notes", "CLANNAD");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("sem notas", result.Output);
        Assert.Contains("--set", result.Output);
    }

    [Fact]
    public async Task Notes_set_grava_e_mostra_a_nota_com_a_data_local()
    {
        var game = AddGame();

        var set = await Run("library", "notes", "CLANNAD", "--set", "Voltar no capítulo 3");
        var show = await Run("library", "notes", "CLANNAD");

        Assert.Equal(0, set.ExitCode);
        Assert.Contains("nota gravada", set.Output);
        Assert.Contains("Voltar no capítulo 3", show.Output);
        Assert.Contains("09/11/2026 20:30", show.Output);
        Assert.Equal("Voltar no capítulo 3", new GameNotesFile(_cli.UserConfigDirectory).Load().Of(game.Id)!.Text);
    }

    [Fact]
    public async Task Notes_add_acrescenta_em_linha_nova_e_set_com_traco_le_a_entrada_padrao()
    {
        var game = AddGame();
        _cli.SetInput("primeira linha\nsegunda linha\n");
        await Run("library", "notes", "CLANNAD", "--set", "-");

        await Run("library", "notes", "CLANNAD", "--add", "terceira");

        Assert.Equal("primeira linha\nsegunda linha\nterceira", new GameNotesFile(_cli.UserConfigDirectory).Load().Of(game.Id)!.Text);
    }

    [Fact]
    public async Task Notes_clear_apaga_e_texto_em_branco_no_set_tambem()
    {
        var game = AddGame();
        await Run("library", "notes", "CLANNAD", "--set", "x");

        var cleared = await Run("library", "notes", "CLANNAD", "--clear");
        await Run("library", "notes", "CLANNAD", "--set", "y");
        var blank = await Run("library", "notes", "CLANNAD", "--set", "   ");

        Assert.Contains("nota apagada", cleared.Output);
        Assert.Contains("nota apagada", blank.Output);
        Assert.Null(new GameNotesFile(_cli.UserConfigDirectory).Load().Of(game.Id));
    }

    [Fact]
    public async Task Notes_com_o_mesmo_texto_nao_muda_nada_nem_a_data()
    {
        var game = AddGame();
        await Run("library", "notes", "CLANNAD", "--set", "igual");
        _clock.Advance(TimeSpan.FromHours(1));

        var again = await Run("library", "notes", "CLANNAD", "--set", "igual");

        Assert.Contains("já estava assim", again.Output);
        Assert.Equal(Start,new GameNotesFile(_cli.UserConfigDirectory).Load().Of(game.Id)!.UpdatedAt.ToUniversalTime());
    }

    [Fact]
    public async Task Notes_recusa_duas_acoes_ao_mesmo_tempo()
    {
        AddGame();

        var result = await Run("library", "notes", "CLANNAD", "--set", "a", "--clear");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("só uma", result.Error);
    }

    [Fact]
    public async Task Notes_em_json_traz_a_nota_e_se_mudou()
    {
        AddGame();

        var data = Data(await Run("--json", "library", "notes", "CLANNAD", "--set", "texto"));

        Assert.True(data.GetProperty("changed").GetBoolean());
        Assert.Equal("texto", data.GetProperty("note").GetProperty("text").GetString());
        Assert.Equal("CLANNAD", data.GetProperty("game").GetProperty("title").GetString());
    }

    [Fact]
    public async Task Notes_de_jogo_que_nao_existe_e_erro_esperado()
    {
        var result = await Run("library", "notes", "Fantasma");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Nenhum jogo da biblioteca", result.Error);
    }

    // ---- capture ----

    [Fact]
    public async Task Capture_take_grava_a_imagem_com_a_data_na_pasta_do_usuario_e_procura_na_pasta_do_jogo()
    {
        var game = AddGame();
        _capturer.Image = Solid(9, 8, 7);

        var result = await Run("library", "capture", "take", "CLANNAD");

        Assert.Equal(0, result.ExitCode);
        var expected = Path.Combine(CapturesRoot, game.Id, "20261109-203015.png");
        Assert.Contains(expected, result.Output);
        Assert.True(File.Exists(expected));
        Assert.Equal(new WindowTarget(Folder: game.Folder), _capturer.LastTarget);
        Assert.StartsWith(_cli.Root, expected);
        Assert.False(Directory.Exists(Path.Combine(game.Folder, "20261109-203015.png")));
        Assert.Empty(Directory.EnumerateFiles(game.Folder));
    }

    [Fact]
    public async Task Capture_take_com_title_acha_a_janela_pelo_titulo_e_guarda_a_nota()
    {
        AddGame();
        _capturer.Image = Solid(1, 2, 3, new WindowSource("Clannad HD - Navegador", @"C:\Program Files\Browser\browser.exe"));

        var data = Data(await Run("--json", "library", "capture", "take", "CLANNAD", "--title", "Clannad HD", "--note", "pôr do sol"));

        Assert.Equal(new WindowTarget(TitleContains: "Clannad HD"), _capturer.LastTarget);
        var capture = data.GetProperty("capture");
        Assert.Equal("20261109-203015.png", capture.GetProperty("file").GetString());
        Assert.Equal("pôr do sol", capture.GetProperty("note").GetString());
        Assert.Equal(6, capture.GetProperty("width").GetInt32());
        Assert.True(capture.GetProperty("exists").GetBoolean());
        var window = data.GetProperty("window");
        Assert.Equal("Clannad HD - Navegador", window.GetProperty("title").GetString());
        Assert.Equal(@"C:\Program Files\Browser\browser.exe", window.GetProperty("program").GetString());
    }

    [Fact]
    public async Task Capture_take_diz_de_qual_janela_veio_a_imagem()
    {
        AddGame();
        _capturer.Image = Solid(1, 2, 3, new WindowSource("Clannad HD - Navegador", @"C:\Program Files\Browser\browser.exe"));

        var result = await Run("library", "capture", "take", "CLANNAD", "--title", "Clannad HD");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Janela capturada: 'Clannad HD - Navegador', do programa browser.exe.", result.Output);
    }

    [Fact]
    public async Task Capture_take_com_janela_de_programa_desconhecido_diz_que_nao_sabe()
    {
        AddGame();
        _capturer.Image = Solid(1, 2, 3, new WindowSource("Clannad", null));

        var text = await Run("library", "capture", "take", "CLANNAD");
        var window = Data(await Run("--json", "library", "capture", "take", "CLANNAD")).GetProperty("window");

        Assert.Contains("Janela capturada: 'Clannad', programa desconhecido.", text.Output);
        Assert.Equal("Clannad", window.GetProperty("title").GetString());
        Assert.False(window.TryGetProperty("program", out _));
    }

    [Fact]
    public async Task Capture_take_sem_janela_e_erro_esperado_e_nada_e_gravado()
    {
        AddGame();
        _capturer.Failure = new LibraryException("Não achei nenhuma janela aberta de um programa da pasta 'X'.");

        var result = await Run("library", "capture", "take", "CLANNAD");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Não achei nenhuma janela", result.Error);
        Assert.False(Directory.Exists(CapturesRoot));
    }

    [Fact]
    public async Task Capture_list_mostra_as_capturas_em_ordem_e_diz_quando_nao_ha()
    {
        AddGame();
        var empty = await Run("library", "capture", "list", "CLANNAD");
        _capturer.Image = Solid(1, 1, 1);
        await Run("library", "capture", "take", "CLANNAD", "--note", "primeira");
        _clock.Advance(TimeSpan.FromMinutes(2));
        await Run("library", "capture", "take", "CLANNAD");

        var list = await Run("library", "capture", "list", "CLANNAD");
        var json = Data(await Run("--json", "library", "capture", "list", "CLANNAD"));

        Assert.Contains("sem capturas", empty.Output);
        Assert.Contains("2 capturas", list.Output);
        Assert.Contains("20261109-203015.png", list.Output);
        Assert.Contains("20261109-203215.png", list.Output);
        Assert.Contains("primeira", list.Output);
        Assert.Equal(["20261109-203015.png", "20261109-203215.png"], json.GetProperty("captures").EnumerateArray().Select(c => c.GetProperty("file").GetString()));
    }

    [Fact]
    public async Task Capture_remove_apaga_a_imagem_e_recusa_arquivo_que_o_jogo_nao_tem()
    {
        var game = AddGame();
        _capturer.Image = Solid(1, 1, 1);
        await Run("library", "capture", "take", "CLANNAD");
        var path = Path.Combine(CapturesRoot, game.Id, "20261109-203015.png");

        var missing = await Run("library", "capture", "remove", "CLANNAD", "outra.png");
        var removed = await Run("library", "capture", "remove", "CLANNAD", "20261109-203015.png");

        Assert.NotEqual(0, missing.ExitCode);
        Assert.Contains("não tem a captura 'outra.png'", missing.Error);
        Assert.Equal(0, removed.ExitCode);
        Assert.False(File.Exists(path));
        Assert.Empty(new GameCapturesFile(CapturesRoot).Load().Of(game.Id));
    }

    [Fact]
    public async Task Capture_remove_em_json_diz_o_que_saiu_e_nao_traz_aviso()
    {
        var game = AddGame();
        _capturer.Image = Solid(1, 1, 1);
        await Run("library", "capture", "take", "CLANNAD");

        var data = Data(await Run("--json", "library", "capture", "remove", "CLANNAD", "20261109-203015.png"));

        Assert.Equal("20261109-203015.png", data.GetProperty("removed").GetString());
        Assert.False(data.TryGetProperty("warning", out _));
        Assert.Equal(game.Id, data.GetProperty("game").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Capture_remove_com_imagem_que_nao_apaga_avisa_e_mantem_o_indice_sem_a_captura()
    {
        var game = AddGame();
        _capturer.Image = Solid(1, 1, 1);
        await Run("library", "capture", "take", "CLANNAD");
        var path = Path.Combine(CapturesRoot, game.Id, "20261109-203015.png");
        // Um diretório com o nome da imagem faz o File.Delete falhar em qualquer sistema.
        File.Delete(path);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "x"), "x");

        var result = await Run("library", "capture", "remove", "CLANNAD", "20261109-203015.png");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("tirada da lista", result.Output);
        Assert.Contains("Aviso:", result.Output);
        Assert.Contains(path, result.Output);
        Assert.Empty(new GameCapturesFile(CapturesRoot).Load().Of(game.Id));
    }

    [Fact]
    public async Task As_capturas_de_cada_jogo_ficam_separadas()
    {
        var first = AddGame();
        var second = AddGame("Little Busters", "Little Busters!");
        _capturer.Image = Solid(1, 1, 1);

        await Run("library", "capture", "take", "CLANNAD");
        await Run("library", "capture", "take", "Busters");

        var captures = new GameCapturesFile(CapturesRoot).Load();
        Assert.Single(captures.Of(first.Id));
        Assert.Single(captures.Of(second.Id));
        Assert.NotEqual(first.Id, second.Id);
    }

    private static WindowImage Solid(byte red, byte green, byte blue, WindowSource? source = null)
    {
        var bgra = new byte[6 * 2 * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            (bgra[i], bgra[i + 1], bgra[i + 2]) = (blue, green, red);
        }

        return new WindowImage(6, 2, bgra, source);
    }

    /// <summary>Relógio à mão cuja hora local é Brasília (-3), para o teste não depender do fuso de quem roda.</summary>
    private sealed class BrasiliaClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeCapturer : IWindowCapturer
    {
        public WindowImage Image { get; set; } = new(1, 1, [0, 0, 0, 0]);

        public LibraryException? Failure { get; set; }

        public WindowTarget? LastTarget { get; private set; }

        public WindowImage Capture(WindowTarget target)
        {
            LastTarget = target;
            return Failure is null ? Image : throw Failure;
        }
    }
}
