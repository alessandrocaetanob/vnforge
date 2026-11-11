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
}
