using VNForge.Core.Eval;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Eval;

internal static class EvalFixtures
{
    public static EvalConfig Config(string id, string model = "claude-sonnet-5-5", string effort = "medium", int context = 15, bool bible = true) =>
        new(id, model, effort, context, bible);

    public static IReadOnlyList<EvalLineResult> Lines(double chrf, params string[] ids) =>
        [.. ids.Select(id => new EvalLineResult(id, $"texto {id} {chrf}", chrf, 8.0))];

    public static EvalConfigReport Report(EvalConfig config, double chrf, decimal dollars, double seconds = 60, params string[] ids) =>
        new(config, Lines(chrf, ids.Length == 0 ? ["l1", "l2"] : ids), 1000, 500, 0, 0, dollars, 0m, seconds);

    public static TextUnit Unit(string id, TranslationStatus status, string source = "原文", string? translation = null, string? speaker = null) =>
        new(id, TextUnitKind.Dialogue, speaker, source, translation, status);

    /// <summary>As cinco configurações do quadro, com números coerentes entre si (A é a mais cara e a de maior chrF, C chega perto por metade do preço).</summary>
    public static EvalRound Board()
    {
        var a = Config("A", "claude-opus-5-5", "high");
        var b = Config("B", "claude-opus-5-5");
        var c = Config("C");
        var d = Config("D", context: 2, bible: false);
        var e = Config("E", "claude-haiku-5-5", "low", bible: false);
        EvalConfigReport[] reports =
        [
            Report(a, 61.4, 2.0m),
            Report(b, 60.9, 1.6m),
            Report(c, 59.8, 1.0m),
            Report(d, 54.3, 0.9m),
            Report(e, 47.0, 0.05m),
        ];
        return new EvalRound(
            EvalRound.CurrentVersion, "20261108-120000", new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero), 7, 1240, 18320, "claude-sonnet-5-5",
            [], reports, [], []);
    }
}
