using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

public class StylePromptTests
{
    private sealed class StubClient : IClaudeMessageClient
    {
        public List<ClaudeRequest> Requests { get; } = [];

        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var ids = System.Text.RegularExpressions.Regex.Matches(request.User, "\"id\":\"([^\"]+)\"").Select(m => m.Groups[1].Value).Distinct();
            var items = string.Join(",", ids.Select(id => $"{{\"id\":\"{id}\",\"translation\":\"T-{id}\"}}"));
            return Task.FromResult(new ClaudeReply("end_turn", $"{{\"translations\":[{items}]}}"));
        }
    }

    private static TextUnit Unit(string id, string source, string? speaker = null, string? translation = null) =>
        new(id, TextUnitKind.Dialogue, speaker, source, translation,
            translation is null ? TranslationStatus.Untranslated : TranslationStatus.Translated);

    private static readonly CharacterBook Cast = new([new CharacterEntry("美咲", "Misaki", CharacterGender.Female)]);

    private static TextUnit[] Script() =>
    [
        Unit("l1", "一行目", translation: "Um"),
        Unit("l2", "二行目", translation: "Dois"),
        Unit("l3", "三行目", translation: "Três"),
        Unit("l4", "四行目", "美咲"),
        Unit("l5", "五行目"),
        Unit("l6", "六行目"),
        Unit("l7", "七行目"),
    ];

    private static async Task<ClaudeRequest> Translate(TranslationBatch batch)
    {
        var client = new StubClient();
        await new ClaudeTranslationProvider(client).TranslateAsync(batch, CancellationToken.None);
        return Assert.Single(client.Requests);
    }

    [Fact]
    public async Task O_lote_leva_duas_falas_antes_e_duas_depois_no_pedido()
    {
        var script = Script();
        var builder = new TranslationBatchBuilder([], Cast);

        var batch = builder.Build([script[3]], script[..3], script[4..]);
        var request = await Translate(batch);

        Assert.Equal(["l2", "l3"], batch.PreviousLines.Select(u => u.Id));
        Assert.Equal(["l5", "l6"], batch.NextLines.Select(u => u.Id));
        Assert.Contains("Contexto anterior", request.User);
        Assert.Contains("二行目", request.User);
        Assert.Contains("三行目", request.User);
        Assert.DoesNotContain("一行目", request.User);
        Assert.Contains("Contexto seguinte", request.User);
        Assert.Contains("五行目", request.User);
        Assert.Contains("六行目", request.User);
        Assert.DoesNotContain("七行目", request.User);
        Assert.True(request.User.IndexOf("Contexto seguinte", StringComparison.Ordinal) < request.User.IndexOf("Traduza estas linhas", StringComparison.Ordinal));
    }

    [Fact]
    public void Falas_seguintes_ignoram_linhas_sem_texto_e_o_limite_pode_ser_zero()
    {
        var script = Script();
        var empty = Unit("e", "  ");

        var batch = new TranslationBatchBuilder([], Cast).Build([script[3]], null, [empty, script[4]]);
        var none = new TranslationBatchBuilder([], Cast, maxPreviousLines: 0, maxNextLines: 0).Build([script[3]], script[..3], script[4..]);

        Assert.Equal(["l5"], batch.NextLines.Select(u => u.Id));
        Assert.Empty(none.PreviousLines);
        Assert.Empty(none.NextLines);
    }

    [Fact]
    public async Task Sem_contexto_o_pedido_nao_traz_os_titulos_de_contexto()
    {
        var request = await Translate(new TranslationBatchBuilder([], Cast).Build([Script()[3]]));

        Assert.DoesNotContain("Contexto anterior", request.User);
        Assert.DoesNotContain("Contexto seguinte", request.User);
    }

    [Fact]
    public async Task O_guia_de_estilo_e_as_escolhas_vao_no_prompt_de_sistema()
    {
        var style = new StyleConfig
        {
            Honorifics = HonorificsPolicy.Keep,
            Address = AddressForm.Tu,
            Narrator = new NarratorStyle { Who = "Kenta, o protagonista", Person = NarratorPerson.Third, Gender = NarratorGender.Male },
            TranslateOnomatopoeia = false,
            WesternNameOrder = false,
        };
        var builder = new TranslationBatchBuilder([], Cast, "Linguagem jovem e natural.", style: style);

        var request = await Translate(builder.Build([Script()[3]]));

        Assert.Contains("Linguagem jovem e natural.", request.System);
        Assert.Contains("Estilo da tradução:", request.System);
        Assert.Contains("mantenha -san, -kun, -chan, -sama e senpai", request.System);
        Assert.Contains("\"tu\"", request.System);
        Assert.Contains("Kenta, o protagonista, em terceira pessoa; gênero masculino", request.System);
        Assert.Contains("mantenha como soam em japonês", request.System);
        Assert.Contains("ordem japonesa", request.System);
    }

    [Theory]
    [InlineData(HonorificsPolicy.Keep, "mantenha -san")]
    [InlineData(HonorificsPolicy.Remove, "remova -san")]
    [InlineData(HonorificsPolicy.Adapt, "adapte. -chan e -kun saem")]
    public async Task Cada_escolha_de_honorifico_tem_a_sua_frase(HonorificsPolicy policy, string expected)
    {
        var builder = new TranslationBatchBuilder([], Cast, style: StyleConfig.Default with { Honorifics = policy });

        var request = await Translate(builder.Build([Script()[3]]));

        Assert.Contains(expected, request.System);
        Assert.Single(StylePhrases(request.System), line => line.StartsWith("- Honoríficos:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sem_estilo_escolhido_vale_o_padrao_do_projeto()
    {
        var request = await Translate(new TranslationBatchBuilder([], Cast).Build([Script()[3]]));

        Assert.Contains("adapte. -chan e -kun saem", request.System);
        Assert.Contains("\"você\"", request.System);
        Assert.Contains("Narrador: em primeira pessoa; gênero neutro", request.System);
        Assert.Contains("traduza para o equivalente em português", request.System);
        Assert.Contains("ordem ocidental", request.System);
    }

    [Fact]
    public async Task O_prompt_de_sistema_nao_muda_entre_lotes_do_mesmo_projeto()
    {
        var script = Script();
        var builder = new TranslationBatchBuilder([], Cast, "Tom leve.", style: StyleConfig.Default with { Address = AddressForm.Tu });
        var client = new StubClient();
        var provider = new ClaudeTranslationProvider(client);

        await provider.TranslateAsync(builder.Build([script[3]], script[..3], script[4..]), CancellationToken.None);
        await provider.TranslateAsync(builder.Build([script[4]], script[..4], script[5..]), CancellationToken.None);

        Assert.Equal(2, client.Requests.Count);
        // Personagens e glossário mudam com as linhas do lote e vêm depois; tudo até o guia de estilo é igual.
        var stable = client.Requests[0].System[..(client.Requests[0].System.IndexOf("Tom leve.", StringComparison.Ordinal) + "Tom leve.".Length)];
        Assert.StartsWith(stable, client.Requests[1].System, StringComparison.Ordinal);
        Assert.StartsWith("Você traduz", stable);
        Assert.Contains("Estilo da tradução:", stable);
        Assert.NotEqual(client.Requests[0].User, client.Requests[1].User);
    }

    [Fact]
    public async Task O_contexto_sobrevive_ao_cache_ao_reenvio_e_ao_fallback_de_recusa()
    {
        var script = Script();
        var style = StyleConfig.Default with { Honorifics = HonorificsPolicy.Remove };
        var batch = new TranslationBatchBuilder([], Cast, "Tom leve.", style: style).Build([script[3], script[4]], script[..3], script[5..]);
        var client = new StubClient();
        var chain = new CachingTranslationProvider(new ValidatingTranslationProvider(new ClaudeTranslationProvider(client)), new TranslationCache(), "claude-opus-5-5");

        await chain.TranslateAsync(batch, CancellationToken.None);

        var request = Assert.Single(client.Requests);
        Assert.Contains("remova -san", request.System);
        Assert.Contains("Tom leve.", request.System);
        Assert.Contains("六行目", request.User);
        Assert.Contains("三行目", request.User);
        Assert.Equal(batch.Style, batch.ForLines([script[3]]).Style);
        Assert.Equal(batch.NextLines, batch.ForLines([script[3]]).NextLines);
    }

    [Fact]
    public void A_chave_do_cache_muda_com_o_estilo_mas_nao_com_as_falas_vizinhas()
    {
        var script = Script();
        var line = script[3];
        var plain = new TranslationBatchBuilder([], Cast);

        string Key(TranslationBatch batch) => TranslationCache.KeyFor(line, TranslationCache.ContextFor("claude-opus-5-5", batch));

        var baseKey = Key(plain.Build([line]));

        Assert.Equal(baseKey, Key(plain.Build([line], script[..3], script[4..])));
        Assert.NotEqual(baseKey, Key(new TranslationBatchBuilder([], Cast, style: StyleConfig.Default with { Honorifics = HonorificsPolicy.Keep }).Build([line])));
        Assert.NotEqual(baseKey, Key(new TranslationBatchBuilder([], Cast, style: StyleConfig.Default with { Address = AddressForm.Tu }).Build([line])));
        Assert.NotEqual(baseKey, Key(new TranslationBatchBuilder([], Cast, style: StyleConfig.Default with { WesternNameOrder = false }).Build([line])));
    }

    private static IEnumerable<string> StylePhrases(string system) =>
        system.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("- ", StringComparison.Ordinal));
}
