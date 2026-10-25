using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O modo revisão: a fila de linhas da IA, aprovar, deixar para depois, pular, a tela final e o registro de aprovações com hora.</summary>
public sealed class ReviewModeTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 25, 15, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public ReviewModeTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste");
        var projeto = ProjectFolder.Open(raiz);
        projeto.SaveText("yst00001",
        [
            Fala(1, 0, "美咲", "「おはよう」\\p", "Bom dia.\\p", TranslationStatus.Machine),
            Fala(1, 1, null, "雨が降っている。\\p", null, TranslationStatus.Untranslated),
            Fala(1, 2, "健太", "「うん」\\p", "Aham.\\p", TranslationStatus.Approved),
            Fala(1, 3, "美咲", "「ねえ」\\p", "Ei.\\p", TranslationStatus.Machine),
            Fala(1, 4, "健太", "「なに？」\\p", "O que foi?\\p", TranslationStatus.Machine),
        ]);
        projeto.SaveText("yst00002",
        [
            Fala(2, 0, "美咲", "「うん」\\p", "Sim.\\p", TranslationStatus.Machine),
            Fala(2, 1, "美咲", "「そう」\\p", "Isso.\\p", TranslationStatus.Machine),
        ]);
        projeto.SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "animada"), new CharacterEntry("健太", "Kenta")]);
    }

    public void Dispose() => pasta.Dispose();

    private static TextUnit Fala(int script, int n, string? falante, string original, string? traducao, TranslationStatus status) =>
        new($"yst0000{script}:{n:D6}", falante is null ? TextUnitKind.Narration : TextUnitKind.Dialogue, falante, original, traducao, status);

    private async Task<EditorViewModel> Abrir(IApprovalLog? registro = null, Func<TextUnit, string>? preco = null)
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao())
        {
            ApprovalLogFactory = registro is null ? null : _ => registro,
            SuggestionPrice = preco,
        };
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    private TextUnit Gravada(string script, int n) => ProjectFolder.Open(raiz).LoadText(script)[n];

    [Fact]
    public async Task A_fila_tem_so_as_linhas_da_IA_do_script_aberto_na_ordem_do_script()
    {
        var vm = await Abrir();

        vm.StartReview();

        var revisao = vm.Review!;
        Assert.True(vm.IsReviewing);
        Assert.Equal(3, revisao.Total);
        Assert.Equal("Linha 1 de 3", revisao.PositionText);
        Assert.Equal("yst00001:000000", revisao.Detail!.Id);
        Assert.True(revisao.Blocks[0].IsCurrent);
        Assert.All(revisao.Blocks.Skip(1), b => Assert.False(b.IsCurrent));

        var ids = new List<string> { revisao.Detail!.Id };
        revisao.SkipCommand.Execute(null);
        ids.Add(revisao.Detail!.Id);
        revisao.SkipCommand.Execute(null);
        ids.Add(revisao.Detail!.Id);
        Assert.Equal(["yst00001:000000", "yst00001:000003", "yst00001:000004"], ids);
    }

    [Fact]
    public async Task Esta_boa_aprova_grava_e_avanca_para_a_proxima_da_fila()
    {
        var vm = await Abrir();
        vm.StartReview();
        var revisao = vm.Review!;

        revisao.ApproveCommand.Execute(null);

        Assert.Equal(TranslationStatus.Approved, Gravada("yst00001", 0).Status);
        Assert.Equal(1, revisao.Position);
        Assert.Equal("Linha 2 de 3", revisao.PositionText);
        Assert.Equal("yst00001:000003", revisao.Detail!.Id);
        Assert.True(revisao.Blocks[0].IsApproved);
        Assert.True(revisao.Blocks[1].IsCurrent);
        Assert.Equal(1, revisao.ApprovedCount);
    }

    [Fact]
    public async Task Esta_boa_registra_a_aprovacao_com_a_hora_do_relogio_em_sessions_json()
    {
        var vm = await Abrir();
        vm.StartReview();

        vm.Review!.ApproveCommand.Execute(null);
        relogio.Advance(TimeSpan.FromMinutes(2));
        vm.Review.ApproveCommand.Execute(null);

        var registro = SessionsFile.Load(ProjectFolder.Open(raiz));
        Assert.Equal(
            [
                new ApprovalRecord("yst00001", "yst00001:000000", new DateTimeOffset(2026, 10, 25, 15, 0, 0, TimeSpan.Zero)),
                new ApprovalRecord("yst00001", "yst00001:000003", new DateTimeOffset(2026, 10, 25, 15, 2, 0, TimeSpan.Zero)),
            ],
            registro.Approvals);
        Assert.Equal(2, vm.ReviewedToday);
        Assert.Equal("2 linhas revisadas hoje", vm.Review.TodayText);
    }

    [Fact]
    public async Task Aprovar_no_inspetor_sem_o_modo_revisao_tambem_entra_no_registro()
    {
        var log = new FakeApprovalLog();
        var vm = await Abrir(log);

        vm.Detail!.ApproveCommand.Execute(null);
        vm.Detail.ApproveCommand.Execute(null);

        Assert.Equal([("yst00001", "yst00001:000000")], log.Records.Select(r => (r.ScriptKey, r.UnitId)));
    }

    [Fact]
    public async Task Depois_marca_a_linha_e_avanca_sem_aprovar_nem_registrar()
    {
        var log = new FakeApprovalLog();
        var vm = await Abrir(log);
        vm.StartReview();
        var revisao = vm.Review!;

        revisao.LaterCommand.Execute(null);

        Assert.Equal(TranslationStatus.Machine, Gravada("yst00001", 0).Status);
        Assert.True(revisao.Blocks[0].IsLater);
        Assert.Equal(1, revisao.Position);
        Assert.Equal(1, revisao.LaterCount);
        Assert.Empty(log.Records);
    }

    [Fact]
    public async Task Pular_so_avanca()
    {
        var log = new FakeApprovalLog();
        var vm = await Abrir(log);
        vm.StartReview();
        var revisao = vm.Review!;

        revisao.SkipCommand.Execute(null);

        Assert.Equal(TranslationStatus.Machine, Gravada("yst00001", 0).Status);
        Assert.True(revisao.Blocks[0].IsSkipped);
        Assert.Equal(1, revisao.Position);
        Assert.Empty(log.Records);
    }

    [Fact]
    public async Task Voltar_mostra_a_linha_de_antes()
    {
        var vm = await Abrir();
        vm.StartReview();
        var revisao = vm.Review!;
        revisao.SkipCommand.Execute(null);

        revisao.BackCommand.Execute(null);

        Assert.Equal(0, revisao.Position);
        Assert.Equal("yst00001:000000", revisao.Detail!.Id);
        Assert.True(revisao.Blocks[0].IsCurrent);
        Assert.False(revisao.Blocks[1].IsCurrent);
    }

    [Fact]
    public async Task Depois_da_ultima_linha_a_tela_final_resume_a_revisao_e_diz_a_proxima_cena()
    {
        var vm = await Abrir();
        vm.StartReview();
        var revisao = vm.Review!;

        revisao.ApproveCommand.Execute(null);
        revisao.LaterCommand.Execute(null);
        revisao.SkipCommand.Execute(null);

        Assert.True(revisao.IsFinished);
        Assert.False(revisao.IsActive);
        Assert.Equal("Revisão da cena concluída", revisao.FinishedTitle);
        Assert.Equal("1 linha aprovada, 1 deixada para depois e 1 pulada.", revisao.FinishedText);
        Assert.Equal("yst00002", revisao.NextSceneName);
        Assert.True(revisao.HasNextScene);
    }

    [Fact]
    public async Task Ir_para_a_proxima_cena_abre_a_revisao_dela()
    {
        var vm = await Abrir();
        vm.StartReview();
        foreach (var _ in Enumerable.Range(0, 3))
        {
            vm.Review!.SkipCommand.Execute(null);
        }

        vm.Review!.GoToNextSceneCommand.Execute(null);

        Assert.Equal("yst00002", vm.SelectedScript!.Key);
        Assert.True(vm.IsReviewing);
        Assert.Equal(2, vm.Review!.Total);
        Assert.Equal("yst00002:000000", vm.Review.Detail!.Id);
        Assert.False(vm.Review.HasNextScene);
    }

    [Fact]
    public async Task Cena_sem_linhas_da_IA_abre_direto_na_tela_final()
    {
        var vm = await Abrir();
        ProjectFolder.Open(raiz).SaveText("yst00002", [Fala(2, 0, "美咲", "「うん」", "Sim.", TranslationStatus.Approved)]);
        vm.Reload();
        await vm.Loading;
        vm.SelectedScript = vm.Scripts.Single(s => s.Key == "yst00002");

        vm.StartReview();

        Assert.True(vm.Review!.IsFinished);
        Assert.Equal(0, vm.Review.Total);
        Assert.Equal("Nenhuma linha da IA para revisar", vm.Review.FinishedTitle);
        Assert.Equal("Nenhuma linha", vm.Review.PositionText);
    }

    [Fact]
    public async Task Corrigir_volta_ao_editor_com_a_linha_aberta()
    {
        var vm = await Abrir();
        vm.StartReview();
        var revisao = vm.Review!;
        revisao.SkipCommand.Execute(null);

        revisao.CorrectCommand.Execute(null);

        Assert.False(vm.IsReviewing);
        Assert.Null(vm.Review);
        Assert.Equal("yst00001:000003", vm.SelectedLine!.Id);
        Assert.Equal("yst00001:000003", vm.Detail!.Id);
    }

    [Fact]
    public async Task Se_a_gravacao_falha_a_revisao_fica_na_linha_e_o_painel_mostra_o_erro()
    {
        var vm = await Abrir();
        vm.StartReview();
        File.Delete(Path.Combine(raiz, "text", "yst00001.json"));

        vm.Review!.ApproveCommand.Execute(null);

        Assert.Equal(0, vm.Review.Position);
        Assert.False(vm.Review.Blocks[0].IsApproved);
        Assert.NotNull(vm.Review.Detail!.ErrorMessage);
        Assert.False(vm.Review.IsFinished);
    }

    [Fact]
    public async Task Depois_nao_marca_a_linha_quando_a_gravacao_pendente_impede_de_sair_dela()
    {
        var vm = await Abrir();
        vm.StartReview();
        vm.Review!.Detail!.Translation = "Bom dia, Misaki.\\p";
        File.Delete(Path.Combine(raiz, "text", "yst00001.json"));

        vm.Review.LaterCommand.Execute(null);

        Assert.Equal(0, vm.Review.Position);
        Assert.False(vm.Review.Blocks[0].IsLater);
        Assert.Equal(0, vm.Review.LaterCount);
    }

    [Fact]
    public async Task Mostra_quem_fala_e_como_fala_e_a_fala_anterior_e_a_seguinte_do_script()
    {
        var vm = await Abrir();
        vm.StartReview();
        var revisao = vm.Review!;
        revisao.SkipCommand.Execute(null);

        Assert.Equal("美咲 → Misaki · animada", revisao.SpeakerText);
        Assert.Equal("Kenta", revisao.Previous!.Who);
        Assert.Equal("Aham.", revisao.Previous.Translation);
        Assert.Equal("Kenta", revisao.Next!.Who);
        Assert.Equal([revisao.Next], revisao.NextLines);

        revisao.BackCommand.Execute(null);

        Assert.Null(revisao.Previous);
        Assert.False(revisao.HasPrevious);
        Assert.Equal("Narração", revisao.Next!.Who);
    }

    [Fact]
    public async Task Personagem_sem_genero_acende_o_aviso_e_definir_o_genero_pede_o_glossario()
    {
        var vm = await Abrir();
        var pedidos = 0;
        vm.DefineGenderRequested += (_, _) => pedidos++;
        vm.StartReview();
        var revisao = vm.Review!;

        Assert.False(revisao.GenderMissing);

        revisao.SkipCommand.Execute(null);
        revisao.SkipCommand.Execute(null);

        Assert.Equal("yst00001:000004", revisao.Detail!.Id);
        Assert.True(revisao.GenderMissing);
        revisao.DefineGenderCommand.Execute(null);
        Assert.Equal(1, pedidos);
    }

    [Fact]
    public async Task Outra_sugestao_mostra_o_preco_em_reais_da_linha_em_exibicao()
    {
        var vm = await Abrir(preco: unidade => $"R$ {unidade.Id[^1]},00");
        vm.StartReview();
        var revisao = vm.Review!;

        Assert.Equal("R$ 0,00", revisao.SuggestionPriceText);
        Assert.True(revisao.HasSuggestionPrice);

        revisao.SkipCommand.Execute(null);

        Assert.Equal("R$ 3,00", revisao.SuggestionPriceText);
    }

    [Fact]
    public async Task Sem_estimativa_o_botao_fica_sem_preco()
    {
        var vm = await Abrir();
        vm.StartReview();

        Assert.False(vm.Review!.HasSuggestionPrice);
    }

    [Fact]
    public async Task Desfazer_uma_aprovacao_recarrega_as_linhas_e_fecha_o_modo_revisao()
    {
        var vm = await Abrir();
        LineSavedEventArgs? aprovada = null;
        vm.LineSaved += (_, e) => aprovada = e;
        vm.StartReview();
        vm.Review!.ApproveCommand.Execute(null);

        Assert.True(vm.Undo(aprovada!));

        Assert.False(vm.IsReviewing);
        Assert.Equal(TranslationStatus.Machine, Gravada("yst00001", 0).Status);
    }

    [Fact]
    public async Task Trocar_de_projeto_fecha_o_modo_revisao()
    {
        var vm = await Abrir();
        vm.StartReview();

        vm.Project = null;

        Assert.False(vm.IsReviewing);
    }

    [Fact]
    public async Task O_modo_revisao_mostra_todas_as_linhas_da_fila_mesmo_com_filtro_ativo()
    {
        var vm = await Abrir();
        vm.SelectFilterCommand.Execute(vm.Filters.Single(f => f.Label == "Pendentes"));
        Assert.True(vm.HasStatusFilter);
        Assert.Single(vm.Lines);

        vm.StartReview();

        Assert.False(vm.HasStatusFilter);
        Assert.Equal(5, vm.Lines.Count);
        Assert.Equal(3, vm.Review!.Total);
    }

    private sealed class FakeApprovalLog : IApprovalLog
    {
        public List<ApprovalRecord> Records { get; } = [];

        public void Record(string scriptKey, string unitId, DateTimeOffset at) => Records.Add(new ApprovalRecord(scriptKey, unitId, at));

        public int ReviewedOn(DateTimeOffset now, TimeZoneInfo zone) => Records.Select(r => r.UnitId).Distinct().Count();
    }
}
