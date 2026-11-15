using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Review;
using VNForge.AI.Translation;
using VNForge.Core.Bible;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

/// <summary>A segunda passada de editor (#532): o provedor Claude, o provedor falso, o corredor que confere os ajustes e a estimativa de custo.</summary>
[Trait("Risco", "R5")]
public class EditorPassTests
{
    private sealed class StubClient(Func<ClaudeRequest, ClaudeReply> reply) : IClaudeMessageClient
    {
        public List<ClaudeRequest> Requests { get; } = [];

        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(reply(request));
        }
    }

    private sealed class ScriptedProvider(Func<TranslationBatch, IReadOnlyList<EditorPassProposal>> reply, Func<TranslationBatch, string, EditorPassProposal?>? another = null) : IEditorPassProvider
    {
        public List<TranslationBatch> Scenes { get; } = [];

        public List<(TranslationBatch Line, string Rejected)> Anothers { get; } = [];

        public Task<EditorPassProposal?> AnotherAsync(TranslationBatch line, string rejected, CancellationToken ct)
        {
            Anothers.Add((line, rejected));
            return Task.FromResult(another?.Invoke(line, rejected));
        }

        public Task<IReadOnlyList<EditorPassProposal>> ReviewSceneAsync(TranslationBatch scene, CancellationToken ct)
        {
            Scenes.Add(scene);
            return Task.FromResult(reply(scene));
        }
    }

    private static TextUnit Unit(string id, string source, string? translation, TranslationStatus status = TranslationStatus.Machine) =>
        new(id, TextUnitKind.Dialogue, "健太", source, translation, status);

    private static ClaudeReply Ok(string json) => new("end_turn", json);

    private static TranslationBatch Scene(params TextUnit[] lines) => new(lines);

    private static TranslationBatchBuilder Builder(GameBible? bible = null) =>
        new([], new CharacterBook([]), maxPreviousLines: 3) { Bible = bible! };

    // ---- ClaudeEditorPassProvider ----

    [Fact]
    public async Task Os_tokens_de_cada_chamada_somam_no_uso_do_provedor()
    {
        var client = new StubClient(_ => new ClaudeReply("end_turn", """{"adjustments":[]}""", new ClaudeUsage(100, 20, 0, 0)));
        var provider = new ClaudeEditorPassProvider(client);

        await provider.ReviewSceneAsync(Scene(Unit("y:1", "a", "a")), TestContext.Current.CancellationToken);
        await provider.ReviewSceneAsync(Scene(Unit("y:1", "a", "a")), TestContext.Current.CancellationToken);

        Assert.Equal(200, provider.Usage.InputTokens);
        Assert.Equal(40, provider.Usage.OutputTokens);
    }

    // ---- FakeEditorPassProvider ----

    // ---- EditorPassRunner ----

    // ---- "Outra" ----

    // ---- EditorPassCost ----

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
