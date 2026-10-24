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
    public void Sem_texto_lista_comandos_e_telas_e_destaca_o_primeiro()
    {
        var palette = Palette([]);

        palette.Open();

        Assert.True(palette.IsOpen);
        Assert.Equal(["Novo build", "Traduzir pendentes", "Conferir", "Editor"], palette.Results.Select(r => r.Title));
        Assert.Equal(0, palette.SelectedIndex);
        Assert.True(palette.Results[0].ShowGroup);
        Assert.False(palette.Results[1].ShowGroup);
        Assert.True(palette.Results[2].ShowGroup);
    }

    [Fact]
    public void Acha_script_por_id_e_tela_sem_acento_nem_maiusculas()
    {
        var palette = Palette([]);
        palette.Open();

        palette.Query = "ST0001";
        Assert.Equal(["yst00014"], palette.Results.Select(r => r.Title));

        palette.Query = "CONFERIR";
        Assert.Equal(["Conferir"], palette.Results.Select(r => r.Title));

        palette.Query = "pendentes";
        Assert.Equal(["Traduzir pendentes"], palette.Results.Select(r => r.Title));
    }

    [Fact]
    public void Resultados_que_comecam_pelo_texto_vem_antes()
    {
        var palette = new CommandPaletteViewModel(
            () =>
            [
                new PaletteEntry(PaletteGroup.Command, "Rebuild", "", () => { }),
                new PaletteEntry(PaletteGroup.Command, "Novo build", "", () => { }),
                new PaletteEntry(PaletteGroup.Command, "Build rápido", "", () => { }),
            ],
            (_, _) => []);
        palette.Open();

        palette.Query = "build";

        Assert.Equal(["Build rápido", "Rebuild", "Novo build"], palette.Results.Select(r => r.Title));
    }

    [Fact]
    public void Linhas_aparecem_depois_dos_nomes()
    {
        var palette = Palette([]);
        palette.Open();

        palette.Query = "olá";

        Assert.Equal([PaletteGroup.Line], palette.Results.Select(r => r.Group).Distinct());
        Assert.Equal("Olá, mundo", palette.SelectedEntry?.Title);
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
    public void Enter_executa_o_destacado_e_fecha()
    {
        var executed = new List<string>();
        var palette = Palette(executed);
        palette.Open();
        palette.SelectNext();

        palette.ExecuteSelected();

        Assert.Equal(["traduzir"], executed);
        Assert.False(palette.IsOpen);
        Assert.Equal(string.Empty, palette.Query);
    }

    [Fact]
    public void Esc_fecha_sem_executar_e_sem_resultado_o_Enter_so_fecha()
    {
        var executed = new List<string>();
        var palette = Palette(executed);
        palette.Open();
        palette.Close();
        Assert.False(palette.IsOpen);

        palette.Open();
        palette.Query = "zzz";
        Assert.False(palette.HasResults);
        Assert.Equal("Nada encontrado para \"zzz\".", palette.EmptyMessage);
        palette.ExecuteSelected();

        Assert.Empty(executed);
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public void Sem_projeto_a_paleta_so_oferece_as_telas_habilitadas()
    {
        using var temp = new TempFolder();
        var vm = CreateMain(temp, temp.SubFolder("vn"));

        vm.OpenPaletteCommand.Execute(null);

        Assert.True(vm.Palette.IsOpen);
        Assert.Equal(["Início", "Ajustes"], vm.Palette.Results.Select(r => r.Title));
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
    public async Task Acha_script_por_id_e_lista_os_comandos_do_projeto()
    {
        using var temp = new TempFolder();
        var vm = await Opened(temp, null, TranslationStatus.Untranslated);
        vm.Palette.Open();

        vm.Palette.Query = "yst0000";
        Assert.Equal("yst00001", Assert.Single(vm.Palette.Results).Title);

        vm.Palette.Query = "";
        var titles = vm.Palette.Results.Select(r => r.Title).ToList();
        Assert.Contains("Traduzir pendentes", titles);
        Assert.Contains("Novo build", titles);
        Assert.Contains("Abrir pasta do projeto", titles);
        Assert.Contains("Painel", titles);
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
    public async Task Comando_traduzir_pendentes_abre_o_Editor_com_o_filtro_Pendentes()
    {
        using var temp = new TempFolder();
        var vm = await Opened(temp, null, TranslationStatus.Approved, TranslationStatus.Untranslated, TranslationStatus.Untranslated);
        vm.Palette.Open();
        vm.Palette.Query = "traduzir";

        vm.Palette.ExecuteSelected();

        Assert.Equal(NavKey.Editor, vm.SelectedNav?.Key);
        Assert.Equal(2, vm.Editor.Lines.Count);
        Assert.True(vm.Editor.Filters.Single(f => f.Filter == LineStatusFilter.Pending).IsSelected);
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
