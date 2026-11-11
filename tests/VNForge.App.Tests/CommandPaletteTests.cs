using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class CommandPaletteTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static CommandPaletteViewModel Palette(List<string> executed) =>
        new(
            () =>
            [
                new PaletteEntry(PaletteGroup.Command, "Novo build", "", () => executed.Add("build")),
                new PaletteEntry(PaletteGroup.Command, "Traduzir pendentes", "", () => executed.Add("traduzir")),
                new PaletteEntry(PaletteGroup.Screen, "Conferir", "Tela", () => executed.Add("conferir")),
                new PaletteEntry(PaletteGroup.Screen, "Editor", "Tela", () => executed.Add("editor")),
                new PaletteEntry(PaletteGroup.Script, "yst00014", "", () => executed.Add("script")),
            ],
            (text, _) => text == "olá" ? [new PaletteEntry(PaletteGroup.Line, "Olá, mundo", "yst00001 · linha 1", () => executed.Add("linha"))] : []);

    private static MainWindowViewModel CreateMain(TempFolder temp, string root, List<string>? opened = null) =>
        new(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)), openFolder: opened is null ? null : opened.Add);

    private static async Task<MainWindowViewModel> Opened(TempFolder temp, List<string>? opened = null, params TranslationStatus[] statuses)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", statuses);
        var vm = CreateMain(temp, root, opened);
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        return vm;
    }

    [Fact]
    public void Setas_navegam_e_dao_a_volta_nas_pontas()
    {
        var palette = Palette([]);
        palette.Open();

        palette.SelectNext();
        Assert.Equal(1, palette.SelectedIndex);

        palette.SelectPrevious();
        palette.SelectPrevious();
        Assert.Equal(palette.Results.Count - 1, palette.SelectedIndex);

        palette.SelectNext();
        Assert.Equal(0, palette.SelectedIndex);
    }

    [Fact]
    public async Task Com_projeto_acha_linha_pelo_texto_e_leva_ate_ela_no_Editor()
    {
        using var temp = new TempFolder();
        var vm = await Opened(temp, null, TranslationStatus.Translated, TranslationStatus.Translated, TranslationStatus.Translated);

        vm.Palette.Open();
        vm.Palette.Query = "tradução 2";
        var line = Assert.Single(vm.Palette.Results);
        Assert.Equal(PaletteGroup.Line, line.Group);
        Assert.Equal("yst00001 · linha 3", line.Subtitle);
        vm.Palette.ExecuteSelected();
        await vm.LineNavigation;

        Assert.Equal(NavKey.Editor, vm.SelectedNav?.Key);
        Assert.Equal("yst00001:000002", vm.Editor.SelectedLine?.Id);
    }

    [Fact]
    public async Task Comando_abrir_pasta_entrega_a_raiz_do_projeto()
    {
        using var temp = new TempFolder();
        var opened = new List<string>();
        var vm = await Opened(temp, opened, TranslationStatus.Untranslated);
        vm.Palette.Open();
        vm.Palette.Query = "abrir pasta";

        vm.Palette.ExecuteSelected();

        Assert.Equal([vm.CurrentProject!.Root], opened);
    }

    [Fact]
    public async Task Comando_novo_build_abre_a_pagina_Build()
    {
        using var temp = new TempFolder();
        var vm = await Opened(temp, null, TranslationStatus.Approved);
        vm.Palette.Open();
        vm.Palette.Query = "novo build";

        vm.Palette.ExecuteSelected();

        Assert.Equal(NavKey.Build, vm.SelectedNav?.Key);
    }

    [Fact]
    public async Task Script_da_paleta_seleciona_o_script_no_Editor()
    {
        using var temp = new TempFolder();
        var vm = await Opened(temp, null, TranslationStatus.Approved);
        vm.Palette.Open();
        vm.Palette.Query = "yst00001";

        vm.Palette.ExecuteSelected();

        Assert.Equal(NavKey.Editor, vm.SelectedNav?.Key);
        Assert.Equal("yst00001", vm.Editor.SelectedScript?.Key);
    }
}
