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
    public async Task O_pedido_leva_a_cena_inteira_traduzida_o_resumo_da_biblia_e_o_schema()
    {
        var client = new StubClient(_ => Ok("""{"adjustments":[]}"""));
        var bible = GameBible.From([new BibleScene("y") { Summary = "Os dois chegam atrasados ao portão." }]);
        var scene = Builder(bible).Build([Unit("y:1", "やあ", "Oi"), Unit("y:2", "遅い", "Tarde.")]);

        await new ClaudeEditorPassProvider(client).ReviewSceneAsync(scene, TestContext.Current.CancellationToken);

        var request = Assert.Single(client.Requests);
        Assert.Contains("\"translation\":\"Oi\"", request.User);
        Assert.Contains("\"translation\":\"Tarde.\"", request.User);
        Assert.Contains("やあ", request.User);
        Assert.Contains("Os dois chegam atrasados ao portão.", request.FullSystem);
        Assert.Contains("editor", request.System);
        Assert.Contains("\"adjustments\"", request.JsonSchema);
        Assert.Contains("\"enum\":[\"fluency\",\"voice\",\"consistency\"]", request.JsonSchema);
    }

    [Fact]
    public async Task Ajuste_com_id_de_fora_id_repetido_ou_tipo_desconhecido_e_descartado_sem_derrubar_os_outros()
    {
        var client = new StubClient(_ => Ok("""
            {"adjustments":[
              {"id":"y:1","kind":"fluency","proposed":"A","reason":"r"},
              {"id":"y:1","kind":"voice","proposed":"B","reason":"r"},
              {"id":"outra:9","kind":"fluency","proposed":"C","reason":"r"},
              {"id":"y:2","kind":"estilo","proposed":"D","reason":"r"},
              {"id":"y:3","kind":"consistency","proposed":"E","reason":"r"}]}
            """));

        var result = await new ClaudeEditorPassProvider(client).ReviewSceneAsync(
            Scene(Unit("y:1", "a", "a"), Unit("y:2", "b", "b"), Unit("y:3", "c", "c")), TestContext.Current.CancellationToken);

        Assert.Equal(["y:1", "y:3"], result.Select(p => p.Id));
        Assert.Equal(["A", "E"], result.Select(p => p.Proposed));
    }

    [Fact]
    public async Task Resposta_sem_o_campo_adjustments_vira_ClaudeResponseException()
    {
        var provider = new ClaudeEditorPassProvider(new StubClient(_ => Ok("""{"reviews":[]}""")));

        await Assert.ThrowsAsync<ClaudeResponseException>(() => provider.ReviewSceneAsync(Scene(Unit("y:1", "a", "a")), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Resposta_cortada_por_limite_de_tokens_e_um_erro_de_resposta()
    {
        var provider = new ClaudeEditorPassProvider(new StubClient(_ => new ClaudeReply("max_tokens", """{"adjustments":[""")));

        await Assert.ThrowsAsync<ClaudeResponseException>(() => provider.ReviewSceneAsync(Scene(Unit("y:1", "a", "a")), TestContext.Current.CancellationToken));
    }

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

    [Fact]
    public async Task Cena_vazia_nao_chama_a_API()
    {
        var client = new StubClient(_ => Ok("""{"adjustments":[]}"""));

        var result = await new ClaudeEditorPassProvider(client).ReviewSceneAsync(Scene(), TestContext.Current.CancellationToken);

        Assert.Empty(result);
        Assert.Empty(client.Requests);
    }

    // ---- FakeEditorPassProvider ----

    [Fact]
    public async Task O_provedor_falso_propoe_tirar_o_espaco_duplo_e_guarda_as_cenas_recebidas()
    {
        var fake = new FakeEditorPassProvider();

        var result = await fake.ReviewSceneAsync(Scene(Unit("y:1", "a", "Oi  você"), Unit("y:2", "b", "Tudo bem")), TestContext.Current.CancellationToken);

        var proposal = Assert.Single(result);
        Assert.Equal(new EditorPassProposal("y:1", EditorSuggestionKind.Fluency, "Oi você", "Tira o espaço duplo."), proposal);
        Assert.Single(fake.ReceivedScenes);
    }

    // ---- EditorPassRunner ----

    [Fact]
    public async Task Ajuste_invalido_e_descartado_e_contado()
    {
        var provider = new ScriptedProvider(_ =>
        [
            new EditorPassProposal("y:1", EditorSuggestionKind.Fluency, "Oi", "igual ao de hoje"),
            new EditorPassProposal("y:2", EditorSuggestionKind.Voice, "  ", "vazio"),
            new EditorPassProposal("y:3", EditorSuggestionKind.Voice, "Sem marcador", "perdeu o \\p"),
            new EditorPassProposal("y:4", EditorSuggestionKind.Voice, "Já aprovada, mudada", "aprovada"),
            new EditorPassProposal("y:9", EditorSuggestionKind.Voice, "Linha que não existe", "fora"),
            new EditorPassProposal("y:5", EditorSuggestionKind.Voice, "Sem motivo", " "),
            new EditorPassProposal("y:6", EditorSuggestionKind.Consistency, "Boa, mudada", "ok"),
            new EditorPassProposal("y:6", EditorSuggestionKind.Fluency, "Repetida", "ok"),
        ]);
        var runner = new EditorPassRunner(provider, Builder());

        var result = await runner.RunSceneAsync(
            "y",
            [
                Unit("y:1", "a", "Oi"),
                Unit("y:2", "b", "Dois"),
                Unit("y:3", "c", "Com\\pmarcador"),
                Unit("y:4", "d", "Aprovada", TranslationStatus.Approved),
                Unit("y:5", "e", "Cinco"),
                Unit("y:6", "f", "Seis"),
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(["y:6"], result.Suggestions.Select(s => s.UnitId));
        Assert.Equal(7, result.Discarded);
    }

    [Fact]
    public async Task O_corredor_nunca_muda_a_traducao_das_linhas()
    {
        var lines = new[] { Unit("y:1", "a", "Os dois") };
        var provider = new ScriptedProvider(_ => [new EditorPassProposal("y:1", EditorSuggestionKind.Fluency, "Vocês dois", "r")]);

        await new EditorPassRunner(provider, Builder()).RunSceneAsync("y", lines, TestContext.Current.CancellationToken);

        Assert.Equal("Os dois", lines[0].Translation);
        Assert.Equal(TranslationStatus.Machine, lines[0].Status);
    }

    // ---- "Outra" ----

    [Fact]
    public async Task Outra_no_provedor_Claude_manda_a_linha_com_a_proposta_recusada_e_devolve_a_primeira_nova()
    {
        var client = new StubClient(_ => Ok("""{"adjustments":[{"id":"y:2","kind":"voice","proposed":"Vocês dois, atrasados.","reason":"Mais direto."}]}"""));
        var provider = new ClaudeEditorPassProvider(client);

        var result = await provider.AnotherAsync(Scene(Unit("y:2", "遅い", "Os dois, atrasados.")), "Os dois atrasados", TestContext.Current.CancellationToken);

        Assert.Equal(new EditorPassProposal("y:2", EditorSuggestionKind.Voice, "Vocês dois, atrasados.", "Mais direto."), result);
        var request = Assert.Single(client.Requests);
        Assert.Contains("A pessoa não quis esta proposta", request.User);
        Assert.Contains("Os dois atrasados", request.User);
    }

    [Fact]
    public async Task Outra_sem_proposta_nova_devolve_nulo()
    {
        var provider = new ClaudeEditorPassProvider(new StubClient(_ => Ok("""{"adjustments":[]}""")));

        Assert.Null(await provider.AnotherAsync(Scene(Unit("y:1", "a", "A")), "B", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Outra_no_corredor_leva_as_falas_em_volta_e_vira_sugestao_nova()
    {
        var provider = new ScriptedProvider(_ => [], (line, _) => new EditorPassProposal(line.Lines[0].Id, EditorSuggestionKind.Voice, "B2", "Outro jeito."));
        var runner = new EditorPassRunner(provider, Builder());

        var result = await runner.AnotherAsync("y", [Unit("y:1", "a", "A"), Unit("y:2", "b", "B"), Unit("y:3", "c", "C")], "y:2", "B1", TestContext.Current.CancellationToken);

        Assert.Equal(("y", "y:2", EditorSuggestionKind.Voice, "B", "B2", "Outro jeito."), (result!.ScriptKey, result.UnitId, result.Kind, result.Base, result.Proposed, result.Reason));
        var (line, rejected) = Assert.Single(provider.Anothers);
        Assert.Equal("B1", rejected);
        Assert.Equal(["y:2"], line.Lines.Select(l => l.Id));
        Assert.Equal(["y:1"], line.PreviousLines.Select(l => l.Id));
        Assert.Equal(["y:3"], line.NextLines.Select(l => l.Id));
    }

    [Theory]
    [InlineData("B1", "y:2", "r")]
    [InlineData("B", "y:2", "r")]
    [InlineData("  ", "y:2", "r")]
    [InlineData("B2", "y:2", " ")]
    [InlineData("B2", "y:9", "r")]
    public async Task Outra_que_nao_vale_devolve_nulo(string proposed, string id, string reason)
    {
        var provider = new ScriptedProvider(_ => [], (_, _) => new EditorPassProposal(id, EditorSuggestionKind.Voice, proposed, reason));

        var result = await new EditorPassRunner(provider, Builder()).AnotherAsync("y", [Unit("y:2", "b", "B")], "y:2", "B1", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task Outra_de_linha_aprovada_ou_inexistente_nem_chama_o_provedor_ou_e_descartada()
    {
        var provider = new ScriptedProvider(_ => [], (l, _) => new EditorPassProposal(l.Lines[0].Id, EditorSuggestionKind.Voice, "Novo", "r"));
        var runner = new EditorPassRunner(provider, Builder());

        Assert.Null(await runner.AnotherAsync("y", [Unit("y:2", "b", "B", TranslationStatus.Approved)], "y:2", "B1", TestContext.Current.CancellationToken));
        Assert.Null(await runner.AnotherAsync("y", [Unit("y:2", "b", "B")], "y:7", "B1", TestContext.Current.CancellationToken));
        Assert.Single(provider.Anothers);
    }

    [Fact]
    public async Task Outra_no_provedor_falso_devolve_a_recusada_com_outra_no_fim()
    {
        var result = await new FakeEditorPassProvider().AnotherAsync(Scene(Unit("y:1", "a", "A")), "Texto", TestContext.Current.CancellationToken);

        Assert.Equal("Texto (outra)", result!.Proposed);
    }

    // ---- EditorPassCost ----

    [Fact]
    public void A_estimativa_cresce_com_a_cena_e_zera_sem_linhas()
    {
        var small = EditorPassCost.Estimate([Scene(Unit("y:1", "やあ、元気？", "Oi, tudo bem?"))], ClaudeModels.Opus);
        var large = EditorPassCost.Estimate([Scene([.. Enumerable.Range(1, 50).Select(i => Unit($"y:{i}", "やあ、元気？", "Oi, tudo bem?"))])], ClaudeModels.Opus);

        Assert.True(small.Dollars > 0m);
        Assert.True(large.Dollars > small.Dollars);
        Assert.Equal(50, large.Lines);
        Assert.Equal(0m, EditorPassCost.Estimate([Scene()], ClaudeModels.Opus).Dollars);
    }

    [Fact]
    public void Linha_ainda_sem_traducao_conta_com_a_traducao_esperada_e_custa_perto_da_traduzida()
    {
        var untranslated = EditorPassCost.Estimate([Scene(Unit("y:1", "今日はいい天気ですね", null, TranslationStatus.Untranslated))], ClaudeModels.Sonnet);
        var translated = EditorPassCost.Estimate([Scene(Unit("y:1", "今日はいい天気ですね", "Hoje está um dia bonito, não é?"))], ClaudeModels.Sonnet);

        Assert.True(untranslated.Dollars > 0m);
        Assert.InRange(untranslated.Dollars / translated.Dollars, 0.5m, 2m);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
