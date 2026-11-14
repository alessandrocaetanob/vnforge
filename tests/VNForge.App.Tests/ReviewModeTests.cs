using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O modo revisão: a fila de linhas da IA, aprovar, deixar para depois, pular, a tela final e o registro de aprovações com hora.</summary>
[Trait("Risco", "R2")]
[Trait("Risco", "R6")]
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

}
