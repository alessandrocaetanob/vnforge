using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Library;
using VNForge.Core.Projects;

namespace VNForge.App.Tests;

/// <summary>O controle de conteúdo adulto (#631) fora da Biblioteca: a aba Privacidade dos Ajustes, o shell que a liga ao mesmo arquivo e o "Mostrar" que vale só nesta sessão.</summary>
public sealed class AdultPrivacyTests : IDisposable
{
    private readonly TempFolder _config = new();

    public void Dispose() => _config.Dispose();

    private LibraryFile File => new(_config.Path);

    private async Task<(MainWindowViewModel Shell, LibraryViewModel Library)> ShellAsync(IEnumerable<LibraryGame>? games = null)
    {
        LibrarySamples.Save(_config.Path, LibrarySamples.Library(games));
        var library = new LibraryViewModel(File, new FakeFolderPicker(null), action => action(), clock: new FixedClock(LibrarySamples.Now), readTranslation: LibrarySamples.Translation, sheets: new LibrarySamples.Sheets());
        var start = new StartViewModel(new RecentProjectsFile(_config.Path), new FakeFolderPicker(null), new FixedClock(LibrarySamples.Now));
        var shell = new MainWindowViewModel(start, library: library);
        await library.LoadAsync();
        return (shell, library);
    }

    [Fact]
    public void A_aba_marca_o_modo_de_agora_e_escolher_outro_cartao_grava_uma_vez_sem_regravar_ao_reler()
    {
        var mode = AdultMode.Blur;
        var writes = new List<AdultMode>();
        var privacy = new PrivacySettingsViewModel(() => mode, m =>
        {
            writes.Add(m);
            mode = m;
            return true;
        });

        Assert.Equal([AdultMode.Show, AdultMode.Blur, AdultMode.Hide], privacy.Modes.Select(m => m.Mode));
        Assert.Equal([false, true, false], privacy.Modes.Select(m => m.IsSelected));
        Assert.Equal([false, true, false], privacy.Modes.Select(m => m.IsDefault));

        privacy.Modes[2].IsSelected = true;

        Assert.Equal([AdultMode.Hide], writes);

        mode = AdultMode.Show;
        privacy.Refresh();

        Assert.Equal([true, false, false], privacy.Modes.Select(m => m.IsSelected));
        Assert.Equal([AdultMode.Hide], writes);
        Assert.Equal("vnf library privacy --adult mostrar|desfocar|esconder", privacy.CommandText);
    }

    [Fact]
    public async Task A_aba_Privacidade_e_o_atalho_da_Biblioteca_gravam_e_leem_o_mesmo_modo_do_arquivo()
    {
        var (shell, library) = await ShellAsync();
        var privacy = shell.Settings.Privacy!;

        Assert.True(shell.Settings.HasPrivacy);
        Assert.Equal(AdultMode.Blur, privacy.Modes.Single(m => m.IsSelected).Mode);

        privacy.Modes.Single(m => m.Mode == AdultMode.Hide).IsSelected = true;

        Assert.Equal(AdultMode.Hide, library.AdultMode);
        Assert.Equal(AdultMode.Hide, File.Load().Privacy.Adult);
        Assert.DoesNotContain(library.Cards, c => c.Title == "Kuroneko Archive");

        library.ChooseAdultMode(AdultMode.Show);

        Assert.Equal(AdultMode.Show, privacy.Modes.Single(m => m.IsSelected).Mode);
    }

    [Fact]
    public void Sem_gravar_o_cartao_volta_ao_modo_que_vale_e_a_aba_mostra_o_erro()
    {
        var failing = true;
        var privacy = new PrivacySettingsViewModel(() => AdultMode.Blur, _ => !failing, () => "disco cheio");

        privacy.Modes[2].IsSelected = true;

        Assert.Equal([false, true, false], privacy.Modes.Select(m => m.IsSelected));
        Assert.Equal("disco cheio", privacy.ErrorMessage);
        Assert.True(privacy.HasError);

        failing = false;
        privacy.Modes[0].IsSelected = true;

        Assert.False(privacy.HasError);
    }

    [Fact]
    public async Task Com_o_arquivo_travado_a_aba_Privacidade_fica_no_modo_gravado_com_o_aviso_dela()
    {
        var (shell, library) = await ShellAsync();
        using var locked = new FileStream(Path.Combine(_config.Path, LibraryFile.FileName), FileMode.Open, FileAccess.Read, FileShare.None);

        shell.Settings.Privacy!.Modes.Single(m => m.Mode == AdultMode.Hide).IsSelected = true;

        Assert.Equal(AdultMode.Blur, library.AdultMode);
        Assert.Equal(AdultMode.Blur, shell.Settings.Privacy.Modes.Single(m => m.IsSelected).Mode);
        Assert.True(shell.Settings.Privacy.HasError);
    }

    private sealed class FakeImages(Func<LibraryPrivacy> privacy) : IVndbImageSource
    {
        public event EventHandler? PrivacyChanged;

        public LibraryPrivacy Privacy => privacy();

        public void Raise() => PrivacyChanged?.Invoke(this, EventArgs.Empty);

        public BitmapLease? TryGet(VNForge.Core.Vndb.VndbImageRef image) => null;

        public void RevalidatePrivacy()
        {
        }

        public Task<BitmapLease?> LoadAsync(VNForge.Core.Vndb.VndbImageRef image, CancellationToken cancellationToken) => Task.FromResult<BitmapLease?>(null);
    }

    [Fact]
    public async Task O_modo_trocado_pela_CLI_chega_a_grade_ao_popover_e_a_aba_pelo_aviso_da_fonte_de_imagens()
    {
        var (shell, library) = await ShellAsync();
        var source = new FakeImages(() => File.Load().Privacy);
        library.FollowPrivacy(source);
        library.RevealGameCommand.Execute(library.Cards.Single(c => c.Title == "Kuroneko Archive"));

        File.Update(l => l.Privacy = l.Privacy with { Adult = AdultMode.Hide });
        source.Raise();
        await library.Loading;

        Assert.Equal(AdultMode.Hide, library.AdultMode);
        Assert.DoesNotContain(library.Cards, c => c.Title == "Kuroneko Archive");
        Assert.Equal("1 adulto escondido", library.HiddenAdultText);
        Assert.Equal(AdultMode.Hide, library.AdultModes.Single(m => m.IsSelected).Mode);
        Assert.Equal(AdultMode.Hide, shell.Settings.Privacy!.Modes.Single(m => m.IsSelected).Mode);
        Assert.False(library.Reveal.IsRevealed("v31201"));
    }

    [Fact]
    public async Task Uma_troca_que_o_proprio_app_gravou_nao_pede_nova_leitura()
    {
        var (_, library) = await ShellAsync();
        var source = new FakeImages(() => File.Load().Privacy);
        library.FollowPrivacy(source);
        library.ChooseAdultMode(AdultMode.Hide);
        var loading = library.Loading;

        source.Raise();

        Assert.Same(loading, library.Loading);
    }

    [Fact]
    public async Task A_CLI_que_troca_o_modo_por_fora_vale_quando_a_Biblioteca_le_o_arquivo_de_novo()
    {
        var (shell, library) = await ShellAsync();
        File.Update(l => l.Privacy = l.Privacy with { Adult = AdultMode.Hide });

        await library.ReloadPrivacyAsync();

        Assert.Equal(AdultMode.Hide, library.AdultMode);
        Assert.Equal(AdultMode.Hide, shell.Settings.Privacy!.Modes.Single(m => m.IsSelected).Mode);
        Assert.Equal("1 adulto escondido", library.HiddenAdultText);
    }

    [Fact]
    public async Task O_mostrar_do_jogo_vale_ate_sair_da_Biblioteca_e_da_Ficha()
    {
        var (shell, library) = await ShellAsync();
        var card = library.Cards.Single(c => c.Title == "Kuroneko Archive");

        library.RevealGameCommand.Execute(card);
        Assert.True(library.Reveal.IsRevealed(card.Game.VndbId));

        shell.SelectedNav = shell.Nav.Single(n => n.Key == NavKey.Start);
        Assert.True(library.Reveal.IsRevealed(card.Game.VndbId));

        shell.SelectedNav = shell.Nav.Single(n => n.Key == NavKey.Settings);
        Assert.False(library.Reveal.IsRevealed(card.Game.VndbId));
        Assert.True(library.Cards.Single(c => c.Title == "Kuroneko Archive").IsBlurred);
    }

    [Fact]
    public async Task A_capa_adulta_de_um_candidato_borra_com_o_mostrar_e_o_mostrar_a_revela()
    {
        var korin = LibrarySamples.Games().Single(g => g.Title == "Kōrin no Mori");
        var adultCover = korin.Candidates[0] with { Image = new VNForge.Core.Vndb.VndbImageRef("https://t.vndb.org/cv/korin_no_mori.jpg", 2) };
        var games = LibrarySamples.Games().Select(g => g.Title == "Kōrin no Mori" ? g with { Candidates = [adultCover, .. g.Candidates.Skip(1)] } : g).ToList();
        var (_, library) = await ShellAsync(games);
        var card = library.Cards.Single(c => c.Title == "Kōrin no Mori");

        Assert.True(card.IsAdult);
        Assert.True(card.IsBlurred);

        library.RevealGameCommand.Execute(card);

        var revealed = library.Cards.Single(c => c.Title == "Kōrin no Mori");
        Assert.False(revealed.IsBlurred);
        Assert.True(revealed.CanConceal);
    }

    [Fact]
    public void O_registro_avisa_so_quando_algo_muda_e_nao_revela_um_jogo_sem_id()
    {
        var reveal = new AdultReveal();
        var changes = 0;
        reveal.Changed += (_, _) => changes++;

        reveal.Reveal(null);
        reveal.Reveal("v1");
        reveal.Reveal("v1");
        reveal.Conceal("v2");
        reveal.Conceal("v1");
        reveal.Clear();

        Assert.Equal(2, changes);
        Assert.False(reveal.IsRevealed("v1"));
    }

    [Fact]
    public async Task O_padrao_de_quem_nunca_escolheu_e_desfocar_e_nao_mostra_o_conteudo_adulto()
    {
        var (_, library) = await ShellAsync();

        Assert.Equal(AdultMode.Blur, new LibraryPrivacy().Adult);
        Assert.Equal(AdultMode.Blur, library.AdultMode);
        Assert.False(File.Load().Privacy.ShowAdult);
        Assert.True(library.Cards.Single(c => c.Title == "Kuroneko Archive").IsBlurred);
        Assert.Equal(string.Empty, library.HiddenAdultText);
    }

    [Fact]
    public async Task Sem_o_arquivo_gravavel_o_modo_nao_muda_e_o_aviso_aparece()
    {
        var (_, library) = await ShellAsync();
        var path = Path.Combine(_config.Path, LibraryFile.FileName);
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        library.ChooseAdultMode(AdultMode.Hide);

        Assert.Equal(AdultMode.Blur, library.AdultMode);
        Assert.False(string.IsNullOrEmpty(library.ErrorMessage));
    }
}
