using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O modo revisão na ordem "o que mais precisa de você": a fila de risco do projeto inteiro, o cartão da linha, as horas e a troca de ordem.</summary>
[Trait("Risco", "R6")]
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
            Fala(1, 1, "健太", "Oi.", TranslationStatus.Machine, duvida: true, estranho: true),
            Fala(1, 2, "美咲", "Aham.\\p", TranslationStatus.Approved),
            Fala(1, 3, "健太", "Ei.\\p", TranslationStatus.Machine),
        ]);
        projeto.SaveText("yst00002",
        [
            Fala(2, 0, "美咲", "Sim.\\p", TranslationStatus.Machine, duvida: true),
            Fala(2, 1, "美咲", null, TranslationStatus.Untranslated),
        ]);
        projeto.SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "animada"), new CharacterEntry("健太", "Kenta", CharacterGender.Male)]);
        GravarSugestao("yst00001:000001", "Oi.");
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

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    private TextUnit Gravada(string script, int n) => ProjectFolder.Open(raiz).LoadText(script)[n];

    [Fact]
    public async Task Esta_boa_atravessa_as_cenas_grava_cada_linha_e_a_tela_final_nao_oferece_a_proxima_cena()
    {
        var vm = await Abrir();
        await vm.StartRiskReviewAsync();
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

}