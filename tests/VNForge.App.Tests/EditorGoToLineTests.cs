using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;
using static VNForge.App.Tests.EditorFixtures;

namespace VNForge.App.Tests;

public class EditorGoToLineTests
{
    private static ProjectLines DoisScripts() => Lines(
        new QaReport([new QaIssue("regra", QaSeverity.Warning, "yst00001:000002", "yst00001", "problema")], [], ["regra"]),
        ("yst00002", [Unit("yst00002", 1, "Ken", TranslationStatus.Approved), Unit("yst00002", 2, "Ana", TranslationStatus.Machine)]),
        ("yst00001",
        [
            Unit("yst00001", 1, "Ana", TranslationStatus.Untranslated),
            Unit("yst00001", 2, "Ana", TranslationStatus.Machine),
            Unit("yst00001", 3, null, TranslationStatus.Untranslated),
        ]));

    private static async Task<EditorViewModel> Carregado()
    {
        var vm = new EditorViewModel(_ => DoisScripts());
        vm.Project = Summary();
        await vm.Loading;
        return vm;
    }

    [Fact]
    public async Task Ir_para_a_linha_de_outro_script_troca_o_script_e_seleciona_a_linha()
    {
        var vm = await Carregado();

        var ok = await vm.GoToLineAsync("yst00002", "yst00002:000002");

        Assert.True(ok);
        Assert.Equal("yst00002", vm.SelectedScript?.Key);
        Assert.Equal("yst00002:000002", vm.SelectedLine?.Id);
    }

    [Fact]
    public async Task Ir_para_a_linha_fora_do_filtro_limpa_os_filtros()
    {
        var vm = await Carregado();
        vm.SelectFilterCommand.Execute(vm.Filters.Single(f => f.Filter == LineStatusFilter.Machine));
        Assert.DoesNotContain(vm.Lines, l => l.Id == "yst00001:000001");

        var ok = await vm.GoToLineAsync("yst00001", "yst00001:000001");

        Assert.True(ok);
        Assert.Equal("yst00001:000001", vm.SelectedLine?.Id);
        Assert.True(vm.Filters.Single(f => f.Filter == LineStatusFilter.All).IsSelected);
    }

    [Fact]
    public async Task Ir_para_a_linha_fora_do_filtro_de_personagem_volta_para_todos()
    {
        var vm = await Carregado();
        vm.SelectedSpeaker = vm.Speakers.Single(s => s.Value == "Ana");
        Assert.DoesNotContain(vm.Lines, l => l.Id == "yst00001:000003");

        var ok = await vm.GoToLineAsync("yst00001", "yst00001:000003");

        Assert.True(ok);
        Assert.Equal("yst00001:000003", vm.SelectedLine?.Id);
        Assert.Null(vm.SelectedSpeaker?.Value);
    }

    [Fact]
    public async Task Linha_ou_script_inexistente_nao_muda_a_selecao()
    {
        var vm = await Carregado();
        var before = vm.SelectedLine;

        Assert.False(await vm.GoToLineAsync("yst00001", "yst00001:000099"));
        Assert.False(await vm.GoToLineAsync("yst99999", "yst99999:000001"));

        Assert.Same(before, vm.SelectedLine);
        Assert.Equal("yst00001", vm.SelectedScript?.Key);
    }

    [Fact]
    public async Task Reload_volta_ao_script_e_a_linha_que_estavam_selecionados()
    {
        var vm = await Carregado();
        await vm.GoToLineAsync("yst00002", "yst00002:000002");

        vm.Reload();
        await vm.Loading;

        Assert.Equal("yst00002", vm.SelectedScript?.Key);
        Assert.Equal("yst00002:000002", vm.SelectedLine?.Id);
    }

    [Fact]
    public async Task Reload_sem_linha_selecionada_ou_com_a_linha_que_sumiu_fica_no_comeco()
    {
        var calls = 0;
        var vm = new EditorViewModel(_ => ++calls == 1 ? DoisScripts() : Lines(("yst00001", [Unit("yst00001", 1, "Ana", TranslationStatus.Machine)])));
        vm.Project = Summary();
        await vm.Loading;
        await vm.GoToLineAsync("yst00001", "yst00001:000003");

        vm.Reload();
        await vm.Loading;

        Assert.Equal("yst00001", vm.SelectedScript?.Key);
        Assert.Equal("yst00001:000001", vm.SelectedLine?.Id);

        var empty = new EditorViewModel(_ => DoisScripts());
        empty.Reload();
        await empty.Loading;
        Assert.Empty(empty.Scripts);
    }

    [Fact]
    public async Task Ir_para_a_linha_espera_a_carga_em_andamento()
    {
        var vm = await Carregado();
        vm.Reload();

        var ok = await vm.GoToLineAsync("yst00002", "yst00002:000001");

        Assert.True(ok);
        Assert.Equal("yst00002:000001", vm.SelectedLine?.Id);
    }
}
