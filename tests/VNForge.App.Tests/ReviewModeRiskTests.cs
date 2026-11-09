using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O modo revisão na ordem "o que mais precisa de você": a fila de risco do projeto inteiro, o cartão da linha, as horas e a troca de ordem.</summary>
public sealed class ReviewModeRiskTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 25, 15, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public ReviewModeRiskTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste");
        var projeto = ProjectFolder.Open(raiz);
        projeto.SaveText("yst00001",
        [
            Fala(1, 0, "美咲", "Bom dia.\\p", TranslationStatus.Machine),
            Fala(1, 1, "健太", "Oi.\\p", TranslationStatus.Machine, duvida: true, estranho: true),
            Fala(1, 2, "美咲", "Aham.\\p", TranslationStatus.Approved),
            Fala(1, 3, "健太", "Ei.\\p", TranslationStatus.Machine),
        ]);
        projeto.SaveText("yst00002",
        [
            Fala(2, 0, "美咲", "Sim.\\p", TranslationStatus.Machine, duvida: true),
            Fala(2, 1, "美咲", null, TranslationStatus.Untranslated),
        ]);
        projeto.SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "animada"), new CharacterEntry("健太", "Kenta", CharacterGender.Male)]);
        GravarSugestao("yst00001:000001", "Oi.\\p");
    }

    public void Dispose() => pasta.Dispose();

    private static TextUnit Fala(int script, int n, string falante, string? traducao, TranslationStatus status, bool duvida = false, bool estranho = false)
    {
        var unit = new TextUnit($"yst0000{script}:{n:D6}", TextUnitKind.Dialogue, falante, "「原文」\\p", traducao, status)
        {
            Doubt = duvida ? new TranslationDoubt(DoubtKind.Tone, "Ela fala sério?") : null,
        };
        return estranho ? unit.WithOdd(true) : unit;
    }

    private void GravarSugestao(string id, string baseText)
    {
        var cache = ProjectFolder.Open(raiz).CacheDirectory;
        Directory.CreateDirectory(cache);
        var suggestion = new { scriptKey = id.Split(':')[0], unitId = id, kind = "voice", @base = baseText, proposed = "Olá.\\p", reason = "voz", at = DateTimeOffset.UnixEpoch };
        File.WriteAllText(Path.Combine(cache, "editor-suggestions.json"), JsonSerializer.Serialize(new { version = 1, suggestions = new[] { suggestion } }, JsonSerializerOptions.Web));
    }

    private void GravarRitmo120PorHora()
    {
        var inicio = relogio.GetUtcNow().AddHours(-1);
        var sessoes = Enumerable.Range(0, 60).Aggregate(
            SessionsFile.Empty,
            (arquivo, i) => arquivo.With(new ApprovalRecord("yst99999", $"yst99999:{i:D6}", inicio.AddSeconds(i * 30))));
        SessionsFile.Save(ProjectFolder.Open(raiz), sessoes);
    }

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    private TextUnit Gravada(string script, int n) => ProjectFolder.Open(raiz).LoadText(script)[n];

    [Fact]
    public async Task A_fila_por_risco_junta_as_cenas_do_maior_risco_ao_menor_e_comeca_pela_linha_mais_arriscada()
    {
        var vm = await Abrir();

        vm.StartRiskReview();

        var revisao = vm.Review!;
        Assert.True(revisao.IsRisk);
        Assert.True(vm.IsReviewing);
        Assert.Equal(4, revisao.Total);
        Assert.Equal("yst00001:000001", revisao.Detail!.Id);
        revisao.SkipCommand.Execute(null);
        Assert.Equal("yst00002:000000", revisao.Detail!.Id);
        Assert.Equal("yst00002", vm.SelectedScript!.Key);
        Assert.Equal("Linha 2 de 4", revisao.PositionText);
    }

    [Fact]
    public async Task O_cartao_da_linha_traz_a_pontuacao_o_nivel_e_um_chip_por_sinal()
    {
        var vm = await Abrir();

        vm.StartRiskReview();

        var revisao = vm.Review!;
        Assert.StartsWith("Risco ", revisao.RiskScoreText, StringComparison.Ordinal);
        Assert.Equal("Alto", revisao.RiskLevelText);
        Assert.True(revisao.IsHighRisk);
        Assert.Equal("linha 0002 · 1ª da fila", revisao.RiskPositionText);
        var chips = revisao.RiskChips.Select(c => c.Text).ToList();
        Assert.Contains("Dúvida da IA +30", chips);
        Assert.Contains("Soa estranho +20", chips);
        Assert.Contains("Sugestão do editor +10", chips);
        Assert.True(revisao.HasRiskChips);
        Assert.True(revisao.ShowRisk);
    }

    [Fact]
    public async Task Sem_o_arquivo_da_passada_de_editor_a_sugestao_nao_pesa_e_a_fila_abre_igual()
    {
        File.Delete(Path.Combine(ProjectFolder.Open(raiz).CacheDirectory, "editor-suggestions.json"));
        var vm = await Abrir();

        vm.StartRiskReview();

        var chips = vm.Review!.RiskChips.Select(c => c.Text).ToList();
        Assert.Contains("Dúvida da IA +30", chips);
        Assert.DoesNotContain("Sugestão do editor +10", chips);
    }

    [Fact]
    public async Task Sugestao_que_ja_nao_vale_para_a_traducao_da_linha_nao_pesa()
    {
        GravarSugestao("yst00001:000001", "uma tradução que a linha já não tem");
        var vm = await Abrir();

        vm.StartRiskReview();

        Assert.DoesNotContain("Sugestão do editor +10", vm.Review!.RiskChips.Select(c => c.Text));
    }

    [Fact]
    public async Task As_horas_ate_zerar_o_risco_alto_saem_do_ritmo_do_sessions_json_e_zeram_ao_passar_da_linha()
    {
        GravarRitmo120PorHora();
        var vm = await Abrir();

        vm.StartRiskReview();

        var revisao = vm.Review!;
        Assert.Equal("1 linha de risco alto · faltam cerca de 1 min no seu ritmo de 120 linhas por hora", revisao.RiskSummaryText);
        revisao.SkipCommand.Execute(null);
        Assert.Equal("Nenhuma linha de risco alto", revisao.RiskSummaryText);
    }

    [Fact]
    public async Task Sem_ritmo_a_frase_diz_que_ainda_nao_ha()
    {
        var vm = await Abrir();

        vm.StartRiskReview();

        Assert.Equal("1 linha de risco alto · sem ritmo ainda, aprove algumas linhas para calcular as horas", vm.Review!.RiskSummaryText);
    }

    [Fact]
    public async Task A_distribuicao_e_a_legenda_contam_a_fila_toda()
    {
        var vm = await Abrir();

        vm.StartRiskReview();

        var revisao = vm.Review!;
        Assert.Equal(100, revisao.RiskShare.High + revisao.RiskShare.Medium + revisao.RiskShare.Low);
        Assert.Equal(25, revisao.RiskShare.High);
        Assert.Equal(["Alto 25%", "Médio 25%", "Baixo 50%"], revisao.RiskLegend.Select(l => l.Text));
    }

    [Fact]
    public async Task A_lista_a_seguir_comeca_na_linha_de_agora_e_mostra_nivel_falante_e_sinais()
    {
        var vm = await Abrir();

        vm.StartRiskReview();

        var linhas = vm.Review!.UpNext;
        Assert.Equal(4, linhas.Count);
        Assert.True(linhas[0].IsCurrent);
        Assert.All(linhas.Skip(1), l => Assert.False(l.IsCurrent));
        Assert.Equal("Alto", linhas[0].LevelText);
        Assert.True(linhas[0].IsHigh);
        Assert.Equal("0002", linhas[0].LineLabel);
        Assert.Equal("Kenta", linhas[0].Speaker);
        Assert.Equal("Oi.", linhas[0].Text);
        Assert.Contains("Dúvida da IA", linhas[0].Signals, StringComparison.Ordinal);
        Assert.Contains("Soa estranho · Sugestão do editor", linhas[0].Signals, StringComparison.Ordinal);
        vm.Review.SkipCommand.Execute(null);
        Assert.True(vm.Review.UpNext[0].IsCurrent);
        Assert.Equal("0001", vm.Review.UpNext[0].LineLabel);
        Assert.Equal(3, vm.Review.UpNext.Count);
    }

    [Fact]
    public async Task Esta_boa_atravessa_as_cenas_grava_cada_linha_e_a_tela_final_nao_oferece_a_proxima_cena()
    {
        var vm = await Abrir();
        vm.StartRiskReview();
        var revisao = vm.Review!;

        for (var i = 0; i < 4; i++)
        {
            revisao.ApproveCommand.Execute(null);
        }

        Assert.True(revisao.IsFinished);
        Assert.False(revisao.ShowRisk);
        Assert.Equal("Fila por risco concluída", revisao.FinishedTitle);
        Assert.Equal("4 linhas aprovadas.", revisao.FinishedText);
        Assert.False(revisao.HasNextScene);
        Assert.Equal(TranslationStatus.Approved, Gravada("yst00001", 1).Status);
        Assert.Equal(TranslationStatus.Approved, Gravada("yst00002", 0).Status);
    }

    [Fact]
    public async Task A_ordem_alterna_entre_do_comeco_e_o_que_mais_precisa_de_voce()
    {
        var vm = await Abrir();
        vm.StartReview();
        Assert.True(vm.Review!.HasOrderChoice);
        Assert.Equal(0, vm.Review.OrderIndex);
        Assert.Equal(3, vm.Review.Total);
        Assert.Equal("yst00001:000000", vm.Review.Detail!.Id);

        vm.Review.OrderIndex = 1;

        Assert.True(vm.Review.IsRisk);
        Assert.Equal(1, vm.Review.OrderIndex);
        Assert.Equal(4, vm.Review.Total);
        Assert.Equal("yst00001:000001", vm.Review.Detail!.Id);

        vm.Review.OrderIndex = 0;

        Assert.False(vm.Review.IsRisk);
        Assert.Equal(0, vm.Review.OrderIndex);
        Assert.Equal(3, vm.Review.Total);
    }

    [Fact]
    public async Task A_fila_de_um_personagem_nao_oferece_trocar_a_ordem()
    {
        var vm = await Abrir();

        vm.StartReviewOf(ReviewSource.OfSpeaker("美咲"));

        Assert.False(vm.Review!.HasOrderChoice);
        vm.Review.OrderIndex = 1;
        Assert.False(vm.Review.IsRisk);
        Assert.Equal(0, vm.Review.OrderIndex);
    }

    [Fact]
    public async Task Sem_linhas_esperando_revisao_a_fila_por_risco_nao_abre()
    {
        var projeto = ProjectFolder.Open(raiz);
        projeto.SaveText("yst00001", [Fala(1, 0, "美咲", "Bom dia.\\p", TranslationStatus.Approved)]);
        projeto.SaveText("yst00002", [Fala(2, 0, "美咲", "Sim.\\p", TranslationStatus.Approved)]);
        var vm = await Abrir();

        vm.StartRiskReview();

        Assert.False(vm.IsReviewing);
    }

    [Fact]
    public async Task A_fila_por_risco_nao_cria_um_bloco_por_linha_no_topo()
    {
        var vm = await Abrir();

        vm.StartRiskReview();

        Assert.Equal(4, vm.Review!.Blocks.Count);
        Assert.Empty(vm.Review.VisibleBlocks);
        vm.StartReview();
        Assert.Equal(vm.Review!.Blocks.Count, vm.Review.VisibleBlocks.Count);
    }
}
