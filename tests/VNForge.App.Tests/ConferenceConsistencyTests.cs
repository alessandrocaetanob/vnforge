using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class ConferenceConsistencyTests
{
    private const string Source = "真弓は校長室に行く前に、出勤状況を確認した。";

    private static TextUnit Line(string key, int n, string translation, string? source = null) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, null, source ?? Source, translation, TranslationStatus.Translated);

    /// <summary>Um original com três traduções em dois scripts: a com ponto final (3 linhas), a sem o ponto (1) e a com espaço duplo (1).</summary>
    private static async Task<(ConferenceViewModel Vm, ProjectFolder Folder)> Loaded(TempFolder temp, QaEngine? engine = null)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001",
        [
            Line("yst00001", 0, "Mayumi passou na sala dos professores."),
            Line("yst00001", 1, "Mayumi passou na sala dos professores."),
            Line("yst00001", 2, "Mayumi passou na sala dos professores"),
            Line("yst00001", 3, "outra coisa", "別の原文"),
        ]);
        folder.SaveText("yst00002",
        [
            Line("yst00002", 5, "Mayumi passou na sala dos professores."),
            Line("yst00002", 6, "Mayumi passou  na sala dos professores."),
        ]);
        var vm = new ConferenceViewModel(engine) { Project = ProjectSummaryReader.Read(root) };
        await vm.RefreshAsync();
        return (vm, folder);
    }

    [Fact]
    public async Task Mais_de_tres_linhas_mostram_mais_N_e_abrem_todas()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [.. Enumerable.Range(0, 9).Select(n => Line("yst00001", n, "Igual.")), Line("yst00001", 9, "Diferente.")]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };
        await vm.RefreshAsync();
        var variant = vm.ConsistencyCards.Single().Variants[0];

        Assert.Equal(3, variant.VisibleLines.Count);
        Assert.Equal("+6", variant.MoreLabel);

        variant.ToggleLinesCommand.Execute(null);

        Assert.Equal(9, variant.VisibleLines.Count);
        Assert.Equal("Mostrar menos", variant.MoreLabel);
    }

    [Fact]
    public async Task Usar_em_todas_grava_a_variante_nas_linhas_do_original_e_o_cartao_some()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        var chosen = vm.ConsistencyCards.Single().Variants[0];
        LineSavedEventArgs? saved = null;
        vm.LinesSaved += (_, e) => saved = e;
        var textsChanged = 0;
        vm.TextsChanged += (_, _) => textsChanged++;

        Assert.True(vm.UseVariantCommand.CanExecute(chosen));
        await vm.UseVariantCommand.ExecuteAsync(chosen);

        var all = folder.LoadAllText().Values.SelectMany(u => u).Where(u => u.Source == Source).ToList();
        Assert.Equal(5, all.Count);
        Assert.All(all, u => Assert.Equal("Mayumi passou na sala dos professores.", u.Translation));
        Assert.Empty(vm.ConsistencyCards);
        Assert.Equal(2, saved!.Batch.Count);
        Assert.Equal(1, textsChanged);
        Assert.Equal("outra coisa", folder.LoadText("yst00001").Single(u => u.Source == "別の原文").Translation);
    }

    [Fact]
    public async Task Desfazer_devolve_as_traducoes_de_antes_e_o_cartao_volta()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        LineSavedEventArgs? saved = null;
        vm.LinesSaved += (_, e) => saved = e;
        await vm.UseVariantCommand.ExecuteAsync(vm.ConsistencyCards.Single().Variants[0]);

        var undone = await vm.UndoAsync(saved!);

        Assert.True(undone);
        Assert.Equal("Mayumi passou  na sala dos professores.", folder.LoadText("yst00002").Single(u => u.Id == "yst00002:000006").Translation);
        Assert.Equal(3, Assert.Single(vm.ConsistencyCards).Variants.Count);
    }

    [Fact]
    public async Task Desfazer_recusa_quando_uma_linha_mudou_depois()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        LineSavedEventArgs? saved = null;
        vm.LinesSaved += (_, e) => saved = e;
        await vm.UseVariantCommand.ExecuteAsync(vm.ConsistencyCards.Single().Variants[0]);
        var units = folder.LoadText("yst00002").ToList();
        units[1] = units[1] with { Translation = "Editada depois." };
        folder.SaveText("yst00002", units);

        Assert.False(await vm.UndoAsync(saved!));
        Assert.Contains("mudou", vm.ErrorMessage);
    }

    [Fact]
    public async Task Manter_diferentes_nao_conta_como_aviso_marcado_como_certo()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        await vm.KeepDifferentCommand.ExecuteAsync(vm.ConsistencyCards.Single());
        var afterKeep = vm.SilencedSummary;
        await vm.RefreshAsync();

        Assert.Equal(afterKeep, vm.SilencedSummary);
    }

    private sealed class GateRule(ManualResetEventSlim gate) : IQaRule
    {
        public string Id => "regra-porta";

        public string Description => "Espera o teste liberar.";

        public IEnumerable<QaIssue> Check(QaContext context)
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            return [];
        }
    }
}
