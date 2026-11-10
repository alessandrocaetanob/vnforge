using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Library;

namespace VNForge.App.Tests;

/// <summary>A aba Jogar (#553) sem janela: capturas e notas sobre arquivos reais, o atalho e a janela do jogo trocados por falsos.</summary>
public sealed class GameCapturesViewModelTests : IDisposable
{
    private static readonly LibraryEngine Engine = new("yuris", "Yu-RIS", LibraryEngineSource.Engine);

    private readonly TempFolder _data = new();
    private readonly TempFolder _game = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 8, 21, 14, 0, TimeSpan.Zero));
    private readonly FakeHotkey _hotkey = new();
    private readonly FakeCapturer _capturer = new();

    public void Dispose()
    {
        _data.Dispose();
        _game.Dispose();
    }

    private LibraryGame Game => new(GameLibrary.IdFor(_game.Path), GameLibrary.NormalizeFolder(_game.Path), "game.exe", Engine, "Amaoto no Hakoniwa", null, _clock.GetUtcNow());

    private GameCapturesViewModel Create(IGlobalHotkey? hotkey = null) => new()
    {
        CapturesFile = new GameCapturesFile(_data.SubFolder("captures"), _clock),
        NotesFile = new GameNotesFile(_data.SubFolder("config")),
        Capturer = _capturer,
        Hotkey = hotkey ?? _hotkey,
        Clock = _clock,
    };

    [Fact]
    public async Task O_atalho_captura_a_janela_do_jogo_e_guarda_a_imagem_com_a_data()
    {
        var vm = Create();
        vm.Enter(Game);

        _hotkey.Press();
        await vm.Capturing;

        Assert.Equal(Game.Folder, _capturer.Target?.Folder);
        var item = Assert.Single(vm.Captures);
        Assert.Equal("08/11 21:14", item.Label);
        Assert.Equal("20261108-211400.png", item.File);
        Assert.True(File.Exists(item.Path));
        Assert.StartsWith(_data.Path, item.Path, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.BlackWarning);
        Assert.True(vm.HasCaptures);
    }

    [Fact]
    public async Task Capturas_ja_guardadas_voltam_da_mais_nova_para_a_mais_antiga()
    {
        var first = Create();
        first.Enter(Game);
        _hotkey.Press();
        await first.Capturing;
        _clock.Advance(TimeSpan.FromHours(2));
        _hotkey.Press();
        await first.Capturing;
        first.Leave();

        var vm = Create();
        vm.Enter(Game);

        Assert.Equal(["08/11 23:14", "08/11 21:14"], vm.Captures.Select(c => c.Label));
    }

    [Fact]
    public async Task Captura_toda_preta_avisa_da_tela_cheia_exclusiva_mas_fica_guardada()
    {
        _capturer.Black = true;
        var vm = Create();
        vm.Enter(Game);

        _hotkey.Press();
        await vm.Capturing;

        Assert.True(vm.BlackWarning);
        Assert.Single(vm.Captures);
    }

    [Fact]
    public async Task Captura_que_falha_mostra_o_motivo_e_nao_entra_na_grade()
    {
        _capturer.Failure = "Não há janela do jogo aberta.";
        var vm = Create();
        vm.Enter(Game);

        _hotkey.Press();
        await vm.Capturing;

        Assert.Equal("Não há janela do jogo aberta.", vm.ErrorMessage);
        Assert.Empty(vm.Captures);
    }

    [Fact]
    public void O_atalho_so_fica_registrado_enquanto_a_aba_esta_aberta_e_avisa_se_a_tecla_esta_em_uso()
    {
        var vm = Create();

        vm.Enter(Game);
        Assert.Equal(1, _hotkey.Active);
        vm.Leave();
        Assert.Equal(0, _hotkey.Active);

        var taken = Create(new FakeHotkey { Taken = true });
        taken.Enter(Game);
        Assert.True(taken.HotkeyTaken);
    }

    [Fact]
    public void A_nota_e_gravada_depois_de_uma_pausa_na_digitacao_e_volta_na_proxima_vez()
    {
        var vm = Create();
        vm.Enter(Game);

        vm.NoteText = "Voltar na rota da Aoi.";
        Assert.False(File.Exists(new GameNotesFile(_data.SubFolder("config")).Path));
        _clock.Advance(GameCapturesViewModel.SaveDelay);
        Assert.Equal("Salvo agora", vm.SavedText);
        _clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal("Salvo há 4 segundos", vm.SavedText);

        var again = Create();
        again.Enter(Game);
        Assert.Equal("Voltar na rota da Aoi.", again.NoteText);
        Assert.Equal("Salvo há 4 segundos", again.SavedText);
    }

    [Fact]
    public void Sair_da_aba_grava_a_nota_que_ainda_esperava()
    {
        var vm = Create();
        vm.Enter(Game);
        vm.NoteText = "Capítulo 4.";

        vm.Leave();

        Assert.Equal("Capítulo 4.", new GameNotesFile(_data.SubFolder("config")).Load().Of(Game.Id)?.Text);
    }

    [Fact]
    public void Projeto_fora_da_Biblioteca_nao_tem_capturas_nem_atalho()
    {
        var vm = Create();

        vm.Enter(null);

        Assert.True(vm.HasNoGame);
        Assert.Equal(0, _hotkey.Active);
    }

    [Fact]
    public async Task A_Biblioteca_acha_o_jogo_pela_pasta_do_projeto_de_traducao()
    {
        var game = Game with { ProjectPath = GameLibrary.NormalizeFolder(_data.SubFolder("projeto")) };
        var other = game with { Id = "g00000000", ProjectPath = null };
        var file = LibrarySamples.Save(_data.SubFolder("library"), new GameLibrary([other, game]));
        var library = new LibraryViewModel(file, new FakeFolderPicker(null), action => action(), clock: _clock);
        await library.LoadAsync();

        Assert.Equal(game.Id, library.GameOfProject(_data.Path + "/projeto/")?.Id);
        Assert.Null(library.GameOfProject(_data.SubFolder("outro")));
    }

    private sealed class FakeHotkey : IGlobalHotkey
    {
        private Action? _pressed;

        public bool Taken { get; init; }

        public int Active { get; private set; }

        public IDisposable? Register(Action pressed)
        {
            if (Taken)
            {
                return null;
            }

            _pressed = pressed;
            Active++;
            return new Release(this);
        }

        public void Press() => _pressed?.Invoke();

        private sealed class Release(FakeHotkey owner) : IDisposable
        {
            public void Dispose()
            {
                owner.Active--;
                owner._pressed = null;
            }
        }
    }

    private sealed class FakeCapturer : IWindowCapturer
    {
        public WindowTarget? Target { get; private set; }

        public bool Black { get; set; }

        public string? Failure { get; set; }

        public WindowImage Capture(WindowTarget target)
        {
            Target = target;
            if (Failure is not null)
            {
                throw new LibraryException(Failure);
            }

            var bgra = new byte[4 * 4 * 4];
            if (!Black)
            {
                Array.Fill(bgra, (byte)0x80);
            }

            return new WindowImage(4, 4, bgra);
        }
    }
}
