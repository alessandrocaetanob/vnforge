using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Glossário, Ajustes e o comando "Reler o projeto" mantêm o Editor, o Painel e a Conferência em dia sem reabrir a app (issue #372).</summary>
public class MainWindowRecheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(MainWindowViewModel Vm, ProjectFolder Folder)> Opened(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001",
        [
            new TextUnit("yst00001:000000", TextUnitKind.Dialogue, null, "そう言いながら亀島は…", "Ele olhou para Kamejima.", TranslationStatus.Approved),
            new TextUnit("yst00001:000001", TextUnitKind.Dialogue, null, "おはよう", "duas  palavras", TranslationStatus.Approved),
        ]);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)));
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        return (vm, folder);
    }

    private static PaletteEntry Rereader(MainWindowViewModel vm)
    {
        vm.Palette.Open();
        vm.Palette.Query = "Reler";
        return Assert.Single(vm.Palette.Results, r => r.Title == "Reler o projeto");
    }

    [Fact]
    public async Task Salvar_o_personagem_no_Glossario_acende_o_aviso_do_Editor()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Opened(temp);
        Assert.False(vm.Editor.Lines[0].HasWarning);

        vm.Glossary.AddCharacterCommand.Execute(null);
        var row = vm.Glossary.Characters[0];
        row.Source = "亀島";
        row.Target = "Kameshima";
        vm.Glossary.SaveCommand.Execute(null);
        await vm.Editor.Rechecking;

        Assert.Contains(vm.Editor.Lines[0].Entry.Issues, i => i.RuleId == "personagem-diferente");
        Assert.True(vm.Editor.Lines[0].HasWarning);
    }

    [Fact]
    public async Task O_comando_Reler_o_projeto_esta_na_paleta_e_atualiza_Editor_e_Conferencia()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Opened(temp);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
        await vm.ConferenceLoading;
        Assert.Equal(1, vm.Conference.IssueCount);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        Assert.Equal(1, vm.Editor.IssueCount);

        var units = folder.LoadText("yst00001").ToList();
        units[1] = units[1] with { Translation = "uma palavra" };
        folder.SaveText("yst00001", units);
        Rereader(vm).Execute();
        await vm.ProjectReloading;
        await vm.Editor.Loading;

        Assert.Equal("uma palavra", vm.Editor.Lines[1].TranslationLabel);
        Assert.Equal(0, vm.Editor.IssueCount);
        Assert.Equal(0, vm.Conference.IssueCount);
    }

    [Fact]
    public async Task O_comando_Reler_o_projeto_grava_antes_a_edicao_pendente_do_Editor()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Opened(temp);
        vm.Editor.SelectedLine = vm.Editor.Lines[1];
        vm.Editor.Detail!.Translation = "uma palavra";

        Rereader(vm).Execute();
        await vm.ProjectReloading;
        await vm.Editor.Loading;

        Assert.Equal("uma palavra", folder.LoadText("yst00001")[1].Translation);
        Assert.Equal("uma palavra", vm.Editor.Lines[1].TranslationLabel);
    }

    [Fact]
    public async Task Sair_do_Editor_para_o_Painel_grava_antes_a_edicao_pendente()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Opened(temp);
        vm.Editor.SelectedLine = vm.Editor.Lines[1];
        vm.Editor.Detail!.Translation = "uma palavra";

        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Dashboard);

        Assert.Equal("uma palavra", folder.LoadText("yst00001")[1].Translation);
    }

    [Fact]
    public void O_comando_Reler_o_projeto_nao_aparece_sem_projeto()
    {
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(Path.Combine(Path.GetTempPath(), "VNForge-sem-recentes")), new FakeFolderPicker(null), new FixedClock(Now)));

        vm.Palette.Open();
        vm.Palette.Query = "Reler";

        Assert.DoesNotContain(vm.Palette.Results, r => r.Title == "Reler o projeto");
    }
}
