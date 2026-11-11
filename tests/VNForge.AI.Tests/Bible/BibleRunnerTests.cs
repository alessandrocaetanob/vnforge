using VNForge.AI.Bible;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.Core.Bible;
using VNForge.Core.Glossary;
using static VNForge.AI.Tests.Bible.BibleFixtures;

namespace VNForge.AI.Tests.Bible;

public sealed class BibleRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-ai-tests", Guid.NewGuid().ToString("N"));

    public BibleRunnerTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly (string Chapter, string[] Keys)[] ExpectedSceneCalls = [("Manhã", ["yst1", "yst2"]), ("Manhã", ["yst3"]), ("Festival", ["yst4"])];

    private static BibleRunOptions NoWait() => new() { Delay = (_, _) => Task.CompletedTask, ScenesPerCall = 2, Model = ClaudeModels.Opus };

    private static IReadOnlyList<BibleChapterInput> TwoChapters() =>
    [
        Chapter("Manhã", 1, Scene("yst1", "manha", "おはよう"), Scene("yst2", "tarde", "ただいま"), Scene("yst3", "noite", "おやすみ")),
        Chapter("Festival", 2, Scene("yst4", "festa", "祭りだ")),
    ];

    [Fact]
    public async Task Le_um_capitulo_de_cada_vez_em_chamadas_de_ate_N_cenas_e_grava_a_biblia()
    {
        var project = NewProject(_root);
        var provider = new FakeBibleProvider();
        var runner = new BibleRunner(provider, NoWait());

        var result = await runner.RunAsync(project, TwoChapters(), null, TestContext.Current.CancellationToken);

        Assert.Equal(4, result.ScenesWritten);
        Assert.Equal(2, result.ChaptersRead);
        Assert.Empty(result.Failures);
        Assert.Equal(
            ExpectedSceneCalls,
            provider.SceneCalls.Select(c => (c.Chapter.Name, c.Keys.ToArray())));
        var bible = project.LoadBible();
        Assert.Equal(["yst1", "yst2", "yst3", "yst4"], bible.Scenes.Select(s => s.Key));
        Assert.All(bible.Scenes, s => Assert.NotEmpty(s.Summary));
    }

    [Fact]
    public async Task Depois_dos_capitulos_pede_a_sinopse_e_a_linha_do_tempo_com_o_resumo_das_cenas()
    {
        var project = NewProject(_root);
        var provider = new FakeBibleProvider();

        await new BibleRunner(provider, NoWait()).RunAsync(project, TwoChapters(), null, TestContext.Current.CancellationToken);

        var digests = Assert.Single(provider.OverviewCalls);
        Assert.Equal(["Manhã", "Festival"], digests.Select(d => d.Name));
        Assert.Equal(3, digests[0].Summaries.Count);
        var bible = project.LoadBible();
        Assert.NotEmpty(bible.Synopsis);
        Assert.Equal(["Manhã", "Festival"], bible.Timeline.Select(t => t.Title));
    }

    [Fact]
    public async Task Rodada_parcial_nao_pede_a_sinopse()
    {
        var project = NewProject(_root);
        var provider = new FakeBibleProvider();

        await new BibleRunner(provider, NoWait()).RunAsync(project, [TwoChapters()[1]], null, TestContext.Current.CancellationToken, includeOverview: false);

        Assert.Empty(provider.OverviewCalls);
        Assert.Equal("", project.LoadBible().Synopsis);
    }

    [Fact]
    public async Task O_que_a_pessoa_escreveu_sobrevive_a_uma_nova_geracao()
    {
        var project = NewProject(_root);
        project.SaveBible(GameBible.From([new BibleScene("yst1") { Name = "Meu nome", Summary = "Meu resumo" }.WithEdited(BibleField.Summary, true)]).EditSynopsis("Minha sinopse"));

        await new BibleRunner(new FakeBibleProvider(), NoWait()).RunAsync(project, TwoChapters(), null, TestContext.Current.CancellationToken);

        var bible = project.LoadBible();
        Assert.Equal("Meu resumo", bible.Scene("yst1")!.Summary);
        Assert.NotEqual("Meu nome", bible.Scene("yst1")!.Name);
        Assert.Equal("Minha sinopse", bible.Synopsis);
    }

    [Fact]
    public async Task Guarda_a_biblia_anterior_para_voltar()
    {
        var project = NewProject(_root);
        project.SaveBible(GameBible.From([new BibleScene("yst1") { Summary = "Antes" }]));

        await new BibleRunner(new FakeBibleProvider(), NoWait()).RunAsync(project, TwoChapters(), null, TestContext.Current.CancellationToken);

        Assert.True(project.HasPreviousBible);
        Assert.True(project.RestorePreviousBible());
        Assert.Equal("Antes", project.LoadBible().Scene("yst1")!.Summary);
    }

    [Fact]
    public async Task Registra_na_biblia_quando_foi_gerada_com_que_modelo_e_o_custo()
    {
        var project = NewProject(_root);
        var options = NoWait() with { RunDollars = () => 1.5m, Clock = new FixedClock(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero)) };

        await new BibleRunner(new FakeBibleProvider(), options).RunAsync(project, TwoChapters(), null, TestContext.Current.CancellationToken);

        Assert.Equal(new BibleGeneration(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero), ClaudeModels.Opus, 4, 2, 1.5m), project.LoadBible().Generation);
    }

    [Fact]
    public async Task Capitulo_que_falha_depois_das_tentativas_nao_derruba_os_outros()
    {
        var project = NewProject(_root);
        var attempts = 0;
        var provider = new FakeBibleProvider(describe: (chapter, key) =>
        {
            if (chapter.Name == "Manhã")
            {
                attempts++;
                throw new InvalidOperationException("falha simulada");
            }

            return new BibleScene(key) { Name = "Festa", Summary = "Resumo" };
        });

        var result = await new BibleRunner(provider, NoWait() with { MaxRetries = 2 }).RunAsync(project, TwoChapters(), null, TestContext.Current.CancellationToken);

        var failure = Assert.Single(result.Failures);
        Assert.Equal("Manhã", failure.Chapter);
        Assert.Equal(3, failure.Attempts);
        Assert.Equal(3, attempts);
        Assert.Equal(1, result.ScenesWritten);
        Assert.Equal(["yst4"], project.LoadBible().Scenes.Select(s => s.Key));
    }

    [Fact]
    public async Task Recusa_nao_e_repetida()
    {
        var project = NewProject(_root);
        var calls = 0;
        var provider = new FakeBibleProvider(describe: (_, _) =>
        {
            calls++;
            throw new ClaudeRefusalException("A IA recusou.");
        });

        var result = await new BibleRunner(provider, NoWait()).RunAsync(project, [TwoChapters()[1]], null, TestContext.Current.CancellationToken);

        Assert.Equal(1, calls);
        Assert.Equal(1, Assert.Single(result.Failures).Attempts);
    }

    [Fact]
    public async Task Resposta_cortada_por_max_tokens_nao_e_repetida_com_o_mesmo_limite()
    {
        var project = NewProject(_root);
        var calls = 0;
        var provider = new FakeBibleProvider(describe: (_, _) =>
        {
            calls++;
            throw new ClaudeResponseException("Cortada.", detail: ClaudeResponseException.MaxTokensDetail);
        });

        var result = await new BibleRunner(provider, NoWait()).RunAsync(project, [TwoChapters()[1]], null, TestContext.Current.CancellationToken);

        Assert.Equal(1, calls);
        Assert.Equal(1, Assert.Single(result.Failures).Attempts);
    }

    [Fact]
    public async Task JSON_fora_do_formato_ainda_e_repetido()
    {
        var project = NewProject(_root);
        var calls = 0;
        var provider = new FakeBibleProvider(describe: (_, _) =>
        {
            calls++;
            throw new ClaudeResponseException("Incompleta.", detail: "A resposta não é o JSON esperado.");
        });

        var result = await new BibleRunner(provider, NoWait()).RunAsync(project, [TwoChapters()[1]], null, TestContext.Current.CancellationToken);

        Assert.Equal(3, calls);
        Assert.Equal(3, Assert.Single(result.Failures).Attempts);
    }

    [Fact]
    public async Task Bible_json_ilegivel_para_antes_de_pagar_e_nao_toca_o_backup()
    {
        var project = NewProject(_root);
        project.SaveBible(GameBible.From([new BibleScene("yst1") { Summary = "Boa" }]));
        project.KeepPreviousBible();
        var backup = File.ReadAllText(project.PreviousBiblePath);
        File.WriteAllText(project.BiblePath, "{ isto não é json");
        var provider = new FakeBibleProvider();

        await Assert.ThrowsAsync<GlossaryFileException>(() => new BibleRunner(provider, NoWait()).RunAsync(project, TwoChapters(), null, TestContext.Current.CancellationToken));

        Assert.Empty(provider.SceneCalls);
        Assert.Equal(backup, File.ReadAllText(project.PreviousBiblePath));
    }

    [Fact]
    public async Task Cancelar_devolve_o_que_ja_foi_gravado()
    {
        var project = NewProject(_root);
        using var cts = new CancellationTokenSource();
        var provider = new FakeBibleProvider(describe: (chapter, key) =>
        {
            var scene = new BibleScene(key) { Name = "x", Summary = "y" };
            if (chapter.Name == "Manhã")
            {
                cts.Cancel();
            }

            return scene;
        });

        var result = await new BibleRunner(provider, NoWait()).RunAsync(project, TwoChapters(), null, cts.Token);

        Assert.True(result.Canceled);
        Assert.Equal(["yst1", "yst2"], project.LoadBible().Scenes.Select(s => s.Key));
    }

    [Fact]
    public async Task Para_quando_o_gasto_chega_ao_limite()
    {
        var project = NewProject(_root);
        var spent = 0m;
        var guard = new BudgetGuard(new BudgetLimits(10m, null), () => spent, _ => 0m, null);
        var provider = new FakeBibleProvider(describe: (_, key) =>
        {
            spent = 10m;
            return new BibleScene(key) { Name = "x", Summary = "y" };
        });

        var result = await new BibleRunner(provider, NoWait() with { Budget = guard }).RunAsync(project, TwoChapters(), null, TestContext.Current.CancellationToken);

        Assert.Equal(BudgetStop.LimitReached, result.BudgetStop);
        Assert.Equal(1, result.ChaptersRead);
        Assert.Empty(provider.OverviewCalls);
    }

    [Fact]
    public async Task Avisa_o_andamento_a_cada_chamada()
    {
        var project = NewProject(_root);
        var seen = new List<BibleProgress>();

        await new BibleRunner(new FakeBibleProvider(), NoWait()).RunAsync(project, TwoChapters(), new SyncProgress<BibleProgress>(seen.Add), TestContext.Current.CancellationToken);

        Assert.All(seen, p => Assert.Equal(4, p.ScenesTotal));
        Assert.Equal(4, seen.Max(p => p.ScenesDone));
        Assert.Contains(seen, p => p.ScenesDone == 4 && p.ChaptersDone == 2);
    }

    [Fact]
    public async Task Reescrever_um_campo_troca_so_aquele_campo_da_cena()
    {
        var project = NewProject(_root);
        project.SaveBible(GameBible.From([new BibleScene("yst1") { Name = "Antigo", Summary = "Antigo", Tone = ["seco"] }]));
        var provider = new FakeBibleProvider(describe: (_, key) => new BibleScene(key) { Name = "Novo", Summary = "Novo", Tone = ["leve"] });

        var scene = await new BibleRunner(provider, NoWait()).RewriteFieldAsync(project, TwoChapters()[0], "yst1", BibleField.Tone, TestContext.Current.CancellationToken);

        Assert.Equal(["leve"], scene.Tone);
        Assert.Equal("Antigo", scene.Summary);
        Assert.Equal(["leve"], project.LoadBible().Scene("yst1")!.Tone);
        Assert.Equal(["yst1"], Assert.Single(provider.SceneCalls).Keys);
    }

    // Progress<T> entrega os avisos no pool de threads; com o runner carregado, nem todos chegavam antes da asserção.
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
