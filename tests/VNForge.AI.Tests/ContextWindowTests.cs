using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

public sealed partial class ContextWindowTests
{
    private static readonly ExchangeRate Rate = new(5m, new DateOnly(2026, 10, 25));

    [System.Text.RegularExpressions.GeneratedRegex("\"id\":\"([^\"]+)\"")]
    private static partial System.Text.RegularExpressions.Regex RequestId();

    private sealed class StubClient : IClaudeMessageClient
    {
        public List<ClaudeRequest> Requests { get; } = [];

        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var ids = RequestId().Matches(request.User).Select(m => m.Groups[1].Value).Distinct();
            var items = string.Join(",", ids.Select(id => $"{{\"id\":\"{id}\",\"translation\":\"T-{id}\"}}"));
            return Task.FromResult(new ClaudeReply("end_turn", $"{{\"translations\":[{items}]}}"));
        }
    }

    // 20 falas já traduzidas, 3 pendentes (20 a 22) e 20 seguintes sem tradução.
    private static TextUnit[] Script() =>
    [
        .. Enumerable.Range(0, 43).Select(i => new TextUnit(
            $"s:{i:D3}", TextUnitKind.Dialogue, "ハル", $"原文{i:D3}", i < 20 ? $"Tradução {i:D3}" : null,
            i < 20 ? TranslationStatus.Translated : TranslationStatus.Untranslated)),
    ];

    private static LineSelection SelectPending(TextUnit[] script) =>
        LineSelection.Build(
            new Dictionary<string, IReadOnlyList<TextUnit>> { ["s"] = script },
            null,
            null,
            u => string.CompareOrdinal(u.Id, "s:020") >= 0 && string.CompareOrdinal(u.Id, "s:022") <= 0);

    private static TranslationBatchBuilder Builder(int before, int after) =>
        new([], new CharacterBook([]), maxPreviousLines: before, maxNextLines: after);
}
