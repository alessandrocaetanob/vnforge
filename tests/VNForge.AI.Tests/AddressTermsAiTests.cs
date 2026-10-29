using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Glossary;
using VNForge.AI.Translation;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

public class AddressTermsAiTests
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

    private static readonly AddressTermRequest[] Speakers =
    [
        new("健太", [new AddressLine("美咲ちゃん、おはよう", "凛")]),
        new("美咲", [new AddressLine("先輩、遅いです", null)]),
    ];

    private static readonly AddressTermCharacter[] Roster =
    [
        new("健太", "Kenta"),
        new("美咲", "Misaki"),
        new("凛"),
    ];

    [Fact]
    public async Task Devolve_os_pares_da_resposta_para_o_haiku_com_o_schema_estrito()
    {
        var client = new StubClient(_ => Ok("""{"pairs":[{"from":"健太","to":"美咲","source":"美咲ちゃん","target":"Misaki-chan"},{"from":"美咲","to":"健太","source":"先輩","target":"senpai"}]}"""));
        var suggester = new ClaudeGlossarySuggester(client);

        var result = await suggester.SuggestAddressTermsAsync(Speakers, Roster, CancellationToken.None);

        Assert.Equal(
            [new AddressTermSuggestion("健太", "美咲", "美咲ちゃん", "Misaki-chan"), new AddressTermSuggestion("美咲", "健太", "先輩", "senpai")],
            result);
        var request = Assert.Single(client.Requests);
        Assert.Equal("claude-haiku-5-5", request.Model);
        Assert.Contains("美咲ちゃん、おはよう", request.User);
        Assert.Contains("\"凛\"", request.User);
        Assert.Contains("Kenta", request.User);

        using var schema = JsonDocument.Parse(request.JsonSchema);
        var item = schema.RootElement.GetProperty("properties").GetProperty("pairs").GetProperty("items");
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.False(item.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["from", "to", "source", "target"], item.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Descarta_o_que_nao_vale_e_fica_com_o_primeiro_de_cada_par()
    {
        var client = new StubClient(_ => Ok("""
            {"pairs":[
              {"from":"健太","to":"健太","source":"俺","target":"eu"},
              {"from":"ある人","to":"美咲","source":"君","target":"você"},
              {"from":"健太","to":"誰か","source":"あいつ","target":"ele"},
              {"from":"健太","to":"美咲","source":"  ","target":"Misaki"},
              {"from":" 健太","to":"美咲","source":" 美咲ちゃん ","target":" Misaki-chan "},
              {"from":"健太","to":"美咲","source":"美咲","target":"Misaki"}
            ]}
            """));
        var suggester = new ClaudeGlossarySuggester(client);

        var result = await suggester.SuggestAddressTermsAsync(Speakers, Roster, CancellationToken.None);

        Assert.Equal([new AddressTermSuggestion("健太", "美咲", "美咲ちゃん", "Misaki-chan")], result);
    }

    [Fact]
    public async Task Sem_falantes_nao_chama_a_api_e_muitos_falantes_viram_varias_chamadas()
    {
        var client = new StubClient(_ => Ok("""{"pairs":[]}"""));
        var suggester = new ClaudeGlossarySuggester(client);

        Assert.Empty(await suggester.SuggestAddressTermsAsync([], Roster, CancellationToken.None));
        Assert.Empty(client.Requests);

        var many = Enumerable.Range(0, ClaudeGlossarySuggester.AddressChunk + 2).Select(i => new AddressTermRequest($"p{i}", [new AddressLine("あ", null)])).ToArray();
        await suggester.SuggestAddressTermsAsync(many, Roster, CancellationToken.None);

        Assert.Equal(2, client.Requests.Count);
    }

    [Theory]
    [InlineData("""{"pairs":[{"from":"健太","to":"美咲","source":"x"}]}""")]
    [InlineData("""{"outro":[]}""")]
    [InlineData("não é json")]
    public async Task Resposta_fora_do_formato_vira_ClaudeResponseException(string json)
    {
        var suggester = new ClaudeGlossarySuggester(new StubClient(_ => Ok(json)));

        await Assert.ThrowsAsync<ClaudeResponseException>(() => suggester.SuggestAddressTermsAsync(Speakers, Roster, CancellationToken.None));
    }

    [Fact]
    public async Task Recusa_e_corte_por_tamanho_viram_excecao_com_a_frase_em_portugues()
    {
        var refused = new ClaudeGlossarySuggester(new StubClient(_ => new ClaudeReply("refusal", "")));
        var cut = new ClaudeGlossarySuggester(new StubClient(_ => new ClaudeReply("max_tokens", "{")));

        await Assert.ThrowsAsync<ClaudeRefusalException>(() => refused.SuggestAddressTermsAsync(Speakers, Roster, CancellationToken.None));
        await Assert.ThrowsAsync<ClaudeResponseException>(() => cut.SuggestAddressTermsAsync(Speakers, Roster, CancellationToken.None));
    }

    [Fact]
    public async Task O_provedor_falso_devolve_o_que_foi_combinado_e_guarda_o_pedido()
    {
        var fake = new FakeGlossarySuggester(address: (speakers, _) => [new AddressTermSuggestion(speakers[0].Speaker, "美咲", "美咲", "Misaki")]);

        var result = await fake.SuggestAddressTermsAsync(Speakers, Roster, CancellationToken.None);

        Assert.Equal("健太", Assert.Single(result).From);
        Assert.Single(fake.ReceivedAddress);
        Assert.Empty(await new FakeGlossarySuggester().SuggestAddressTermsAsync(Speakers, Roster, CancellationToken.None));
    }

    [Fact]
    public void O_preco_cresce_com_as_falas_e_e_zero_sem_pedido()
    {
        var few = GlossaryCost.AddressTerms(Speakers[..1], Roster, ClaudeModels.Haiku);
        var more = GlossaryCost.AddressTerms(Speakers, Roster, ClaudeModels.Haiku);

        Assert.True(more.Dollars > few.Dollars);
        Assert.Equal(0m, GlossaryCost.AddressTerms([], Roster, ClaudeModels.Haiku).Dollars);
    }

    // ---- o tratamento no pedido de tradução ----

    private static TextUnit Unit(string id, string source, string? speaker, string? translation = null) =>
        new(id, TextUnitKind.Dialogue, speaker, source, translation,
            translation is null ? TranslationStatus.Untranslated : TranslationStatus.Translated);

    private static readonly CharacterBook Cast = new(
    [
        new CharacterEntry("健太", "Kenta"),
        new CharacterEntry("美咲", "Misaki"),
        new CharacterEntry("凛", ""),
    ]);

    private static readonly AddressTerm[] Terms =
    [
        new("健太", "美咲", "美咲ちゃん", "Misaki-chan"),
        new("美咲", "健太", "先輩", "senpai"),
        new("健太", "凛", "凛", "Rin"),
        new("健太", "拓也", "拓也", " "),
    ];

    [Fact]
    public void O_lote_leva_so_os_tratamentos_de_quem_fala_nele_com_os_nomes_traduzidos()
    {
        var builder = new TranslationBatchBuilder([], Cast, addressTerms: Terms);

        var batch = builder.Build([Unit("a", "美咲ちゃん、おはよう", "健太")]);

        Assert.Equal(
            [new AddressInfo("Kenta", "Misaki", "美咲ちゃん", "Misaki-chan"), new AddressInfo("Kenta", "凛", "凛", "Rin")],
            batch.Addressing);
    }

    [Fact]
    public void Sem_tratamentos_o_lote_nao_traz_nada_e_o_falante_sem_cadastro_tambem_nao()
    {
        Assert.Empty(new TranslationBatchBuilder([], Cast).Build([Unit("a", "x", "健太")]).Addressing);
        Assert.Empty(new TranslationBatchBuilder([], Cast, addressTerms: Terms).Build([Unit("a", "x", "誰か"), Unit("b", "y", null)]).Addressing);
    }

    private static async Task<ClaudeRequest> Translate(TranslationBatch batch)
    {
        var client = new StubClient(request =>
        {
            var ids = System.Text.RegularExpressions.Regex.Matches(request.User, "\"id\":\"([^\"]+)\"").Select(m => m.Groups[1].Value).Distinct();
            return Ok($"{{\"translations\":[{string.Join(",", ids.Select(id => $"{{\"id\":\"{id}\",\"translation\":\"T-{id}\"}}"))}]}}");
        });
        await new ClaudeTranslationProvider(client).TranslateAsync(batch, CancellationToken.None);
        return Assert.Single(client.Requests);
    }

    [Fact]
    public async Task O_pedido_de_traducao_traz_quem_chama_quem_de_que()
    {
        var batch = new TranslationBatchBuilder([], Cast, addressTerms: Terms).Build([Unit("a", "美咲ちゃん、おはよう", "健太")]);

        var request = await Translate(batch);

        Assert.Contains("Tratamento entre personagens", request.FullSystem);
        Assert.Contains("- Kenta chama Misaki de 美咲ちゃん => Misaki-chan", request.FullSystem);
        Assert.DoesNotContain("Misaki chama Kenta", request.FullSystem);
    }

    [Fact]
    public async Task Sem_tratamentos_o_pedido_de_traducao_nao_muda()
    {
        var request = await Translate(new TranslationBatchBuilder([], Cast).Build([Unit("a", "美咲ちゃん", "健太")]));

        Assert.DoesNotContain("Tratamento entre personagens", request.FullSystem);
    }

    [Fact]
    public void O_cache_de_traducao_muda_quando_o_tratamento_muda()
    {
        string Key(params AddressTerm[] terms) =>
            TranslationCache.ContextFor("claude-haiku-5-5", new TranslationBatchBuilder([], Cast, addressTerms: terms).Build([Unit("a", "x", "健太")]));

        var chan = Key(new AddressTerm("健太", "美咲", "美咲ちゃん", "Misaki-chan"));

        Assert.NotEqual(chan, Key(new AddressTerm("健太", "美咲", "美咲ちゃん", "Misaki")));
        Assert.NotEqual(chan, Key());
        Assert.Equal(chan, Key(new AddressTerm("健太", "美咲", "美咲ちゃん", "Misaki-chan")));
    }

    [Fact]
    public void ForLines_mantem_o_tratamento()
    {
        var batch = new TranslationBatchBuilder([], Cast, addressTerms: Terms).Build([Unit("a", "x", "健太"), Unit("b", "y", "健太")]);

        Assert.Equal(batch.Addressing, batch.ForLines([batch.Lines[0]]).Addressing);
    }
}
