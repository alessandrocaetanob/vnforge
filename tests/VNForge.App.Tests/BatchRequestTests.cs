using Microsoft.Extensions.Time.Testing;
using VNForge.AI.BatchRequest;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>
/// O pedido em lote escrito em português: a paleta oferece o comando, o painel de busca e substituição mostra as falas e o custo, a IA só é chamada ao
/// confirmar, a prévia traz antes e depois e nada é gravado até Aplicar, que passa pelo ponto de restauração.
/// </summary>
public sealed class BatchRequestTests : IDisposable
{
    private const string Request = "nas falas do Kenta, trocar você por tu";
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder temp = new();
    private readonly FakeTimeProvider clock = new(Now);
    private readonly string root;

    public BatchRequestTests()
    {
        root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN", lines: [("yst00001", TranslationStatus.Translated)]);
        var folder = ProjectFolder.Open(root);
        folder.SaveCharacters([new CharacterEntry("健太", "Kenta"), new CharacterEntry("美咲", "Misaki")]);
        folder.SaveText("yst00001",
        [
            Line(0, "健太", "Você sabe o que houve?", TranslationStatus.Approved),
            Line(1, "美咲", "Você chegou cedo.", TranslationStatus.Machine),
            Line(2, "健太", "Vamos embora.", TranslationStatus.Machine),
            Line(3, "健太", "Eu vi você ontem.", TranslationStatus.Machine),
        ]);
    }

    public void Dispose() => temp.Dispose();

    private sealed class ThrowingProvider : IBatchRequestProvider
    {
        public Task<IReadOnlyList<BatchRewrite>> RewriteAsync(string request, VNForge.AI.Translation.TranslationBatch batch, CancellationToken ct) =>
            Task.FromException<IReadOnlyList<BatchRewrite>>(new InvalidOperationException("sem rede"));
    }

    private static TextUnit Line(int index, string speaker, string translation, TranslationStatus status) =>
        new($"yst00001:{index:D6}", TextUnitKind.Dialogue, speaker, $"原文{index}", translation, status);

    private static BatchRequestSettings Settings(bool key = true, BudgetLimits? limits = null) =>
        new(ExchangeRate.Placeholder, limits ?? BudgetLimits.None, TranslationQuality.Balanced, 2, key, (_, _) => Task.CompletedTask);

    private (SearchReplaceViewModel Vm, FakeBatchRequestProvider Provider, BackgroundTaskService Tasks) Build(bool key = true, BudgetLimits? limits = null, Func<IBatchRequestProvider, ClaudeUsage?>? usageOf = null)
    {
        var provider = FakeBatchRequestProvider.Replacing("você", "tu");
        var tasks = new BackgroundTaskService(time: clock);
        var vm = new SearchReplaceViewModel(clock)
        {
            Project = ProjectSummaryReader.Read(root),
            Requests = new BatchRequestService((_, _) => provider, () => Settings(key, limits), usageOf),
            History = new BatchHistory(tasks, clock),
        };
        return (vm, provider, tasks);
    }

    private string Translation(int index) => ProjectFolder.Open(root).LoadText("yst00001")[index].Translation!;

    [Fact]
    public async Task O_pedido_acha_as_falas_do_personagem_e_mostra_o_custo_sem_chamar_a_IA()
    {
        var (vm, provider, _) = Build();

        await vm.BeginRequestAsync(Request);

        Assert.True(vm.HasRequestPlan);
        Assert.Equal(["yst00001:000000", "yst00001:000003"], vm.RequestPlan!.Candidates.Select(c => c.Unit.Id));
        Assert.Contains("2 falas de Kenta com “você”", vm.Summary);
        Assert.Contains("Custo estimado: até", vm.Summary);
        Assert.Contains("Nada é gravado", vm.Summary);
        Assert.Empty(provider.Received);
        Assert.Empty(vm.Results);
        Assert.True(vm.RunRequestCommand.CanExecute(null));
    }

    [Fact]
    public async Task Confirmar_chama_a_IA_e_mostra_a_previa_com_antes_e_depois_sem_gravar()
    {
        var (vm, provider, _) = Build();
        await vm.BeginRequestAsync(Request);

        await vm.RunRequestCommand.ExecuteAsync(null);

        var (request, batch) = Assert.Single(provider.Received);
        Assert.Equal(Request, request);
        Assert.Equal(["yst00001:000000", "yst00001:000003"], batch.Lines.Select(l => l.Id));
        Assert.Equal(["yst00001 · linha 1", "yst00001 · linha 4"], vm.Results.Select(r => r.Location));
        Assert.Equal(["Você sabe o que houve?", "Eu vi você ontem."], vm.Results.Select(r => r.Before));
        Assert.Equal(["tu sabe o que houve?", "Eu vi tu ontem."], vm.Results.Select(r => r.After));
        Assert.True(vm.HasPreview);
        Assert.False(vm.HasRequestPlan);
        Assert.Contains("A IA reescreveu 2 linhas de 2 lidas. Nada foi gravado ainda.", vm.Summary);
        Assert.Contains("Voltam para Traduzida e precisam de nova revisão: 1 aprovada", vm.Summary);
        Assert.Equal("Você sabe o que houve?", Translation(0));
        Assert.Equal("Eu vi você ontem.", Translation(3));
    }

    [Fact]
    public async Task Aplicar_grava_so_a_previa_e_deixa_o_Desfazer_na_lista_de_tarefas()
    {
        var (vm, _, tasks) = Build();
        await vm.BeginRequestAsync(Request);
        await vm.RunRequestCommand.ExecuteAsync(null);
        var applied = 0;
        vm.Applied += (_, result) => applied = result.Applied;

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(2, applied);
        Assert.Equal("tu sabe o que houve?", Translation(0));
        Assert.Equal("Você chegou cedo.", Translation(1));
        Assert.Equal("Eu vi tu ontem.", Translation(3));
        var unit = ProjectFolder.Open(root).LoadText("yst00001")[0];
        Assert.Equal(TranslationStatus.Translated, unit.Status);
        Assert.Equal("manual", unit.Origin?.Provider);
        var task = Assert.Single(tasks.Tasks);
        Assert.StartsWith("Pedido em lote:", task.Title);
        Assert.NotNull(task.Restore);
        Assert.False(vm.HasPreview);
    }

    [Fact]
    public async Task O_Desfazer_do_pedido_devolve_as_linhas()
    {
        var (vm, _, tasks) = Build();
        await vm.BeginRequestAsync(Request);
        await vm.RunRequestCommand.ExecuteAsync(null);
        await vm.ApplyCommand.ExecuteAsync(null);

        await vm.History!.UndoAsync(Assert.Single(tasks.Tasks));

        Assert.Equal("Você sabe o que houve?", Translation(0));
        Assert.Equal("Eu vi você ontem.", Translation(3));
    }

    [Fact]
    public async Task Descartar_o_pedido_nao_chama_a_IA()
    {
        var (vm, provider, _) = Build();
        await vm.BeginRequestAsync(Request);

        vm.DiscardRequestCommand.Execute(null);

        Assert.False(vm.HasRequestPlan);
        Assert.Empty(provider.Received);
        Assert.Contains("Nada foi gasto", vm.Summary);
    }

    [Fact]
    public async Task Mudar_um_campo_do_painel_descarta_o_pedido_pendente()
    {
        var (vm, _, _) = Build();
        await vm.BeginRequestAsync(Request);

        vm.Pattern = "outra coisa";

        Assert.False(vm.HasRequestPlan);
    }

    [Fact]
    public async Task Sem_chave_o_pedido_e_recusado_antes_de_qualquer_gasto()
    {
        var (vm, provider, _) = Build(key: false);

        await vm.BeginRequestAsync(Request);

        Assert.False(vm.HasRequestPlan);
        Assert.Contains("falta a chave", vm.ErrorMessage);
        Assert.Empty(provider.Received);
    }

    [Fact]
    public async Task Pedido_que_nao_acha_fala_diz_isso_em_vez_de_ler_o_projeto_todo()
    {
        var (vm, provider, _) = Build();

        await vm.BeginRequestAsync("nas falas da Misaki, trocar tchau por até logo");

        Assert.False(vm.HasRequestPlan);
        Assert.Contains("Nenhuma fala traduzida bate", vm.ErrorMessage);
        Assert.Empty(provider.Received);
    }

    [Fact]
    public async Task Pedido_sem_filtro_avisa_que_a_IA_vai_ler_tudo()
    {
        var (vm, _, _) = Build();

        await vm.BeginRequestAsync("deixar as falas mais naturais");

        Assert.Contains("não cita personagem, cena nem palavra", vm.Summary);
        Assert.Equal(4, vm.RequestPlan!.Candidates.Count);
    }

    [Fact]
    public async Task Limite_de_gasto_atingido_recusa_o_pedido()
    {
        var folder = ProjectFolder.Open(root);
        AiUsageLog.TryRecord(folder, new AiUsageTotals(1_000_000, 1_000_000, 0, 0, 100m, 1, 0));
        var (vm, provider, _) = Build(limits: new BudgetLimits(1m, null));

        await vm.BeginRequestAsync(Request);

        Assert.False(vm.HasRequestPlan);
        Assert.Contains("limite de gasto", vm.ErrorMessage);
        Assert.Empty(provider.Received);
    }

    [Fact]
    public async Task O_gasto_da_IA_fica_registrado_no_projeto()
    {
        var (vm, _, _) = Build(usageOf: _ => new ClaudeUsage(2_000, 500, 0, 0));
        await vm.BeginRequestAsync(Request);

        await vm.RunRequestCommand.ExecuteAsync(null);

        Assert.True(AiUsageLog.Load(ProjectFolder.Open(root)).Dollars > 0m);
        Assert.Contains("Gasto: ", vm.Summary);
    }

    [Fact]
    public async Task Falha_da_IA_mostra_o_erro_e_nao_deixa_prévia()
    {
        var provider = new ThrowingProvider();
        var vm = new SearchReplaceViewModel(clock)
        {
            Project = ProjectSummaryReader.Read(root),
            Requests = new BatchRequestService((_, _) => provider, () => Settings()),
        };
        await vm.BeginRequestAsync(Request);

        await vm.RunRequestCommand.ExecuteAsync(null);

        Assert.False(vm.HasPreview);
        Assert.Contains("A IA não respondeu", vm.ErrorMessage);
        Assert.Equal("Você sabe o que houve?", Translation(0));
    }

    private sealed class GatedProvider(Task gate) : IBatchRequestProvider
    {
        public async Task<IReadOnlyList<BatchRewrite>> RewriteAsync(string request, VNForge.AI.Translation.TranslationBatch batch, CancellationToken ct)
        {
            await gate.WaitAsync(ct);
            return [.. batch.Lines.Select(l => new BatchRewrite(l.Id, l.Translation!.Replace("você", "tu", StringComparison.Ordinal)))];
        }
    }

    [Fact]
    public async Task Novo_pedido_com_a_IA_trabalhando_e_recusado_e_o_Cancelar_vale_para_o_pedido_ativo()
    {
        var gate = new TaskCompletionSource();
        var vm = new SearchReplaceViewModel(clock)
        {
            Project = ProjectSummaryReader.Read(root),
            Requests = new BatchRequestService((_, _) => new GatedProvider(gate.Task), () => Settings()),
        };
        await vm.BeginRequestAsync(Request);
        var running = vm.RunRequestCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);

        await vm.BeginRequestAsync("nas falas da Misaki, trocar você por tu");

        Assert.Contains("Espere a operação", vm.ErrorMessage);
        vm.CancelCommand.Execute(null);
        await running;
        Assert.False(vm.IsBusy);
        Assert.Contains("Cancelado", vm.Summary);
        Assert.Equal("Você sabe o que houve?", Translation(0));
    }

    [Fact]
    public async Task Cancelar_depois_de_um_lote_voltar_mantem_a_previa_das_linhas_prontas()
    {
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00002", [.. Enumerable.Range(0, 70).Select(i => Line(i, "健太", $"Você vai {i}?", TranslationStatus.Machine) with { Id = $"yst00002:{i:D6}" })]);
        using var cancel = new CancellationTokenSource();
        var calls = 0;
        var provider = new FakeBatchRequestProvider(unit =>
        {
            if (Interlocked.Increment(ref calls) == 31)
            {
                cancel.Cancel();
            }

            return unit.Translation!.Replace("Você", "Tu", StringComparison.Ordinal);
        });
        var service = new BatchRequestService((_, _) => provider, () => Settings() with { Concurrency = 1 });
        var plan = service.Plan(folder, "na yst00002, trocar você por tu", TestContext.Current.CancellationToken);

        var result = await service.RunAsync(folder, plan, cancel.Token);

        Assert.True(result.Outcome.Canceled);
        Assert.InRange(result.Preview.Items.Count, 30, 69);
        Assert.Contains("Tu vai 0?", result.Preview.Items[0].After);
    }

    [Fact]
    public void A_paleta_oferece_o_pedido_primeiro_quando_o_texto_tem_tres_palavras_ou_mais()
    {
        var asked = new List<string>();
        var palette = new CommandPaletteViewModel(
            () => [new PaletteEntry(PaletteGroup.Command, "Trocar tema", "", () => { })],
            (_, _) => [],
            query => new PaletteEntry(PaletteGroup.Command, $"Pedir à IA: {query}", "", () => asked.Add(query)));
        palette.Open();

        palette.Query = "trocar você por tu";

        Assert.Equal("Pedir à IA: trocar você por tu", palette.Results[0].Title);
        palette.ExecuteSelected();
        Assert.Equal(["trocar você por tu"], asked);
    }

    [Fact]
    public void A_paleta_nao_oferece_o_pedido_quando_o_texto_ja_nomeia_um_comando()
    {
        var palette = new CommandPaletteViewModel(
            () => [new PaletteEntry(PaletteGroup.Command, "Revisar falas de Kenta", "", () => { })],
            (_, _) => [],
            query => new PaletteEntry(PaletteGroup.Command, $"Pedir à IA: {query}", "", () => { }));
        palette.Open();

        palette.Query = "revisar falas de kenta";

        Assert.Equal(["Revisar falas de Kenta"], palette.Results.Select(r => r.Title));
    }

    [Fact]
    public async Task Na_janela_principal_a_paleta_leva_o_pedido_ao_painel_de_busca()
    {
        var provider = FakeBatchRequestProvider.Replacing("você", "tu");
        var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now));
        var settings = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("ajustes")), _ => null);
        var vm = new MainWindowViewModel(start, new ResolvedApiKey("chave", ApiKeySource.Vault), null, settings)
        {
            BatchRequestProviderFactory = (_, _) => provider,
        };
        await start.OpenAsync(root);
        start.OpenSelectedInEditorCommand.Execute(null);
        await vm.Editor.Loading;
        vm.Palette.Open();

        vm.Palette.Query = "tu";
        Assert.DoesNotContain(vm.Palette.Results, r => r.Title.StartsWith("Pedir à IA", StringComparison.Ordinal));

        vm.Palette.Query = Request;
        Assert.StartsWith("Pedir à IA: ", vm.Palette.Results[0].Title, StringComparison.Ordinal);
        vm.Palette.ExecuteSelected();
        await vm.Editor.BatchRequest;

        Assert.True(vm.Editor.IsSearchOpen);
        Assert.True(vm.Editor.IsReplaceVisible);
        Assert.NotNull(vm.Editor.Search.RequestPlan);
        Assert.Empty(provider.Received);
    }
}
