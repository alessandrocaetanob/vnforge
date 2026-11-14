using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Search;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

[Trait("Risco", "R2")]
public class SearchReplaceViewModelTests
{
    private static (SearchReplaceViewModel Vm, ProjectFolder Folder) Abrir(TempFolder temp, params TranslationStatus[] statuses)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", statuses);
        var vm = new SearchReplaceViewModel { Project = ProjectSummaryReader.Read(root) };
        return (vm, ProjectFolder.Open(root));
    }

    [Fact]
    public async Task Aplicar_so_vale_depois_da_previa_e_grava_o_que_ela_mostrou()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Abrir(temp, TranslationStatus.Approved, TranslationStatus.Translated);
        vm.Pattern = "tradução 1";
        vm.Replacement = "TROCADA";
        Assert.False(vm.ApplyCommand.CanExecute(null));
        ReplaceResult? aplicado = null;
        vm.Applied += (_, r) => aplicado = r;

        await vm.PreviewCommand.ExecuteAsync(null);
        await vm.ApplyCommand.ExecuteAsync(null);

        var linhas = folder.LoadText("yst00001");
        Assert.Equal("tradução 0", linhas[0].Translation);
        Assert.Equal("TROCADA", linhas[1].Translation);
        Assert.Equal(1, aplicado?.Applied);
        Assert.Equal("1 linha gravada.", vm.Summary);
        Assert.False(vm.HasPreview);
        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task Aplicar_que_falha_no_segundo_script_avisa_e_manda_o_Editor_reler()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Abrir(temp, TranslationStatus.Translated);
        folder.SaveText("yst00002", [new TextUnit("yst00002:000000", TextUnitKind.Dialogue, null, "原文", "tradução b", TranslationStatus.Translated)]);
        vm.Pattern = "tradução";
        vm.Replacement = "nova";
        await vm.PreviewCommand.ExecuteAsync(null);
        File.Delete(folder.TextPath("yst00002"));
        var avisos = new List<ReplaceResult>();
        vm.Applied += (_, r) => avisos.Add(r);

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Contains("Alguns scripts podem já ter sido gravados", vm.ErrorMessage);
        Assert.Equal(0, Assert.Single(avisos).Applied);
        Assert.Equal("nova 0", folder.LoadText("yst00001")[0].Translation);
        Assert.False(vm.HasPreview);
    }

}
