using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Sheet;
using VNForge.Core.Factsheet;

namespace VNForge.AI.Tests.Sheet;

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
    public async Task Traduz_os_itens_e_devolve_cada_um_pela_chave()
    {
        var client = new StubClient(_ => Reply("""{"items":[{"key":"synopsis","text":"Uma cidade chuvosa. [spoiler]Ele morre.[/spoiler]"},{"key":"tag:g10","text":"Comédia"},{"key":"trait:i1","text":"Cabelo longo"}]}"""));

        var result = await new ClaudeSheetTranslator(client).TranslateAsync(Request(), Ct);

        Assert.Equal(["synopsis", "tag:g10", "trait:i1"], result.Select(t => t.Key));
        Assert.Equal("Uma cidade chuvosa. [spoiler]Ele morre.[/spoiler]", result[0].Text);
        Assert.Equal("Comédia", result[1].Text);
    }

    [Fact]
    public async Task As_regras_vao_no_bloco_estavel_e_os_itens_na_mensagem_do_usuario()
    {
        var client = new StubClient(_ => Reply("""{"items":[]}"""));

        await new ClaudeSheetTranslator(client).TranslateAsync(Request(), Ct);

        var request = Assert.Single(client.Requests);
        Assert.Equal("claude-opus-5-5", request.Model);
        Assert.Contains("[spoiler]", request.System, StringComparison.Ordinal);
        Assert.Contains("português do Brasil", request.System, StringComparison.Ordinal);
        Assert.DoesNotContain("A rainy town", request.System, StringComparison.Ordinal);
        using var user = JsonDocument.Parse(request.User[(request.User.IndexOf('\n', request.User.IndexOf('\n') + 1) + 1)..]);
        var items = user.RootElement.EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal(("synopsis", "synopsis", "A rainy town. [spoiler]He dies.[/spoiler]"), (items[0].GetProperty("key").GetString(), items[0].GetProperty("kind").GetString(), items[0].GetProperty("text").GetString()));
        Assert.Equal(("tag_name", "cont"), (items[1].GetProperty("kind").GetString(), items[1].GetProperty("context").GetString()));
        Assert.Contains("Amaoto no Hakoniwa", request.User, StringComparison.Ordinal);
        Assert.Contains("\"items\"", request.JsonSchema, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_mesmo_pedido_manda_sempre_o_mesmo_bloco_de_sistema_para_o_cache_acertar()
    {
        var client = new StubClient(_ => Reply("""{"items":[]}"""));
        var translator = new ClaudeSheetTranslator(client);

        await translator.TranslateAsync(Request(), Ct);
        await translator.TranslateAsync(new SheetTranslationRequest("Outro", [new SheetTranslationItem("tag:g1", SheetItemKind.TagName, "Drama", "cont")]), Ct);

        Assert.Equal(client.Requests[0].System, client.Requests[1].System);
    }

    [Fact]
    public async Task Soma_o_uso_de_todas_as_chamadas_com_o_que_veio_do_cache()
    {
        var translator = new ClaudeSheetTranslator(new StubClient(_ => Reply("""{"items":[]}""")));

        await translator.TranslateAsync(Request(), Ct);
        await translator.TranslateAsync(Request(), Ct);

        Assert.Equal(new ClaudeUsage(200, 100, 800, 0), translator.Usage);
    }

    [Fact]
    public async Task Respeita_o_modelo_e_o_esforco_pedidos()
    {
        var client = new StubClient(_ => Reply("""{"items":[]}"""));
        var translator = new ClaudeSheetTranslator(client, new ClaudeOptions { Model = ClaudeModels.Haiku, Effort = ClaudeEffort.Low });

        await translator.TranslateAsync(Request(), Ct);

        Assert.Equal((ClaudeModels.Haiku, ClaudeEffort.Low), (client.Requests[0].Model, client.Requests[0].Effort));
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("""{"itens":[]}""")]
    [InlineData("""{"items":[{"key":"synopsis"}]}""")]
    [InlineData("""{"items":[{"key":null,"text":"x"}]}""")]
    public async Task Resposta_fora_do_formato_vira_erro_de_resposta_em_portugues(string json)
    {
        var translator = new ClaudeSheetTranslator(new StubClient(_ => Reply(json)));

        var ex = await Assert.ThrowsAsync<ClaudeResponseException>(() => translator.TranslateAsync(Request(), Ct));

        Assert.Contains("incompleta ou fora do formato", ex.Message, StringComparison.Ordinal);
    }

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

    [Fact]
    public async Task O_provedor_falso_traduz_sem_rede_mantendo_os_marcadores_e_guarda_os_pedidos()
    {
        var fake = new FakeSheetTranslator();

        var result = await fake.TranslateAsync(Request(), Ct);

        Assert.Equal("[pt-BR] A rainy town. [spoiler]He dies.[/spoiler]", result[0].Text);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task O_provedor_falso_pode_esquecer_um_item_ou_falhar_como_a_IA_real()
    {
        var forgetting = new FakeSheetTranslator(i => i.Kind == SheetItemKind.TagName ? null : "x");
        var failing = new FakeSheetTranslator(_ => throw new InvalidOperationException("falhou"));

        Assert.Equal(["synopsis", "trait:i1"], (await forgetting.TranslateAsync(Request(), Ct)).Select(t => t.Key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.TranslateAsync(Request(), Ct));
    }

    [Fact]
    public void A_estimativa_conta_as_chamadas_e_cresce_com_o_texto()
    {
        var small = new SheetTranslationPlan("J", [new("tag:g1", SheetItemKind.TagName, "Comedy", "cont")]);
        var big = new SheetTranslationPlan("J", [.. Enumerable.Range(0, 20).Select(i => new SheetTranslationItem($"character:c{i}", SheetItemKind.CharacterDescription, new string('a', 3000), "Nome"))]);

        var low = SheetCost.Estimate(small, ClaudeModels.Opus);
        var high = SheetCost.Estimate(big, ClaudeModels.Opus);

        Assert.Equal((1, 1), (low.Items, low.Calls));
        Assert.Equal((20, 10), (high.Items, high.Calls));
        Assert.True(high.Dollars > low.Dollars && low.Dollars > 0);
        Assert.True(SheetCost.Estimate(big, ClaudeModels.Haiku).Dollars < high.Dollars);
    }
}
