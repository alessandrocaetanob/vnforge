using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A fala do cartão "Continuar de onde parou" do Início e o "Abrir" da linha da lista: o que a leitura escolhe, o que ela nunca mostra e o que ela deixa na tela.</summary>
public class StartContinueCardTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static StartViewModel Create(TempFolder temp) =>
        new(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now));

    private static TextUnit Unit(int i, TranslationStatus status, string? translation) =>
        new($"yst00001:{i:D6}", TextUnitKind.Dialogue, null, $"原文{i}", translation, status);

    private static string Project(TempFolder temp, string folder, string name, params TextUnit[] units)
    {
        var root = StartFixtures.Create(temp.SubFolder(folder), name, lines: [("yst00001", TranslationStatus.Machine)]);
        ProjectFolder.Open(root).SaveText("yst00001", units);
        return root;
    }

    [Fact]
    public async Task Fala_com_pagina_e_quebra_no_meio_chega_na_caixa_sem_marcador_cru()
    {
        using var temp = new TempFolder();
        var root = Project(temp, "vn", "Minha VN", Unit(0, TranslationStatus.Machine, "Primeira linha\\nsegunda linha\\pTerceira linha"));
        var vm = Create(temp);

        await vm.OpenAsync(root);
        await vm.DetailLoading;

        var layout = vm.ContinueLayout!;
        Assert.Equal(2, layout.Boxes.Count);
        var texts = layout.Boxes.SelectMany(b => b.Lines).Select(l => l.Text).ToList();
        Assert.Equal(["Primeira linha", "segunda linha", "Terceira linha"], texts);
        Assert.All(texts, t => Assert.DoesNotContain('\\', t));
    }

    [Fact]
    public async Task Fala_que_o_pipeline_recusa_passa_a_vez_para_a_proxima_da_cena()
    {
        using var temp = new TempFolder();
        var root = Project(temp, "vn", "Minha VN", Unit(0, TranslationStatus.Machine, "{ sem fechar"), Unit(1, TranslationStatus.Machine, "Fala boa"));
        var vm = Create(temp);

        await vm.OpenAsync(root);
        await vm.DetailLoading;

        Assert.Equal("Fala boa", Assert.Single(vm.ContinueLayout!.Boxes[0].Lines).Text);
        Assert.Equal("Próxima fala a revisar", vm.ContinueLineCaption);
    }

    [Fact]
    public async Task Cena_em_que_nenhuma_fala_monta_deixa_o_cartao_sem_a_caixa()
    {
        using var temp = new TempFolder();
        var root = Project(temp, "vn", "Minha VN", Unit(0, TranslationStatus.Machine, "{ sem fechar"), Unit(1, TranslationStatus.Approved, "\\"));
        var vm = Create(temp);

        await vm.OpenAsync(root);
        await vm.DetailLoading;

        Assert.False(vm.HasContinueLayout);
    }

    [Fact]
    public async Task Proximo_passo_sem_cena_nao_tem_fala_e_o_alvo_nao_leva_chave_de_script()
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN", lines: [("yst00001", TranslationStatus.Approved), ("yst00001", TranslationStatus.Approved)]);
        var vm = Create(temp);

        await vm.OpenAsync(root);
        await vm.DetailLoading;

        Assert.NotNull(vm.Detail.NextStepTarget);
        Assert.Null(vm.Detail.NextStepTarget!.ScriptKey);
        Assert.False(vm.HasContinueLayout);
    }

    [Fact]
    public async Task Trocar_a_selecao_durante_a_leitura_nunca_mostra_a_fala_do_projeto_anterior()
    {
        using var temp = new TempFolder();
        var a = Project(temp, "a", "Primeira VN", Unit(0, TranslationStatus.Machine, "fala do projeto A"));
        var b = Project(temp, "b", "Segunda VN", Unit(0, TranslationStatus.Machine, "fala do projeto B"));
        var vm = Create(temp);
        await vm.OpenAsync(a);
        await vm.OpenAsync(b);
        await vm.DetailLoading;
        var itemA = vm.Recents.Single(r => r.Name == "Primeira VN");
        var itemB = vm.Recents.Single(r => r.Name == "Segunda VN");
        var seen = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StartViewModel.ContinueLayout) && vm.ContinueLayout is { } layout)
            {
                seen.Add(layout.Boxes[0].Lines[0].Text);
            }
        };

        vm.Selected = itemA;
        vm.Selected = itemB;
        await vm.DetailLoading;
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("fala do projeto A", seen);
        Assert.Equal("fala do projeto B", vm.ContinueLayout!.Boxes[0].Lines[0].Text);
    }

    [Fact]
    public async Task Recarregar_o_mesmo_projeto_mantem_a_fala_e_o_proximo_passo_ate_a_leitura_nova_chegar()
    {
        using var temp = new TempFolder();
        var root = Project(temp, "vn", "Minha VN", Unit(0, TranslationStatus.Machine, "primeira fala"), Unit(1, TranslationStatus.Machine, "segunda fala"));
        var vm = Create(temp);
        await vm.OpenAsync(root);
        await vm.DetailLoading;
        Assert.Equal("primeira fala", vm.ContinueLayout!.Boxes[0].Lines[0].Text);

        // A pessoa aprovou a primeira fora do Início: o resumo muda (outras contagens), mas a raiz é a mesma.
        ProjectFolder.Open(root).SaveText("yst00001", [Unit(0, TranslationStatus.Approved, "primeira fala"), Unit(1, TranslationStatus.Machine, "segunda fala")]);
        var blanks = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StartViewModel.ContinueLayout) && vm.ContinueLayout is null)
            {
                blanks.Add("fala");
            }
        };
        vm.Detail.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DashboardViewModel.NextStepLabel) && vm.Detail.NextStepLabel.Length == 0)
            {
                blanks.Add("passo");
            }
        };

        await vm.LoadAsync();
        await vm.DetailLoading;

        Assert.Empty(blanks);
        Assert.Equal("segunda fala", vm.ContinueLayout!.Boxes[0].Lines[0].Text);
    }

    [Fact]
    public async Task Abrir_da_linha_nao_abre_projeto_de_motor_sem_suporte_nem_pasta_que_sumiu()
    {
        using var temp = new TempFolder();
        var unsupported = StartFixtures.Create(temp.SubFolder("k"), "Segunda VN", engineId: "kirikiri");
        var vanishing = StartFixtures.Create(temp.SubFolder("v"), "Terceira VN");
        var vm = Create(temp);
        await vm.OpenAsync(unsupported);
        await vm.OpenAsync(vanishing);
        Directory.Delete(vanishing, recursive: true);
        await vm.LoadAsync();
        var pedidos = 0;
        vm.DashboardRequested += (_, _) => pedidos++;
        var opened = 0;
        vm.ProjectOpened += (_, _) => opened++;

        foreach (var item in vm.Recents)
        {
            await vm.OpenRecentCommand.ExecuteAsync(item);
        }

        Assert.Equal(2, vm.Recents.Count);
        Assert.False(vm.Recents[0].IsSupported);
        Assert.Equal(0, pedidos);
        Assert.Equal(0, opened);
    }
}
