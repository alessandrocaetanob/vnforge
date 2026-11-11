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
}
