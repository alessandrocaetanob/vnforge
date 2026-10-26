using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>Uma linha cujas ocorrências do QA mudaram numa releitura: a entrada como estava e as ocorrências que ela passa a ter.</summary>
public sealed record LineIssueChange(LineEntry Original, IReadOnlyList<QaIssue> Issues);

/// <summary>O que uma releitura do QA achou de diferente nas linhas; <see cref="ProjectLines.Apply"/> leva isso para as linhas.</summary>
public sealed record RecheckResult(IReadOnlyList<LineIssueChange> Changes)
{
    /// <summary>Resultado sem mudança.</summary>
    public static RecheckResult None { get; } = new([]);
}

/// <summary>
/// Um instantâneo das linhas carregadas e do contexto do QA, para refazer as regras fora da thread da tela. Não lê nem escreve em
/// <see cref="ProjectLines"/>: <see cref="Run"/> só olha o instantâneo, e <see cref="ProjectLines.Apply"/> leva o resultado de volta.
/// </summary>
public sealed class LineRecheck
{
    private readonly QaEngine? engine;
    private readonly QaContext? context;
    private readonly IReadOnlyList<LineEntry> entries;

    internal LineRecheck(QaEngine? engine, QaContext? context, IReadOnlyList<LineEntry> entries)
    {
        this.engine = engine;
        this.context = context;
        this.entries = entries;
    }

    /// <summary>Roda as regras sobre o instantâneo e devolve só as linhas cujas ocorrências mudaram.</summary>
    public RecheckResult Run()
    {
        if (engine is null || context is null)
        {
            return RecheckResult.None;
        }

        var fresh = new Dictionary<(string Script, string Unit), List<QaIssue>>();
        foreach (var issue in engine.Run(context).Issues)
        {
            if (!fresh.TryGetValue((issue.ScriptKey, issue.UnitId), out var list))
            {
                fresh[(issue.ScriptKey, issue.UnitId)] = list = [];
            }

            list.Add(issue);
        }

        var covered = engine.Rules.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var changes = new List<LineIssueChange>();
        foreach (var entry in entries)
        {
            var found = fresh.TryGetValue((entry.ScriptKey, entry.Unit.Id), out var list) ? list : [];
            IReadOnlyList<QaIssue> next = [.. entry.Issues.Where(i => !covered.Contains(i.RuleId)), .. found];
            if (!SameIssues(entry.Issues, next))
            {
                changes.Add(new LineIssueChange(entry, next));
            }
        }

        return new RecheckResult(changes);
    }

    private static bool SameIssues(IReadOnlyList<QaIssue> before, IReadOnlyList<QaIssue> after) =>
        before.Count == after.Count && Sorted(before).SequenceEqual(Sorted(after));

    private static IEnumerable<QaIssue> Sorted(IReadOnlyList<QaIssue> issues) =>
        issues.OrderBy(i => i.RuleId, StringComparer.Ordinal).ThenBy(i => i.Message, StringComparer.Ordinal);
}
