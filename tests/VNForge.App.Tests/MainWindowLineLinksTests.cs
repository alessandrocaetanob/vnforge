using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O id de linha citado num erro do build ou da Conferência vira link: clicar nele abre o Editor naquela linha.</summary>
public class MainWindowLineLinksTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 20, 0, 0, TimeSpan.Zero);

    private const string BuildError = "Não foi possível reconstruir o script 'yst00001.ybn' com o text/yst00001.json: A tradução da unidade 'yst00001:000002' não pode ser gravada: O texto tem 1 caractere(s) que a estratégia cp1252 não grava.";

    private static async Task<MainWindowViewModel> Opened(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Approved, TranslationStatus.Approved);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)));
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        return vm;
    }

    private static LineRequest FirstIdOf(string message)
    {
        var mention = LineIds.Find(message)[0];
        return new LineRequest(mention.ScriptKey, mention.UnitId);
    }

    [Fact]
    public async Task Clicar_no_id_do_erro_do_build_abre_o_Editor_na_linha()
    {
        using var temp = new TempFolder();
        var vm = await Opened(temp);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Build);
        Assert.NotEqual("yst00001:000002", vm.Editor.SelectedLine?.Id);

        vm.Build.OpenLineCommand.Execute(FirstIdOf(BuildError));
        await vm.LineNavigation;

        Assert.Same(vm.Editor, vm.CurrentPage);
        Assert.Equal(NavKey.Editor, vm.SelectedNav?.Key);
        Assert.Equal("yst00001:000002", vm.Editor.SelectedLine?.Id);
    }

    [Fact]
    public async Task Clicar_no_id_do_erro_da_Conferencia_abre_o_Editor_na_linha()
    {
        using var temp = new TempFolder();
        var vm = await Opened(temp);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
        await vm.ConferenceLoading;

        vm.Conference.OpenLineByIdCommand.Execute(FirstIdOf(BuildError));
        await vm.LineNavigation;

        Assert.Same(vm.Editor, vm.CurrentPage);
        Assert.Equal("yst00001:000002", vm.Editor.SelectedLine?.Id);
    }

    [Fact]
    public async Task Id_de_uma_linha_que_nao_existe_mais_avisa_no_Editor()
    {
        using var temp = new TempFolder();
        var vm = await Opened(temp);

        vm.Build.OpenLineCommand.Execute(new LineRequest("yst00001", "yst00001:999999"));
        await vm.LineNavigation;

        Assert.Same(vm.Editor, vm.CurrentPage);
        Assert.Contains("yst00001:999999", vm.Editor.ErrorMessage);
    }

    [Fact]
    public async Task Busca_da_paleta_por_id_completo_acha_so_a_linha_e_por_cena_acha_a_cena()
    {
        using var temp = new TempFolder();
        var vm = await Opened(temp);

        Assert.Equal(["yst00001:000002"], vm.Editor.FindLines("yst00001:000002", 10).Select(l => l.Unit.Id));
        Assert.Equal(vm.Editor.Lines.Count, vm.Editor.FindLines("yst00001", 100).Count);
    }
}
