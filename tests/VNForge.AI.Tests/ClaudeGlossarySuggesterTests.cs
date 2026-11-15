using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Glossary;
using VNForge.Core.Glossary;

namespace VNForge.AI.Tests;

[Trait("Risco", "R5")]
public class ClaudeGlossarySuggesterTests
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

    private static ClaudeReply Ok(string json) => new("end_turn", json);

    private static readonly CharacterVoiceRequest[] Voices =
    [
        new("美咲", ["「先輩、遅刻です」", "えへへ、ありがとう！"]),
        new("健太", ["おい、行くぞ"]),
    ];

    public static TheoryData<string?, string> Falhas => new()
    {
        { "refusal", "" },
        { "max_tokens", """{"characters":[""" },
        { "end_turn", "isto não é json" },
        { "end_turn", """{"characters":[{"name":"他人","gender":"male","voice":"z"}]}""" },
        { "end_turn", """{"characters":[{"name":"美咲","gender":"robô","voice":"x"},{"name":"健太","gender":"male","voice":"y"}]}""" },
        { "end_turn", """{"characters":[{"name":"美咲","gender":"female","voice":"x"},{"name":"美咲","gender":"female","voice":"x"}]}""" },
    };

    [Fact]
    public async Task Soma_os_tokens_de_todas_as_chamadas()
    {
        var client = new StubClient(_ => new ClaudeReply("end_turn", """{"terms":[{"source":"魔法","target":"magia","reason":"r"}]}""", new ClaudeUsage(100, 20, 5, 0)));
        var suggester = new ClaudeGlossarySuggester(client);

        await suggester.SuggestTermsAsync([new TermTranslationRequest("魔法", 3)], CancellationToken.None);
        await suggester.SuggestTermsAsync([new TermTranslationRequest("魔法", 3)], CancellationToken.None);

        Assert.Equal(new ClaudeUsage(200, 40, 10, 0), suggester.Usage);
    }
}
