using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Sheet;
using VNForge.Core.Factsheet;

namespace VNForge.AI.Tests.Sheet;

[Trait("Risco", "R5")]
public sealed class SheetTranslatorTests
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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SheetTranslationRequest Request() =>
        new("Amaoto no Hakoniwa",
        [
            new SheetTranslationItem("synopsis", SheetItemKind.Synopsis, "A rainy town. [spoiler]He dies.[/spoiler]", null),
            new SheetTranslationItem("tag:g10", SheetItemKind.TagName, "Comedy", "cont"),
            new SheetTranslationItem("trait:i1", SheetItemKind.TraitName, "Long Hair", "Hair"),
        ]);

    private static ClaudeReply Reply(string json, ClaudeUsage? usage = null) => new("end_turn", json, usage ?? new ClaudeUsage(100, 50, 400, 0));

    [Fact]
    public async Task Recusa_e_corte_por_tamanho_viram_erros_proprios_e_a_chamada_ja_cobrada_soma_no_uso()
    {
        var refusing = new ClaudeSheetTranslator(new StubClient(_ => new ClaudeReply("refusal", "", new ClaudeUsage(10, 5, 0, 0))));
        var truncated = new ClaudeSheetTranslator(new StubClient(_ => new ClaudeReply("max_tokens", "{", new ClaudeUsage(10, 5, 0, 0))));

        var refused = await Assert.ThrowsAsync<ClaudeRefusalException>(() => refusing.TranslateAsync(Request(), Ct));
        var cut = await Assert.ThrowsAsync<ClaudeResponseException>(() => truncated.TranslateAsync(Request(), Ct));

        Assert.Contains("não quis traduzir", refused.Message, StringComparison.Ordinal);
        Assert.True(cut.HitMaxTokens);
        Assert.Equal(new ClaudeUsage(10, 5, 0, 0), refusing.Usage);
    }
}
