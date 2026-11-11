using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.Core.Demo;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;
using static VNForge.App.Tests.EditorFixtures;

namespace VNForge.App.Tests;

/// <summary>"Mudou desde o último teste": a lista de cenas na página Testar e no Painel, e o filtro do Editor que o "Abrir" liga.</summary>
public sealed class ChangedSinceTestTests
{
    private static readonly DateTimeOffset TestAt = new(2026, 10, 22, 23, 16, 0, TimeSpan.Zero);

    private static TextUnit Line(string key, int n, string translation) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, "Ana", $"原文{n}", translation, TranslationStatus.Translated);

    /// <summary>Duas cenas traduzidas, testadas no jogo, e depois três falas editadas: duas na yst00001 e uma na yst00003.</summary>
    private static (Dictionary<string, IReadOnlyList<TextUnit>> Now, GameLastTest Test) Scenario()
    {
        Dictionary<string, IReadOnlyList<TextUnit>> before = new()
        {
            ["yst00001"] = [Line("yst00001", 1, "Um"), Line("yst00001", 2, "Dois"), Line("yst00001", 3, "Três")],
            ["yst00002"] = [Line("yst00002", 1, "Quatro")],
            ["yst00003"] = [Line("yst00003", 1, "Cinco"), Line("yst00003", 2, "Seis")],
        };
        var test = new GameLastTest(TestAt, GameTestKind.Translation, [], TranslationChanges.Snapshot(before));
        Dictionary<string, IReadOnlyList<TextUnit>> now = new()
        {
            ["yst00001"] = [Line("yst00001", 1, "Um!"), Line("yst00001", 2, "Dois"), Line("yst00001", 3, "Três!")],
            ["yst00002"] = before["yst00002"],
            ["yst00003"] = [Line("yst00003", 1, "Cinco"), Line("yst00003", 2, "Seis!")],
        };
        return (now, test);
    }

    private static async Task<EditorViewModel> OpenEditorAsync(Dictionary<string, IReadOnlyList<TextUnit>> texts, GameLastTest? test)
    {
        var vm = new EditorViewModel(_ => ProjectLines.Create(texts)) { LastTestLoader = _ => test };
        vm.Project = Summary();
        await vm.Loading;
        return vm;
    }

    [Fact]
    public async Task Abrir_leva_a_cena_so_com_as_falas_que_mudaram()
    {
        var (now, test) = Scenario();
        var vm = await OpenEditorAsync(now, test);

        var ok = await vm.ShowChangedSinceTestAsync("yst00003");

        Assert.True(ok);
        Assert.Equal("yst00003", vm.SelectedScript?.Key);
        var chip = vm.Filters.Single(f => f.Filter == LineStatusFilter.ChangedSinceTest);
        Assert.Contains(chip, vm.Filters);
        Assert.True(chip.IsSelected);
        Assert.Equal(1, chip.Count);
        Assert.Equal(["yst00003:000002"], vm.Lines.Select(l => l.Id));
    }

    [Fact]
    public async Task Um_teste_novo_tira_o_chip_ao_voltar_ao_Editor()
    {
        using var temp = new TempFolder();
        var main = await OpenMainAsync(temp);
        await main.Build.ReloadAsync();
        main.Build.SinceTest.OpenCommand.Execute(main.Build.SinceTest.Rows[0]);
        await main.LineNavigation;
        Assert.Contains(main.Editor.Filters, f => f.Filter == LineStatusFilter.ChangedSinceTest);

        main.SelectedNav = main.Nav.First(n => n.Key == NavKey.Build);
        var project = ProjectFolder.Open(main.Editor.Project!.Root);
        GameStateFile.Save(project, new GameStateFile(GameStateFile.CurrentVersion, null, new GameLastTest(TestAt.AddHours(2), GameTestKind.Translation, [], TranslationChanges.Snapshot(project.LoadAllText()))));
        main.SelectedNav = main.Nav.First(n => n.Key == NavKey.Editor);

        Assert.DoesNotContain(main.Editor.Filters, f => f.Filter == LineStatusFilter.ChangedSinceTest);
        Assert.True(main.Editor.Filters.Single(f => f.Filter == LineStatusFilter.All).IsSelected);
    }

    [Fact]
    public void Os_motivos_da_lista_vazia_aparecem_em_portugues_sem_travessao()
    {
        var (now, test) = Scenario();
        var list = new ChangedScenesViewModel();

        list.Update(TranslationChanges.Since(null, now), _ => string.Empty);
        var none = list.Message;
        list.Update(TranslationChanges.Since(test with { Snapshot = null }, now), _ => string.Empty);
        var old = list.Message;
        list.Update(TranslationChanges.Since(test with { Kind = GameTestKind.AccentTest }, now), _ => string.Empty);
        var accents = list.Message;

        Assert.False(list.IsInformative);
        Assert.Contains("Ainda não houve teste", none);
        Assert.Contains("versão anterior", old);
        Assert.Contains("só dos acentos", accents);
        Assert.All(new[] { none, old, accents }, m => Assert.DoesNotContain('—', m));
        Assert.All(new[] { none, old, accents }, m => Assert.DoesNotContain('–', m));
        Assert.False(list.HasRows);
    }

    [Fact]
    public void Cena_so_com_falas_removidas_aparece_sem_Abrir_e_o_comando_nao_pede_nada()
    {
        var (now, test) = Scenario();
        now["yst00002"] = [];
        var list = new ChangedScenesViewModel();
        string? requested = null;
        list.OpenRequested += (_, key) => requested = key;

        list.Update(TranslationChanges.Since(test, now), scene => scene.Key);

        var removed = list.Rows.Single(r => r.Key == "yst00002");
        Assert.False(removed.CanOpen);
        Assert.Equal("1 fala removida", removed.LinesText);
        Assert.All(list.Rows.Where(r => r.Key != "yst00002"), r => Assert.True(r.CanOpen));
        list.OpenCommand.Execute(removed);
        Assert.Null(requested);
    }

    [Fact]
    public void Teste_que_nao_leu_os_textos_nao_diz_que_e_de_uma_versao_anterior()
    {
        var (now, test) = Scenario();
        var list = new ChangedScenesViewModel();

        list.Update(TranslationChanges.Since(test with { Snapshot = null, SnapshotFailed = true }, now), scene => scene.Key);

        Assert.DoesNotContain("versão anterior", list.Message);
        Assert.Contains("não guardou as traduções", list.Message);
        Assert.Empty(list.Rows);
        Assert.False(list.IsInformative);
    }

    [Fact]
    public async Task Sem_o_retrato_por_falha_de_leitura_o_Editor_nao_liga_o_filtro()
    {
        var (now, test) = Scenario();
        var vm = await OpenEditorAsync(now, test with { Snapshot = null, SnapshotFailed = true });

        Assert.False(await vm.ShowChangedSinceTestAsync("yst00001"));
    }

    [Fact]
    public async Task O_Painel_lista_as_cenas_e_o_Abrir_pede_o_Editor_com_o_filtro()
    {
        var (now, test) = Scenario();
        var status = ProjectStatusReporter.Create(now);
        var dashboard = new ProjectDashboard(status, [.. Enum.GetValues<PipelineStageKey>().Select(k => new PipelineStage(k, true, 0))], AiUsageTotals.Empty)
        {
            ChangesSinceTest = TranslationChanges.Since(test, now),
            ScriptNames = ScriptNames.From([new ScriptNameEntry("yst00001", null, "Prólogo")]),
        };
        var vm = new DashboardViewModel(_ => dashboard, new FakeTimeProvider(TestAt)) { Project = Summary() };
        await vm.ReloadAsync();
        DashboardNavigation? navigation = null;
        vm.NavigationRequested += (_, request) => navigation = request;

        Assert.Equal(["Prólogo", "Cena 3"], vm.SinceTest.Rows.Select(r => r.Name));
        vm.SinceTest.OpenCommand.Execute(vm.SinceTest.Rows[0]);

        Assert.Equal(new DashboardNavigation(NavKey.Editor, "yst00001", ChangedSinceTest: true), navigation);
    }

    private static async Task<MainWindowViewModel> OpenMainAsync(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Approved, TranslationStatus.Approved);
        var project = ProjectFolder.Open(root);
        var snapshot = TranslationChanges.Snapshot(project.LoadAllText());
        project.SaveText("yst00001", [.. project.LoadText("yst00001").Select((u, i) => i == 1 ? u with { Translation = "Mudou!" } : u)]);
        GameStateFile.Save(project, new GameStateFile(GameStateFile.CurrentVersion, null, new GameLastTest(TestAt, GameTestKind.Translation, [], snapshot)));
        var main = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(DateTimeOffset.UnixEpoch)));
        await main.Start.OpenFolderCommand.ExecuteAsync(null);
        await main.Editor.Loading;
        return main;
    }

    [Fact]
    public async Task Abrir_no_Painel_leva_ao_Editor_com_so_as_falas_que_mudaram()
    {
        using var temp = new TempFolder();
        var main = await OpenMainAsync(temp);
        await main.Dashboard.ReloadAsync();
        Assert.Equal(["Cena 1"], main.Dashboard.SinceTest.Rows.Select(r => r.Name));

        main.Dashboard.SinceTest.OpenCommand.Execute(main.Dashboard.SinceTest.Rows[0]);
        await main.LineNavigation;

        Assert.Equal(NavKey.Editor, main.SelectedNav!.Key);
        Assert.Equal("yst00001", main.Editor.SelectedScript?.Key);
        Assert.True(main.Editor.Filters.Single(f => f.Filter == LineStatusFilter.ChangedSinceTest).IsSelected);
        Assert.Equal(["yst00001:000001"], main.Editor.Lines.Select(l => l.Id));
    }

    private sealed class IdleStarter : VNForge.Core.Projects.IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory) => throw new IOException("o teste não abre o jogo");
    }
}
