using Microsoft.Extensions.Time.Testing;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A dúvida que a IA registra ao traduzir: o selo na grade, o filtro, o cartão do inspetor, aprovar e o modo revisão só das dúvidas.</summary>
public sealed class LineDoubtTests : IDisposable
{
    private static readonly TranslationDoubt Sujeito = new(DoubtKind.HiddenSubject, "O japonês não diz quem fala.");

    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public LineDoubtTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste");
        ProjectFolder.Open(raiz).SaveText("yst00001",
        [
            Fala(0, "Bom dia.\\p", TranslationStatus.Machine, Sujeito),
            Fala(1, "Chovia.\\p", TranslationStatus.Machine, null),
            Fala(2, "Aham.\\p", TranslationStatus.Machine, new TranslationDoubt(DoubtKind.Tone, "Ironia ou carinho?")),
            Fala(3, "Ei.\\p", TranslationStatus.Approved, null),
        ]);
    }

    public void Dispose() => pasta.Dispose();

    private static TextUnit Fala(int n, string traducao, TranslationStatus status, TranslationDoubt? duvida) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, "美咲", $"原文{n}\\p", traducao, status) { Doubt = duvida };

    private TextUnit Gravada(int n) => ProjectFolder.Open(raiz).LoadText("yst00001")[n];

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    [Fact]
    public async Task A_grade_marca_so_as_linhas_com_duvida_e_a_dica_traz_o_tipo_e_a_frase()
    {
        var vm = await Abrir();

        Assert.Equal([true, false, true, false], vm.Lines.Select(l => l.HasDoubt));
        Assert.Equal("Sujeito oculto: O japonês não diz quem fala.", vm.Lines[0].DoubtTip);
        Assert.Null(vm.Lines[1].DoubtTip);
    }

    [Fact]
    public async Task O_filtro_Com_duvida_mostra_so_as_linhas_com_duvida_e_conta_quantas_sao()
    {
        var vm = await Abrir();
        var chip = vm.Filters.Single(f => f.Filter == LineStatusFilter.Doubt);

        Assert.Equal("Com dúvida", chip.Label);
        Assert.Equal(2, chip.Count);

        vm.SelectFilterCommand.Execute(chip);

        Assert.Equal([0, 2], vm.Lines.Select(l => l.Number - 1));
    }

    [Fact]
    public async Task O_chip_e_o_botao_Duvidas_dizem_o_mesmo_numero_mesmo_com_um_personagem_escolhido()
    {
        ProjectFolder.Open(raiz).SaveText("yst00001",
        [
            Fala(0, "Bom dia.\\p", TranslationStatus.Machine, Sujeito),
            Fala(1, "Chovia.\\p", TranslationStatus.Machine, Sujeito) with { Speaker = "健太" },
            Fala(2, "Aham.\\p", TranslationStatus.Machine, null),
        ]);
        var vm = await Abrir();
        var chip = vm.Filters.Single(f => f.Filter == LineStatusFilter.Doubt);

        vm.SelectedSpeaker = vm.Speakers.Single(s => s.Value == "健太");

        Assert.Equal(2, vm.DoubtLines);
        Assert.Equal(vm.DoubtLines, chip.Count);
        vm.StartDoubtReview();
        Assert.Equal(vm.DoubtLines, vm.Review!.Total);
    }

    [Fact]
    public async Task O_chip_Com_duvida_so_aparece_quando_ha_duvida_ou_ele_e_o_filtro_ativo()
    {
        var vm = await Abrir();
        var chip = vm.Filters.Single(f => f.Filter == LineStatusFilter.Doubt);
        Assert.True(chip.IsShown);
        Assert.True(vm.Filters.Where(f => f != chip && f.Filter != LineStatusFilter.Odd).All(f => f.IsShown));

        ProjectFolder.Open(raiz).SaveText("yst00001", [Fala(0, "Oi.\\p", TranslationStatus.Machine, null)]);
        var semDuvida = await Abrir();
        var vazio = semDuvida.Filters.Single(f => f.Filter == LineStatusFilter.Doubt);

        Assert.False(vazio.IsShown);
        vazio.IsSelected = true;
        Assert.True(vazio.IsShown);
    }

    [Fact]
    public async Task O_inspetor_mostra_o_cartao_da_duvida_e_aprovar_a_linha_o_encerra_na_grade_no_arquivo_e_no_filtro()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[0];
        var detail = vm.Detail!;
        var mudou = new List<string?>();
        detail.PropertyChanged += (_, e) => mudou.Add(e.PropertyName);

        Assert.True(detail.HasDoubt);
        Assert.Equal("Dúvida · Sujeito oculto", detail.DoubtTitle);
        Assert.Equal("O japonês não diz quem fala.", detail.DoubtNote);

        detail.ApproveCommand.Execute(null);

        Assert.False(detail.HasDoubt);
        Assert.Equal(string.Empty, detail.DoubtTitle);
        Assert.Contains(nameof(LineDetailViewModel.HasDoubt), mudou);
        Assert.Null(Gravada(0).Doubt);
        Assert.False(vm.Lines[0].HasDoubt);
        Assert.Equal(1, vm.Filters.Single(f => f.Filter == LineStatusFilter.Doubt).Count);
        Assert.Equal(1, vm.DoubtLines);
    }

    [Fact]
    public async Task Editar_a_traducao_encerra_a_duvida_e_uma_linha_sem_duvida_nao_tem_cartao()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[1];
        Assert.False(vm.Detail!.HasDoubt);

        vm.SelectedLine = vm.Lines[2];
        vm.Detail!.Translation = "Aham, claro.\\p";
        Assert.True(vm.Detail.Flush());

        Assert.Null(Gravada(2).Doubt);
        Assert.False(vm.Detail.HasDoubt);
    }

    [Fact]
    public async Task O_modo_revisao_pode_comecar_so_pelas_duvidas()
    {
        var vm = await Abrir();

        vm.StartDoubtReview();

        var revisao = vm.Review!;
        Assert.Equal(2, revisao.Total);
        Assert.Equal("yst00001:000000", revisao.Detail!.Id);
        Assert.Equal("Linha 1 de 2", revisao.PositionText);
        Assert.Equal(string.Empty, revisao.SourceText);
        Assert.False(revisao.HasNextScene);
    }

    [Fact]
    public async Task Sem_duvida_no_script_o_modo_revisao_das_duvidas_nao_abre_e_o_comum_segue_com_as_linhas_da_IA()
    {
        ProjectFolder.Open(raiz).SaveText("yst00001", [Fala(0, "Oi.\\p", TranslationStatus.Machine, null)]);
        var vm = await Abrir();

        vm.StartDoubtReview();
        Assert.False(vm.IsReviewing);
        Assert.False(vm.HasDoubts);

        vm.StartReview();
        Assert.Equal(1, vm.Review!.Total);
    }

    [Fact]
    public async Task A_fonte_Duvidas_segue_a_ordem_do_script_pula_e_volta_entre_as_duas_linhas()
    {
        var vm = await Abrir();
        vm.StartReviewOf(ReviewSource.Doubts);
        var revisao = vm.Review!;

        Assert.Equal(vm.DoubtLines, revisao.Total);
        Assert.Equal("yst00001:000000", revisao.Detail!.Id);

        revisao.SkipCommand.Execute(null);
        Assert.Equal("yst00001:000002", revisao.Detail!.Id);
        Assert.Equal("Linha 2 de 2", revisao.PositionText);

        revisao.BackCommand.Execute(null);
        Assert.Equal("yst00001:000000", revisao.Detail!.Id);
        Assert.Equal("Linha 1 de 2", revisao.PositionText);
    }

    [Fact]
    public async Task Terminar_as_duvidas_mostra_a_tela_final_sem_proxima_cena_e_aprovar_encerra_cada_duvida()
    {
        var vm = await Abrir();
        vm.StartDoubtReview();
        var revisao = vm.Review!;

        revisao.ApproveCommand.Execute(null);
        revisao.ApproveCommand.Execute(null);

        Assert.True(revisao.IsFinished);
        Assert.Equal("Revisão da cena concluída", revisao.FinishedTitle);
        Assert.False(revisao.HasNextScene);
        Assert.Null(Gravada(0).Doubt);
        Assert.Null(Gravada(2).Doubt);
        Assert.Equal(0, vm.DoubtLines);
        Assert.False(vm.Filters.Single(f => f.Filter == LineStatusFilter.Doubt).IsShown);
    }

    [Fact]
    public async Task Abrir_as_duvidas_fecha_o_leitor_de_cena()
    {
        var vm = await Abrir();
        vm.StartReading();
        Assert.True(vm.IsReadingScene);

        vm.StartDoubtReview();

        Assert.False(vm.IsReadingScene);
        Assert.True(vm.IsReviewing);
        Assert.Equal(2, vm.Review!.Total);
    }

    [Fact]
    public async Task Sair_das_duvidas_e_voltar_refaz_a_fila_so_com_as_que_restam()
    {
        var vm = await Abrir();
        vm.StartDoubtReview();
        vm.Review!.ApproveCommand.Execute(null);

        vm.StopReview();
        Assert.False(vm.IsReviewing);
        Assert.Equal(1, vm.DoubtLines);

        vm.StartDoubtReview();

        Assert.Equal(1, vm.Review!.Total);
        Assert.Equal("yst00001:000002", vm.Review.Detail!.Id);
        Assert.Equal("Linha 1 de 1", vm.Review.PositionText);
    }

    [Fact]
    public void A_fonte_Duvidas_nao_oferece_a_proxima_cena_e_tem_os_textos_da_cena()
    {
        var duvidas = ReviewSource.Doubts;

        Assert.False(duvidas.OffersNextScene);
        Assert.False(duvidas.SpansScenes);
        Assert.True(ReviewSource.OpenScene.OffersNextScene);
        Assert.Null(duvidas.Caption("x"));
        Assert.Equal("Revisão da cena concluída", duvidas.FinishedTitle("x"));
        Assert.Equal("Esta cena não tem linhas com dúvida da IA.", duvidas.EmptyText("x"));
    }
}
