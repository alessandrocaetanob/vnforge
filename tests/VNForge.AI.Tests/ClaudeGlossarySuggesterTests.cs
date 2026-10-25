using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Glossary;
using VNForge.Core.Glossary;

namespace VNForge.AI.Tests;

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

    [Fact]
    public async Task Devolve_genero_e_voz_de_cada_personagem_na_ordem_pedida()
    {
        var client = new StubClient(_ => Ok("""{"characters":[{"name":"健太","gender":"male","voice":"direto e casual"},{"name":"美咲","gender":"female","voice":"animada, trata o veterano com respeito"}]}"""));
        var suggester = new ClaudeGlossarySuggester(client);

        var result = await suggester.SuggestVoicesAsync(Voices, CancellationToken.None);

        Assert.Equal(
            [
                new CharacterVoiceSuggestion("美咲", CharacterGender.Female, "animada, trata o veterano com respeito"),
                new CharacterVoiceSuggestion("健太", CharacterGender.Male, "direto e casual"),
            ],
            result);
    }

    [Fact]
    public async Task O_pedido_vai_para_o_haiku_com_as_falas_e_o_schema_de_personagens()
    {
        var client = new StubClient(_ => Ok("""{"characters":[{"name":"美咲","gender":"female","voice":"x"},{"name":"健太","gender":"male","voice":"y"}]}"""));
        var suggester = new ClaudeGlossarySuggester(client);

        await suggester.SuggestVoicesAsync(Voices, CancellationToken.None);

        var request = Assert.Single(client.Requests);
        Assert.Equal("claude-haiku-5-5", request.Model);
        Assert.Contains("えへへ、ありがとう！", request.User);
        Assert.Contains("\"characters\"", request.JsonSchema);
        Assert.Contains("\"female\"", request.JsonSchema);
    }

    [Fact]
    public async Task Lista_vazia_nao_chama_a_api()
    {
        var client = new StubClient(_ => Ok("{}"));
        var suggester = new ClaudeGlossarySuggester(client);

        Assert.Empty(await suggester.SuggestVoicesAsync([], CancellationToken.None));
        Assert.Empty(await suggester.SuggestTermsAsync([], CancellationToken.None));
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Divide_muitos_personagens_em_varias_chamadas()
    {
        var many = Enumerable.Range(0, ClaudeGlossarySuggester.VoiceChunk + 3).Select(i => new CharacterVoiceRequest($"p{i}", ["あ"])).ToArray();
        var client = new StubClient(request =>
        {
            var names = many.Where(m => request.User.Contains($"\"name\":\"{m.Name}\"", StringComparison.Ordinal)).Select(m => m.Name);
            return Ok($$"""{"characters":[{{string.Join(",", names.Select(n => $$"""{"name":"{{n}}","gender":"other","voice":"v"}"""))}}]}""");
        });
        var suggester = new ClaudeGlossarySuggester(client);

        var result = await suggester.SuggestVoicesAsync(many, CancellationToken.None);

        Assert.Equal(2, client.Requests.Count);
        Assert.Equal(many.Select(m => m.Name), result.Select(r => r.Name));
    }

    [Fact]
    public async Task Recusa_lanca_a_excecao_de_recusa()
    {
        var suggester = new ClaudeGlossarySuggester(new StubClient(_ => new ClaudeReply("refusal", string.Empty)));

        await Assert.ThrowsAsync<ClaudeRefusalException>(() => suggester.SuggestVoicesAsync(Voices, CancellationToken.None));
        await Assert.ThrowsAsync<ClaudeRefusalException>(() => suggester.SuggestTermsAsync([new TermTranslationRequest("魔法", 9)], CancellationToken.None));
    }

    [Fact]
    public async Task Resposta_cortada_por_limite_de_tokens_lanca()
    {
        var suggester = new ClaudeGlossarySuggester(new StubClient(_ => new ClaudeReply("max_tokens", """{"characters":[""")));

        await Assert.ThrowsAsync<ClaudeResponseException>(() => suggester.SuggestVoicesAsync(Voices, CancellationToken.None));
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("""{"outra":[]}""")]
    [InlineData("""{"characters":[{"name":"美咲","gender":"female"}]}""")]
    [InlineData("""{"characters":[{"name":"美咲","gender":"robô","voice":"x"},{"name":"健太","gender":"male","voice":"y"}]}""")]
    [InlineData("""{"characters":[{"name":"美咲","gender":"female","voice":"x"}]}""")]
    [InlineData("""{"characters":[{"name":"美咲","gender":"female","voice":"x"},{"name":"美咲","gender":"female","voice":"x"},{"name":"健太","gender":"male","voice":"y"}]}""")]
    [InlineData("""{"characters":[{"name":"美咲","gender":"female","voice":"x"},{"name":"健太","gender":"male","voice":"y"},{"name":"他人","gender":"male","voice":"z"}]}""")]
    public async Task Resposta_invalida_lanca(string json)
    {
        var suggester = new ClaudeGlossarySuggester(new StubClient(_ => Ok(json)));

        await Assert.ThrowsAsync<ClaudeResponseException>(() => suggester.SuggestVoicesAsync(Voices, CancellationToken.None));
    }

    [Fact]
    public async Task Devolve_traducao_e_motivo_de_cada_termo()
    {
        var client = new StubClient(_ => Ok("""{"terms":[{"source":"魔法学院","target":"Academia de Magia","reason":"Nome da escola, aparece em todo o jogo."},{"source":"先輩","target":"veterano","reason":"Tratamento entre alunos."}]}"""));
        var suggester = new ClaudeGlossarySuggester(client);

        var result = await suggester.SuggestTermsAsync([new TermTranslationRequest("魔法学院", 40, "魔法学院に入る"), new TermTranslationRequest("先輩", 12)], CancellationToken.None);

        Assert.Equal(
            [
                new TermTranslationSuggestion("魔法学院", "Academia de Magia", "Nome da escola, aparece em todo o jogo."),
                new TermTranslationSuggestion("先輩", "veterano", "Tratamento entre alunos."),
            ],
            result);
        var request = Assert.Single(client.Requests);
        Assert.Equal("claude-haiku-5-5", request.Model);
        Assert.Contains("魔法学院に入る", request.User);
        Assert.Contains("\"terms\"", request.JsonSchema);
    }

    [Theory]
    [InlineData("""{"terms":[{"source":"魔法学院","target":"x"}]}""")]
    [InlineData("""{"terms":[{"source":"魔法学院","target":" ","reason":"y"}]}""")]
    [InlineData("""{"terms":[]}""")]
    [InlineData("""{"terms":[{"source":"outro","target":"x","reason":"y"}]}""")]
    [InlineData("""[1,2]""")]
    public async Task Termos_com_resposta_invalida_lancam(string json)
    {
        var suggester = new ClaudeGlossarySuggester(new StubClient(_ => Ok(json)));

        await Assert.ThrowsAsync<ClaudeResponseException>(() => suggester.SuggestTermsAsync([new TermTranslationRequest("魔法学院", 3)], CancellationToken.None));
    }

    public static TheoryData<string?, string> Falhas => new()
    {
        { "refusal", "" },
        { "max_tokens", """{"characters":[""" },
        { "end_turn", "isto não é json" },
        { "end_turn", """{"characters":[{"name":"他人","gender":"male","voice":"z"}]}""" },
        { "end_turn", """{"characters":[{"name":"美咲","gender":"robô","voice":"x"},{"name":"健太","gender":"male","voice":"y"}]}""" },
        { "end_turn", """{"characters":[{"name":"美咲","gender":"female","voice":"x"},{"name":"美咲","gender":"female","voice":"x"}]}""" },
    };

    [Theory]
    [MemberData(nameof(Falhas))]
    public async Task A_mensagem_da_falha_e_uma_frase_para_a_pessoa_e_o_detalhe_tecnico_vai_a_parte(string? stopReason, string text)
    {
        var suggester = new ClaudeGlossarySuggester(new StubClient(_ => new ClaudeReply(stopReason, text)));

        var error = await Assert.ThrowsAnyAsync<Exception>(() => suggester.SuggestVoicesAsync(Voices, CancellationToken.None));

        Assert.EndsWith("Tente de novo ou preencha à mão.", error.Message);
        foreach (var technical in new[] { "stop_reason", "token", "modelo", "json", "claude", "schema" })
        {
            Assert.DoesNotContain(technical, error.Message, StringComparison.OrdinalIgnoreCase);
        }

        var detail = error switch
        {
            ClaudeRefusalException refusal => refusal.Detail,
            ClaudeResponseException response => response.Detail,
            _ => null,
        };
        Assert.False(string.IsNullOrWhiteSpace(detail));
    }

    [Fact]
    public async Task A_traducao_vazia_de_um_termo_nomeia_o_termo_na_frase()
    {
        var suggester = new ClaudeGlossarySuggester(new StubClient(_ => Ok("""{"terms":[{"source":"魔法学院","target":"","reason":"y"}]}""")));

        var error = await Assert.ThrowsAsync<ClaudeResponseException>(() => suggester.SuggestTermsAsync([new TermTranslationRequest("魔法学院", 3)], CancellationToken.None));

        Assert.Equal("A IA não trouxe a tradução de 魔法学院. Tente de novo ou preencha à mão.", error.Message);
        Assert.Contains("魔法学院", error.Detail);
    }

    [Fact]
    public async Task Soma_os_tokens_de_todas_as_chamadas()
    {
        var client = new StubClient(_ => new ClaudeReply("end_turn", """{"terms":[{"source":"魔法","target":"magia","reason":"r"}]}""", new ClaudeUsage(100, 20, 5, 0)));
        var suggester = new ClaudeGlossarySuggester(client);

        await suggester.SuggestTermsAsync([new TermTranslationRequest("魔法", 3)], CancellationToken.None);
        await suggester.SuggestTermsAsync([new TermTranslationRequest("魔法", 3)], CancellationToken.None);

        Assert.Equal(new ClaudeUsage(200, 40, 10, 0), suggester.Usage);
    }

    [Fact]
    public async Task Cancelamento_antes_da_chamada_lanca_sem_pedir_nada()
    {
        var client = new StubClient(_ => Ok("{}"));
        var suggester = new ClaudeGlossarySuggester(client);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => suggester.SuggestVoicesAsync(Voices, cts.Token));
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task O_provedor_falso_responde_sem_rede_e_guarda_o_que_recebeu()
    {
        var fake = new FakeGlossarySuggester();

        var voices = await fake.SuggestVoicesAsync(Voices, CancellationToken.None);
        var terms = await fake.SuggestTermsAsync([new TermTranslationRequest("魔法", 9)], CancellationToken.None);

        Assert.Equal(["美咲", "健太"], voices.Select(v => v.Name));
        Assert.Equal("魔法", Assert.Single(terms).Source);
        Assert.Equal(2, Assert.Single(fake.ReceivedVoices).Count);
        Assert.Single(Assert.Single(fake.ReceivedTerms));
    }

    [Fact]
    public void O_preco_de_voz_e_genero_cresce_com_as_falas_e_vira_zero_sem_pedido()
    {
        var small = GlossaryCost.Voices(Voices, ClaudeModels.Haiku);
        var big = GlossaryCost.Voices([.. Voices, .. Enumerable.Range(0, 50).Select(i => new CharacterVoiceRequest($"p{i}", Enumerable.Repeat("吾輩は猫である。名前はまだ無い。", 20).ToArray()))], ClaudeModels.Haiku);

        Assert.True(small.Dollars > 0m);
        Assert.True(big.Dollars > small.Dollars);
        Assert.Equal(0m, GlossaryCost.Voices([], ClaudeModels.Haiku).Dollars);
        Assert.Equal(0m, GlossaryCost.Terms([], ClaudeModels.Haiku).Dollars);
        Assert.True(GlossaryCost.Terms([new TermTranslationRequest("魔法", 9, "魔法を使う")], ClaudeModels.Haiku).Dollars > 0m);
    }
}
