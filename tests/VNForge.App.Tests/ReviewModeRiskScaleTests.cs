using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A fila por risco de um jogo grande: montar e ler os arquivos acontece fora da thread da tela, que não espera.</summary>
public sealed class ReviewModeRiskScaleTests : IDisposable
{
    private const int Scripts = 10;
    private const int LinesPerScript = 5_000;

    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 25, 15, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public ReviewModeRiskScaleTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Grande");
        var projeto = ProjectFolder.Open(raiz);
        for (var s = 1; s <= Scripts; s++)
        {
            var key = $"yst{s:D5}";
            projeto.SaveText(
                key,
                [.. Enumerable.Range(0, LinesPerScript).Select(n => new TextUnit($"{key}:{n:D6}", TextUnitKind.Dialogue, "美咲", "「原文」\\p", "Texto.\\p", TranslationStatus.Machine)
                {
                    Doubt = n % 50 == 0 ? new TranslationDoubt(DoubtKind.Tone, "Tom?") : null,
                })]);
        }
    }

    public void Dispose() => pasta.Dispose();

    [Fact]
    public async Task Com_50_mil_linhas_a_tela_nao_espera_a_leitura_do_ritmo_nem_a_pontuacao_e_a_fila_abre_quando_a_conta_termina()
    {
        using var gate = new ManualResetEventSlim();
        var log = new BlockingLog(gate);
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao()) { ApprovalLogFactory = _ => log };
        vm.Project = EditorFixtures.Summary("Grande", raiz);
        await vm.Loading;
        Assert.Equal(Scripts * LinesPerScript, vm.Scripts.Sum(s => s.Progress.Counts.Total));

        // A leitura do ritmo (sessions.json) espera o portão: se rodasse na thread da tela, esta chamada nunca voltaria.
        var starting = vm.StartRiskReviewAsync();

        Assert.False(starting.IsCompleted, "a fila por risco ainda está sendo montada");
        Assert.False(vm.IsReviewing);
        Assert.True(log.Entered.Wait(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken), "a conta não começou fora da thread da tela");
        Assert.False(vm.IsReviewing);

        gate.Set();
        await starting.WaitAsync(TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);

        Assert.True(vm.IsReviewing);
        Assert.Equal(Scripts * LinesPerScript, vm.Review!.Total);
        Assert.StartsWith("Fila por risco", vm.Review.HeaderTitle, StringComparison.Ordinal);
        Assert.Equal(5, vm.Review.UpNext.Count);
        Assert.Empty(vm.Review.VisibleBlocks);
    }

    private sealed class BlockingLog(ManualResetEventSlim gate) : IApprovalLog
    {
        public ManualResetEventSlim Entered { get; } = new();

        public void Record(string scriptKey, string unitId, DateTimeOffset at)
        {
        }

        public void Remove(string scriptKey, string unitId)
        {
        }

        public int ReviewedOn(DateTimeOffset now, TimeZoneInfo zone) => 0;

        public ReviewPace PaceOf(int linesToReview, DateTimeOffset now, TimeZoneInfo zone)
        {
            Entered.Set();
            gate.Wait(TimeSpan.FromMinutes(5));
            return ReviewPace.None;
        }
    }
}
