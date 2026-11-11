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

    private async Task<EditorViewModel> Abrir(IApprovalLog? registro = null, Func<TextUnit, string>? preco = null, Func<string, string?>? nomes = null, Func<string, ScriptNames>? capitulos = null)
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao(), scriptLabel: nomes)
        {
            NamesLoader = capitulos,
            ApprovalLogFactory = registro is null ? null : _ => registro,
            SuggestionPrice = preco,
        };
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    private TextUnit Gravada(string script, int n) => ProjectFolder.Open(raiz).LoadText(script)[n];

    [Fact]
    public async Task Esta_boa_registra_a_aprovacao_com_a_hora_do_relogio_em_sessions_json()
    {
        var log = new ProjectApprovalLog(raiz);
        var vm = await Abrir(log);
        vm.StartReview();

        vm.Review!.ApproveCommand.Execute(null);
        relogio.Advance(TimeSpan.FromMinutes(2));
        vm.Review.ApproveCommand.Execute(null);
        await log.Saving;

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
        Assert.Equal("Cena 2", revisao.NextSceneName);
        Assert.True(revisao.HasNextScene);
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
        Assert.Equal([revisao.Previous], revisao.PreviousLines);

        revisao.BackCommand.Execute(null);

        Assert.Null(revisao.Previous);
        Assert.False(revisao.HasPrevious);
        Assert.Empty(revisao.PreviousLines);
        Assert.Equal("Narração", revisao.Next!.Who);
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
    public async Task Desfazer_uma_aprovacao_recarrega_as_linhas_e_fecha_o_modo_revisao()
    {
        var log = new ProjectApprovalLog(raiz);
        var vm = await Abrir(log);
        LineSavedEventArgs? aprovada = null;
        vm.LineSaved += (_, e) => aprovada = e;
        vm.StartReview();
        vm.Review!.ApproveCommand.Execute(null);
        Assert.Equal(1, vm.ReviewedToday);

        Assert.True(vm.Undo(aprovada!));
        await log.Saving;

        Assert.False(vm.IsReviewing);
        Assert.Equal(TranslationStatus.Machine, Gravada("yst00001", 0).Status);
        Assert.Equal(0, vm.ReviewedToday);
        Assert.Empty(SessionsFile.Load(ProjectFolder.Open(raiz)).Approvals);
    }

    [Fact]
    public async Task A_proxima_cena_da_tela_final_nao_fecha_o_modo_revisao()
    {
        var vm = await Abrir();
        vm.StartReview();
        foreach (var _ in Enumerable.Range(0, 3))
        {
            vm.Review!.SkipCommand.Execute(null);
        }

        vm.Review!.GoToNextSceneCommand.Execute(null);

        Assert.True(vm.IsReviewing);
        Assert.Equal("yst00002", vm.SelectedScript!.Key);
    }

    [Fact]
    public async Task Ctrl_F_e_Ctrl_H_nao_abrem_a_busca_por_baixo_do_modo_revisao()
    {
        var vm = await Abrir();
        vm.StartReview();

        vm.OpenSearchCommand.Execute(null);
        vm.OpenReplaceCommand.Execute(null);
        vm.ToggleSearchCommand.Execute(null);

        Assert.False(vm.IsSearchOpen);
        Assert.False(vm.IsReplaceVisible);
    }

    private sealed class FakeApprovalLog : IApprovalLog
    {
        public List<ApprovalRecord> Records { get; } = [];

        public void Record(string scriptKey, string unitId, DateTimeOffset at) => Records.Add(new ApprovalRecord(scriptKey, unitId, at));

        public void Remove(string scriptKey, string unitId)
        {
            var index = Records.FindLastIndex(r => r.ScriptKey == scriptKey && r.UnitId == unitId);
            if (index >= 0)
            {
                Records.RemoveAt(index);
            }
        }

        public int ReviewedOn(DateTimeOffset now, TimeZoneInfo zone) => Records.Select(r => r.UnitId).Distinct().Count();
    }

    [Fact]
    public async Task Sem_cena_aberta_o_modo_revisao_nao_abre_e_a_fila_por_risco_so_abre_pelo_proprio_comando()
    {
        var vm = new EditorViewModel();

        vm.StartReview();
        vm.StartReviewOf(ReviewSource.Doubts);
        await vm.StartRiskReviewAsync();

        Assert.False(vm.IsReviewing);
        Assert.Throws<ArgumentNullException>(() => vm.StartReviewOf(null!));
        Assert.Throws<ArgumentException>(() => vm.StartReviewOf(ReviewSource.Risk));
    }
}
