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
}
