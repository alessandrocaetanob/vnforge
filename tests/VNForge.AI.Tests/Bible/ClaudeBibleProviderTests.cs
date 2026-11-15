using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using VNForge.AI.Bible;
using VNForge.AI.Claude;
using static VNForge.AI.Tests.Bible.BibleFixtures;

namespace VNForge.AI.Tests.Bible;

[Trait("Risco", "R5")]
public sealed class ClaudeBibleProviderTests
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

    private static readonly string[] SceneCharacters = ["Kenta", "Ren"];
    private static readonly string[] SceneTone = ["cômico leve"];
    private static readonly string[] SceneJokes = ["o guarda-chuva"];

    private static string SceneJson(string key, string summary = "Kenta e Ren chegam à escola.") =>
        JsonSerializer.Serialize(new
        {
            key,
            name = "Atraso no portão",
            summary,
            characters = SceneCharacters,
            tone = SceneTone,
            jokes = SceneJokes,
            relations = new[] { new { between = "Kenta e Ren", note = "Amigos de infância" } },
        });

    private static ClaudeReply Scenes(params string[] keys) =>
        new("end_turn", $$"""{"scenes":[{{string.Join(",", keys.Select(k => SceneJson(k)))}}]}""", new ClaudeUsage(100, 50, 400, 0));

    private static BibleChapterInput TwoSceneChapter() =>
        Chapter("Manhã de chuva", 1, Scene("yst1", "manha", "おはよう", "遅いぞ"), Scene("yst2", "tarde", "ただいま"));

    [Theory]
    [InlineData("refusal", "")]
    [InlineData("max_tokens", "{")]
    public async Task Chamada_que_falha_ja_foi_cobrada_e_soma_no_uso(string stopReason, string text)
    {
        var provider = new ClaudeBibleProvider(new StubClient(_ => new ClaudeReply(stopReason, text, new ClaudeUsage(100, 50, 400, 20))));

        await Assert.ThrowsAnyAsync<Exception>(() => provider.DescribeScenesAsync(TwoSceneChapter(), ["yst1"], TestContext.Current.CancellationToken));

        Assert.Equal(new ClaudeUsage(100, 50, 400, 20), provider.Usage);
    }
}
