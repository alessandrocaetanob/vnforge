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
    public async Task Um_original_com_tres_traducoes_vira_um_cartao_so_e_nao_aparece_na_lista_de_linhas()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        var card = Assert.Single(vm.ConsistencyCards);

        Assert.Equal("Mesmo original, 3 traduções", card.Title);
        Assert.Equal("5 linhas", card.LinesText);
        Assert.Equal(Source, card.Source);
        Assert.Equal(["3 linhas", "1 linha", "1 linha"], card.Variants.Select(v => v.CountText));
        Assert.Equal([true, false, false], card.Variants.Select(v => v.IsReference));
        Assert.DoesNotContain(vm.Rows, r => r.RuleId == "traducao-inconsistente");
    }

    [Fact]
    public async Task O_cartao_conta_nos_totais_uma_vez_por_linha_fora_da_referencia()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);

        var inconsistent = QaEngine.Default.Run(QaContext.FromProject(folder)).Issues.Count(i => i.RuleId == "traducao-inconsistente");

        Assert.Equal(2, inconsistent);
        Assert.Equal(inconsistent, vm.ConsistencyCards.Sum(c => c.Count));
        Assert.Equal(inconsistent, vm.Filters.Single(f => f.Kind == ConferenceFilterKind.WordsAndNames).Count);
        Assert.Contains("problemas abertos", vm.ListFooter);
    }

    [Fact]
    public async Task Cada_linha_e_um_link_com_a_cena_e_a_posicao_e_so_as_tres_primeiras_ficam_a_vista()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);
        var reference = vm.ConsistencyCards.Single().Variants[0];

        Assert.All(reference.VisibleLines, l => Assert.Matches(@"^(Cena \d+|.+) · \d+$", l.Label));
        Assert.Equal(("yst00001", "yst00001:000000"), (reference.VisibleLines[0].Request.ScriptKey, reference.VisibleLines[0].Request.UnitId));
        Assert.Equal("yst00001:000000", reference.VisibleLines[0].Request.UnitId);
        Assert.False(reference.HasMore);
        Assert.True(vm.OpenLineByIdCommand.CanExecute(reference.VisibleLines[0].Request));

        LineRequest? opened = null;
        vm.LineRequested += (_, request) => opened = request;
        vm.OpenLineByIdCommand.Execute(reference.VisibleLines[1].Request);
        Assert.Equal("yst00001:000001", opened?.UnitId);
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
    public async Task A_diferenca_mostra_o_ponto_que_falta_e_o_espaco_duplo_de_forma_visivel()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);
        var variants = vm.ConsistencyCards.Single().Variants;

        var semPonto = variants.Single(v => v.Translation.EndsWith("professores", StringComparison.Ordinal));
        var duplo = variants.Single(v => v.Translation.Contains("passou  na", StringComparison.Ordinal));

        Assert.Contains(semPonto.Segments, s => s is { Kind: TextDiffKind.Missing, Text: "." });
        Assert.Contains(duplo.Segments, s => s.Kind == TextDiffKind.Added && s.Visible == "·");
        Assert.All(variants[0].Segments, s => Assert.Equal(TextDiffKind.Same, s.Kind));
    }

    [Fact]
    public async Task Os_trechos_da_diferenca_trazem_cada_marcador_inteiro_para_a_tela_poder_desenhar_o_selo()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        ProjectFolder.Open(root).SaveText("yst00001",
        [
            Line("yst00001", 0, @"Mayumi passou na sala.\pDepois saiu.\p"),
            Line("yst00001", 1, @"Mayumi passou na sala.\pDepois saiu.\p"),
            Line("yst00001", 2, @"Mayumi passou na sala.\nDepois saiu.\p"),
            Line("yst00001", 3, @"Mayumi passou na sala.\pDepois saiu."),
        ]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };
        await vm.RefreshAsync();

        var variants = vm.ConsistencyCards.Single().Variants;

        Assert.Equal(3, variants.Count);
        Assert.All(variants, v => Assert.All(v.Segments, s => ControlMarkers.Split(s.Text)));
        var trocado = variants.Single(v => v.Translation.Contains(@"\nDepois", StringComparison.Ordinal));
        Assert.Contains(trocado.Segments, s => s is { Kind: TextDiffKind.Missing, Text: @"\p" });
        Assert.Contains(trocado.Segments, s => s is { Kind: TextDiffKind.Added, Text: @"\n" });
        var semFim = variants.Single(v => v.Translation.EndsWith("saiu.", StringComparison.Ordinal));
        Assert.Contains(semFim.Segments, s => s is { Kind: TextDiffKind.Missing, Text: @"\p" });
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
    public async Task Manter_diferentes_some_da_conferencia_grava_no_projeto_e_continua_depois_de_reabrir()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        var card = vm.ConsistencyCards.Single();
        var before = vm.IssueCount;

        Assert.True(vm.KeepDifferentCommand.CanExecute(card));
        await vm.KeepDifferentCommand.ExecuteAsync(card);

        Assert.Empty(vm.ConsistencyCards);
        Assert.Equal(before - 2, vm.IssueCount);
        Assert.All(folder.LoadAllText().Values.SelectMany(u => u).Where(u => u.Source == Source), u => Assert.Contains(ConsistencyGroups.FlagFor(card.Group), u.Flags));

        var reopened = new ConferenceViewModel { Project = vm.Project };
        await reopened.RefreshAsync();
        Assert.Empty(reopened.ConsistencyCards);
    }

    [Fact]
    public async Task Manter_diferentes_volta_a_aparecer_quando_uma_traducao_muda()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        await vm.KeepDifferentCommand.ExecuteAsync(vm.ConsistencyCards.Single());
        var units = folder.LoadText("yst00001").ToList();
        units[2] = units[2] with { Translation = "Mayumi foi à sala." };
        folder.SaveText("yst00001", units);

        await vm.RefreshAsync();

        Assert.Single(vm.ConsistencyCards);
    }

    [Fact]
    public async Task Com_a_pagina_ocupada_as_acoes_do_cartao_nao_executam()
    {
        using var temp = new TempFolder();
        // A regra extra segura a conferência até o teste olhar os comandos: sem ela a leitura terminava, em outra thread, antes dos Assert
        // quando a thread do teste ficava sem CPU, e os comandos já voltavam a executar.
        using var gate = new ManualResetEventSlim(initialState: true);
        var (vm, _) = await Loaded(temp, new QaEngine([.. QaEngine.Default.Rules, new GateRule(gate)]));
        var card = vm.ConsistencyCards.Single();
        gate.Reset();

        var running = vm.RefreshAsync();
        Assert.True(vm.IsBusy);
        Assert.False(vm.KeepDifferentCommand.CanExecute(card));
        Assert.False(vm.UseVariantCommand.CanExecute(card.Variants[0]));
        gate.Set();
        await running;

        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Os_rotulos_do_cartao_sao_os_da_issue()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        Assert.Equal("Usar em todas", vm.ConsistencyCards.Single().Variants[0].UseLabel);
        Assert.Equal("Manter diferentes de propósito", vm.ConsistencyCards.Single().KeepLabel);
    }

    [Fact]
    public async Task Sem_a_regra_no_motor_nao_ha_cartao_e_as_outras_linhas_seguem_na_lista()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        ProjectFolder.Open(root).SaveText("yst00001", [Line("yst00001", 0, "a  b"), Line("yst00001", 1, "c")]);
        var vm = new ConferenceViewModel(new QaEngine([new DoubleSpaceRule()])) { Project = ProjectSummaryReader.Read(root) };

        await vm.RefreshAsync();

        Assert.Empty(vm.ConsistencyCards);
        Assert.Contains(vm.Rows, r => r.RuleId == "espaco-duplo");
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
