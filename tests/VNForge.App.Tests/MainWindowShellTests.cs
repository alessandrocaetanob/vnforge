using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O shell da #544: Biblioteca no trilho, o jogo aberto com o seu cabeçalho e as abas Ficha, Tradução e Jogar; a tradução de antes vive numa segunda linha.</summary>
[Trait("Risco", "R6")]
[Trait("Risco", "R7")]
public sealed class MainWindowShellTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static MainWindowViewModel Create(TempFolder temp, string root) =>
        new(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)));

    [Fact]
    public async Task Abrir_um_projeto_que_ja_existe_da_biblioteca_vazia_leva_a_Ficha()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", [TranslationStatus.Machine]);
        var vm = Create(temp, root);

        vm.Library.OpenExistingProjectCommand.Execute(null);
        await vm.ProjectOpening;

        Assert.Equal("Minha VN", vm.ProjectTitle);
        Assert.Same(vm.Sheet, vm.CurrentPage);
    }

    [Fact]
    public async Task O_mesmo_erro_ao_abrir_o_projeto_do_jogo_aparece_de_novo_na_segunda_vez()
    {
        using var temp = new TempFolder();
        var missing = Path.Combine(temp.Path, "sumiu");
        var game = LibrarySamples.Games().First(g => g.ProjectPath is null && g.Engine.Translatable) with { ProjectPath = missing };
        var file = LibrarySamples.Save(temp.SubFolder("biblioteca"), LibrarySamples.Library([game]));
        var library = new LibraryViewModel(file, new FakeFolderPicker(null), action => action());
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now)), library: library);
        await library.LoadAsync();

        library.OpenGameCommand.Execute(library.Cards[0]);
        await vm.ProjectOpening;
        var first = library.ErrorMessage;
        library.ReportProblem(string.Empty);
        library.OpenGameCommand.Execute(library.Cards[0]);
        await vm.ProjectOpening;

        Assert.False(string.IsNullOrEmpty(first));
        Assert.Equal(first, library.ErrorMessage);
    }

    [Fact]
    public async Task Abrir_um_projeto_que_ja_existe_com_a_pasta_ilegivel_vira_aviso_na_Biblioteca_e_nao_excecao_solta()
    {
        using var temp = new TempFolder();
        var library = new LibraryViewModel(null, new FakeFolderPicker(null), action => action());
        var picker = new ThrowingPicker(new IOException("O disco não respondeu."));
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), picker, new FixedClock(Now)), library: library);

        library.OpenExistingProjectCommand.Execute(null);
        await vm.ProjectOpening;

        Assert.True(vm.ProjectOpening.IsCompletedSuccessfully);
        Assert.Contains("O disco não respondeu.", library.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Abrir_o_jogo_da_biblioteca_sem_projeto_leva_a_Ficha_e_traduz_a_ficha_sem_projeto()
    {
        using var temp = new TempFolder();
        var config = temp.SubFolder("config");
        await GameSheetSamples.SeedAsync(config, translate: false, ct: TestContext.Current.CancellationToken);
        var game = LibrarySamples.Games().First(g => g.ProjectPath is null && g.Engine.Translatable) with { VndbId = GameSheetSamples.Id };
        var file = LibrarySamples.Save(temp.SubFolder("biblioteca"), LibrarySamples.Library([game]));
        var library = new LibraryViewModel(file, new FakeFolderPicker(null), action => action());
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(config), new FakeFolderPicker(null), new FixedClock(Now)), library: library);
        vm.Sheet.ConfigDirectory = config;
        vm.Sheet.TranslatorFactory = GameSheetSamples.Translator;
        await library.LoadAsync();

        library.OpenGameCommand.Execute(library.Cards[0]);
        await vm.Sheet.Loading;

        Assert.False(vm.HasProject);
        Assert.True(vm.InProjectlessGame);
        Assert.Same(vm.Sheet, vm.CurrentPage);
        Assert.Equal("Ainda em inglês", vm.Sheet.StatusTitle);

        vm.Sheet.SpoilerLevel = 1;
        await vm.Sheet.TranslateCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.Sheet.SpoilerLevel);
        Assert.True(vm.Sheet.PrimaryIsReview);

        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Start);

        Assert.False(vm.InProjectlessGame);
        Assert.False(vm.Nav.Single(n => n.Key == NavKey.Sheet).IsEnabled);
    }

    private sealed class ThrowingPicker(Exception error) : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromException<string?>(error);
    }
}
