using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class MainWindowConferenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static async Task<MainWindowViewModel> OpenedWithProblem(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Approved, TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        var units = folder.LoadText("yst00001").ToList();
        units[1] = units[1] with { Translation = "duas  palavras" };
        folder.SaveText("yst00001", units);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)));
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        return vm;
    }

    [Fact]
    public async Task Conferencia_fica_desabilitada_ate_abrir_um_projeto_e_depois_lista_os_problemas()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)));
        var item = vm.Nav.Single(n => n.Key == NavKey.Conference);
        Assert.False(item.IsEnabled);
        Assert.Equal("Conferir", item.Label);

        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        Assert.True(item.IsEnabled);
        vm.SelectedNav = item;
        await vm.ConferenceLoading;

        Assert.Same(vm.Conference, vm.CurrentPage);
        Assert.Equal("Minha VN", vm.Conference.Project?.Name);
        Assert.Equal("Nenhum problema encontrado.", vm.Conference.EmptyMessage);
    }

    [Fact]
    public async Task Trocar_o_ajuste_pela_Conferencia_faz_Ajustes_reler_o_perfil()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Translated);
        var folder = ProjectFolder.Open(root);
        folder.SaveConfig(folder.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        folder.SaveFormatting(VNForge.Core.Formatting.FormattingProfile.Default with { Typography = VNForge.Core.Formatting.FormattingProfile.Default.Typography with { Ellipsis = VNForge.Core.Formatting.EllipsisStyle.Character } });
        folder.SaveText("yst00001", [new TextUnit("yst00001:000000", TextUnitKind.Dialogue, null, "原文", "Espera... vai", TranslationStatus.Translated)]);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)));
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        Assert.Equal(VNForge.Core.Formatting.EllipsisStyle.Character, vm.Settings.SelectedEllipsis?.Value);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
        await vm.ConferenceLoading;

        await vm.Conference.ApplySettingCommand.ExecuteAsync(vm.Conference.SettingCards.Single());
        await vm.Settings.EstimateLoading;

        Assert.Equal(VNForge.Core.Formatting.EllipsisStyle.Dots, vm.Settings.SelectedEllipsis?.Value);
        Assert.False(vm.Settings.HasEncodingConflicts);
        Assert.Equal(VNForge.Core.Formatting.EllipsisStyle.Dots, folder.LoadFormatting().Typography.Ellipsis);
    }

    [Fact]
    public async Task Abrir_a_linha_leva_ao_Editor_e_seleciona_a_linha_do_problema()
    {
        using var temp = new TempFolder();
        var vm = await OpenedWithProblem(temp);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
        await vm.ConferenceLoading;
        var row = vm.Conference.Rows.OfType<ConferenceIssueRowViewModel>().Single();

        vm.Conference.OpenLineCommand.Execute(row);
        await vm.LineNavigation;

        Assert.Same(vm.Editor, vm.CurrentPage);
        Assert.Equal(NavKey.Editor, vm.SelectedNav?.Key);
        Assert.Equal("yst00001:000001", vm.Editor.SelectedLine?.Id);
    }

    [Fact]
    public async Task Entrar_na_Conferencia_grava_antes_a_edicao_pendente_do_Editor()
    {
        using var temp = new TempFolder();
        var vm = await OpenedWithProblem(temp);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        await vm.Editor.Loading;
        vm.Editor.SelectedLine = vm.Editor.Lines[2];
        vm.Editor.Detail!.Translation = "editada  agora";

        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
        await vm.ConferenceLoading;

        Assert.Same(vm.Conference, vm.CurrentPage);
        Assert.Contains(vm.Conference.Rows.OfType<ConferenceIssueRowViewModel>(), r => r.UnitId == "yst00001:000002" && r.RuleId == "espaco-duplo");
    }

    [Fact]
    public async Task Corrigir_tudo_com_edicao_pendente_no_Editor_grava_a_edicao_antes_e_o_Editor_volta_na_mesma_linha()
    {
        using var temp = new TempFolder();
        var vm = await OpenedWithProblem(temp);
        await PendingEditThen(vm, () => vm.Conference.FixAllCommand.ExecuteAsync(null));

        var saved = ProjectFolder.Open(vm.CurrentProject!.Root).LoadText("yst00001");
        Assert.Equal("pendente aqui", saved[2].Translation);
        Assert.Equal("duas palavras", saved[1].Translation);

        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        await vm.Editor.Loading;
        Assert.Equal("yst00001:000002", vm.Editor.SelectedLine?.Id);
        Assert.Equal("pendente aqui", vm.Editor.Detail?.Translation);
    }

    [Fact]
    public async Task Aplicar_correcao_com_edicao_pendente_no_Editor_grava_a_edicao_antes()
    {
        using var temp = new TempFolder();
        var vm = await OpenedWithProblem(temp);
        await PendingEditThen(vm, () =>
        {
            var row = vm.Conference.Rows.Single(r => r.RuleId == "espaco-duplo" && r.UnitId == "yst00001:000002");
            return vm.Conference.ApplyFixCommand.ExecuteAsync(row);
        });

        var saved = ProjectFolder.Open(vm.CurrentProject!.Root).LoadText("yst00001");
        Assert.Equal("pendente aqui", saved[2].Translation);
        // A correção é só da linha pedida: a edição pendente foi gravada antes e então corrigida; a outra linha fica como estava.
        Assert.Equal("duas  palavras", saved[1].Translation);
    }

    private static async Task PendingEditThen(MainWindowViewModel vm, Func<Task> fix)
    {
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        await vm.Editor.Loading;
        vm.Editor.SelectedLine = vm.Editor.Lines[2];
        vm.Editor.Detail!.Translation = "pendente  aqui";

        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
        await vm.ConferenceLoading;
        await fix();
    }

    [Fact]
    public async Task Abrir_linha_que_sumiu_avisa_no_Editor_e_um_salto_certo_limpa_o_aviso()
    {
        using var temp = new TempFolder();
        var vm = await OpenedWithProblem(temp);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
        await vm.ConferenceLoading;

        vm.Conference.OpenLineCommand.Execute(new ConferenceIssueRowViewModel(new QaIssue("espaco-duplo", QaSeverity.Warning, "yst00001:000099", "yst00001", "x")));
        await vm.LineNavigation;

        Assert.Equal(NavKey.Editor, vm.SelectedNav?.Key);
        Assert.Equal("A linha yst00001:000099 não existe mais neste projeto. Confira de novo em Conferir a tradução.", vm.Editor.ErrorMessage);

        vm.Conference.OpenLineCommand.Execute(vm.Conference.Rows.OfType<ConferenceIssueRowViewModel>().Single());
        await vm.LineNavigation;

        Assert.Null(vm.Editor.ErrorMessage);
        Assert.Equal("yst00001:000001", vm.Editor.SelectedLine?.Id);
    }

    [Fact]
    public async Task Gravacao_pendente_que_falha_impede_de_entrar_na_Conferencia()
    {
        using var temp = new TempFolder();
        var vm = await OpenedWithProblem(temp);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        await vm.Editor.Loading;
        vm.Editor.Detail!.Translation = "não vai gravar";
        File.Delete(ProjectFolder.Open(vm.CurrentProject!.Root).TextPath("yst00001"));

        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);

        Assert.Same(vm.Editor, vm.CurrentPage);
        Assert.Equal(NavKey.Editor, vm.SelectedNav?.Key);
        Assert.NotNull(vm.Editor.Detail?.ErrorMessage);
    }

    [Fact]
    public async Task Marcar_como_ok_na_Conferencia_faz_o_Editor_reler_ao_voltar()
    {
        using var temp = new TempFolder();
        var vm = await OpenedWithProblem(temp);
        await vm.Editor.Loading;
        Assert.Single(vm.Editor.Lines, l => l.HasWarning);

        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
        await vm.ConferenceLoading;
        await vm.Conference.MarkOkCommand.ExecuteAsync(vm.Conference.Rows.OfType<ConferenceIssueRowViewModel>().Single());
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        await vm.Editor.Loading;

        Assert.DoesNotContain(vm.Editor.Lines, l => l.HasWarning);
    }
}
