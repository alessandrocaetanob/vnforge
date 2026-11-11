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
