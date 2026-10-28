using Microsoft.Extensions.Time.Testing;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O modo revisão com as falas de um personagem em todas as cenas, na ordem da história.</summary>
public sealed class ReviewModeSpeakerTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 25, 15, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public ReviewModeSpeakerTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste");
        var projeto = ProjectFolder.Open(raiz);
        projeto.SaveText("yst00001",
        [
            Fala(1, 0, "美咲", "Bom dia.\\p", TranslationStatus.Machine),
            Fala(1, 1, "健太", "Oi.\\p", TranslationStatus.Machine),
            Fala(1, 2, "美咲", "Aham.\\p", TranslationStatus.Approved),
            Fala(1, 3, "美咲", "Ei.\\p", TranslationStatus.Machine),
            Fala(1, 4, "美咲", null, TranslationStatus.Untranslated),
        ]);
        projeto.SaveText("yst00002",
        [
            Fala(2, 0, "健太", "Sim.\\p", TranslationStatus.Machine),
        ]);
        projeto.SaveText("yst00003",
        [
            Fala(3, 0, "美咲", "Isso.\\p", TranslationStatus.Machine),
            Fala(3, 1, "美咲", "Tudo bem.\\p", TranslationStatus.Machine),
        ]);
        projeto.SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "animada"), new CharacterEntry("健太", "Kenta")]);
    }

    public void Dispose() => pasta.Dispose();

    private static TextUnit Fala(int script, int n, string falante, string? traducao, TranslationStatus status) =>
        new($"yst0000{script}:{n:D6}", TextUnitKind.Dialogue, falante, "「原文」\\p", traducao, status);

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    private TextUnit Gravada(string script, int n) => ProjectFolder.Open(raiz).LoadText(script)[n];

    [Fact]
    public async Task A_fila_do_personagem_junta_as_linhas_da_IA_dele_de_todas_as_cenas_na_ordem_da_historia()
    {
        var vm = await Abrir();
        Assert.Equal("yst00001", vm.SelectedScript!.Key);

        vm.StartReviewOf(ReviewSource.OfSpeaker("美咲"));

        var revisao = vm.Review!;
        Assert.True(vm.IsReviewing);
        Assert.Equal(4, revisao.Total);
        Assert.Equal("Linha 1 de 4", revisao.PositionText);
        var ids = new List<string> { revisao.Detail!.Id };
        for (var i = 1; i < revisao.Total; i++)
        {
            revisao.SkipCommand.Execute(null);
            ids.Add(revisao.Detail!.Id);
        }

        Assert.Equal(["yst00001:000000", "yst00001:000003", "yst00003:000000", "yst00003:000001"], ids);
    }

    [Fact]
    public async Task A_fila_do_personagem_comeca_na_cena_dele_mesmo_com_outra_cena_aberta()
    {
        var vm = await Abrir();
        vm.SelectedScript = vm.Scripts.Single(s => s.Key == "yst00002");

        vm.StartReviewOf(ReviewSource.OfSpeaker("美咲"));

        Assert.Equal("yst00001:000000", vm.Review!.Detail!.Id);
        Assert.Equal("yst00001", vm.SelectedScript!.Key);
        Assert.Equal(vm.SelectedScript.Name, vm.Review.SceneName);
    }

    [Fact]
    public async Task Ao_passar_de_uma_cena_a_outra_o_nome_da_cena_e_o_painel_acompanham()
    {
        var vm = await Abrir();
        vm.StartReviewOf(ReviewSource.OfSpeaker("美咲"));
        var revisao = vm.Review!;
        var primeira = revisao.SceneName;
        revisao.SkipCommand.Execute(null);
        Assert.Equal(primeira, revisao.SceneName);

        revisao.SkipCommand.Execute(null);

        Assert.Equal("yst00003", vm.SelectedScript!.Key);
        Assert.Equal(vm.Scripts.Single(s => s.Key == "yst00003").Name, revisao.SceneName);
        Assert.NotEqual(primeira, revisao.SceneName);
        Assert.Equal("yst00003:000000", revisao.Detail!.Id);
        Assert.Equal("Linha 3 de 4", revisao.PositionText);
    }

    [Fact]
    public async Task Esta_boa_atravessa_as_cenas_grava_cada_linha_e_a_tela_final_nomeia_o_personagem_sem_proxima_cena()
    {
        var vm = await Abrir();
        vm.StartReviewOf(ReviewSource.OfSpeaker("美咲"));
        var revisao = vm.Review!;

        for (var i = 0; i < 4; i++)
        {
            revisao.ApproveCommand.Execute(null);
        }

        Assert.True(revisao.IsFinished);
        Assert.Equal(4, revisao.ApprovedCount);
        Assert.Equal(TranslationStatus.Approved, Gravada("yst00001", 0).Status);
        Assert.Equal(TranslationStatus.Approved, Gravada("yst00001", 3).Status);
        Assert.Equal(TranslationStatus.Approved, Gravada("yst00003", 0).Status);
        Assert.Equal(TranslationStatus.Approved, Gravada("yst00003", 1).Status);
        Assert.Equal(TranslationStatus.Machine, Gravada("yst00001", 1).Status);
        Assert.Contains("Misaki", revisao.FinishedTitle, StringComparison.Ordinal);
        Assert.Equal("4 linhas aprovadas.", revisao.FinishedText);
        Assert.False(revisao.HasNextScene);
        Assert.Null(revisao.NextSceneName);
    }

    [Fact]
    public async Task Voltar_na_primeira_linha_de_uma_cena_reabre_a_ultima_da_cena_anterior()
    {
        var vm = await Abrir();
        vm.StartReviewOf(ReviewSource.OfSpeaker("美咲"));
        var revisao = vm.Review!;
        revisao.SkipCommand.Execute(null);
        revisao.SkipCommand.Execute(null);
        Assert.Equal("yst00003", vm.SelectedScript!.Key);

        revisao.BackCommand.Execute(null);

        Assert.Equal("yst00001", vm.SelectedScript.Key);
        Assert.Equal("yst00001:000003", revisao.Detail!.Id);
        Assert.Equal("Linha 2 de 4", revisao.PositionText);
    }

    [Fact]
    public async Task Personagem_sem_linhas_da_IA_abre_a_tela_final_vazia()
    {
        var vm = await Abrir();

        vm.StartReviewOf(ReviewSource.OfSpeaker("ninguém"));

        var revisao = vm.Review!;
        Assert.True(revisao.IsFinished);
        Assert.Equal(0, revisao.Total);
        Assert.Contains("ninguém", revisao.FinishedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_revisao_da_cena_continua_igual_e_oferece_a_proxima_cena()
    {
        var vm = await Abrir();

        vm.StartReview();

        Assert.Equal(3, vm.Review!.Total);
        Assert.Equal("Revisão da cena concluída", vm.Review.FinishedTitle);
        Assert.True(vm.Review.HasNextScene);
    }

    [Fact]
    public async Task O_detalhe_da_linha_inicia_a_revisao_do_falante_dela()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines.First(l => l.Id == "yst00001:000001");

        vm.Detail!.ReviewSpeakerCommand.Execute(null);

        Assert.True(vm.IsReviewing);
        Assert.Equal("yst00001:000001", vm.Review!.Detail!.Id);
        Assert.Equal(2, vm.Review.Total);
    }

    [Fact]
    public async Task Linha_com_falante_trocado_entra_na_fila_de_quem_foi_escolhido_e_sai_da_do_falante_do_jogo()
    {
        ProjectFolder.Open(raiz).SaveText("yst00002",
        [
            Fala(2, 0, "健太", "Sim.\\p", TranslationStatus.Machine),
            Fala(2, 1, "健太", "Isso.\\p", TranslationStatus.Machine) with { ChosenSpeaker = "美咲" },
        ]);
        var vm = await Abrir();

        vm.StartReviewOf(ReviewSource.OfSpeaker("美咲"));
        var misaki = new List<string> { vm.Review!.Detail!.Id };
        for (var i = 1; i < vm.Review.Total; i++)
        {
            vm.Review.SkipCommand.Execute(null);
            misaki.Add(vm.Review.Detail!.Id);
        }

        Assert.Equal(["yst00001:000000", "yst00001:000003", "yst00002:000001", "yst00003:000000", "yst00003:000001"], misaki);
        vm.StartReviewOf(ReviewSource.OfSpeaker("健太"));
        Assert.Equal(2, vm.Review!.Total);
        Assert.Equal(("健太", "Kenta", 2), (vm.ReviewableSpeakers[1].Source, vm.ReviewableSpeakers[1].Name, vm.ReviewableSpeakers[1].Count));
        Assert.Equal(5, vm.ReviewableSpeakers[0].Count);
    }

    [Fact]
    public async Task A_lista_de_personagens_acompanha_uma_aprovacao()
    {
        var vm = await Abrir();
        Assert.Equal(4, vm.ReviewableSpeakers[0].Count);

        vm.StartReviewOf(ReviewSource.OfSpeaker("美咲"));
        vm.Review!.ApproveCommand.Execute(null);

        Assert.Equal(3, vm.ReviewableSpeakers.Single(s => s.Source == "美咲").Count);
    }

    [Fact]
    public void Uma_fonte_de_personagem_exige_o_nome_e_o_tipo_tem_de_existir()
    {
        Assert.Throws<ArgumentException>(() => new ReviewSource(ReviewSourceKind.Speaker, null));
        Assert.Throws<ArgumentException>(() => new ReviewSource(ReviewSourceKind.Speaker, " "));
        Assert.Throws<ArgumentException>(() => new ReviewSource((ReviewSourceKind)99));
    }

    [Fact]
    public void A_fonte_diz_a_legenda_o_titulo_final_e_o_texto_vazio()
    {
        var cena = ReviewSource.OpenScene;
        var personagem = ReviewSource.OfSpeaker("美咲");

        Assert.False(cena.SpansScenes);
        Assert.Null(cena.Caption("Misaki"));
        Assert.Equal("Revisão da cena concluída", cena.FinishedTitle("Misaki"));
        Assert.Equal("Esta cena não tem linhas traduzidas pela IA esperando revisão.", cena.EmptyText("Misaki"));
        Assert.True(personagem.SpansScenes);
        Assert.Equal("Falas de Misaki em todas as cenas", personagem.Caption("Misaki"));
        Assert.Equal("Revisão das falas de Misaki concluída", personagem.FinishedTitle("Misaki"));
        Assert.Equal("Misaki não tem falas traduzidas pela IA esperando revisão.", personagem.EmptyText("Misaki"));
    }

    [Fact]
    public async Task Os_personagens_para_revisar_trazem_o_nome_traduzido_e_quantas_linhas_da_IA_tem()
    {
        var vm = await Abrir();

        var speakers = vm.ReviewableSpeakers;

        Assert.Equal([("美咲", "Misaki", 4), ("健太", "Kenta", 2)], speakers.Select(s => (s.Source, s.Name, s.Count)));
    }
}
