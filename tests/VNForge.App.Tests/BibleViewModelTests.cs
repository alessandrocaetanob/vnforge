using VNForge.AI.Bible;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.App.ViewModels;
using VNForge.Core.Bible;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public sealed class BibleViewModelTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly string _root;
    private readonly ProjectFolder _project;
    private readonly FakeBibleProvider _provider = new();

    public BibleViewModelTests()
    {
        _root = SampleProjects.Create(_temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated, TranslationStatus.Untranslated);
        _project = ProjectFolder.Open(_root);
        _project.SaveText("yst00002", [new TextUnit("yst00002:000000", TextUnitKind.Dialogue, "美咲", "ただいま", null, TranslationStatus.Untranslated)]);
        _project.SaveText("yst00010", [new TextUnit("yst00010:000000", TextUnitKind.Dialogue, "美咲", "さようなら", null, TranslationStatus.Untranslated)]);
    }

    public void Dispose() => _temp.Dispose();

    private BibleViewModel Open()
    {
        var vm = new BibleViewModel { ProviderFactory = (_, _) => _provider };
        vm.Open(ProjectSummaryReader.Read(_root));
        return vm;
    }

    [Fact]
    public void Lista_as_cenas_na_ordem_da_historia_e_marca_as_sem_resumo()
    {
        var vm = Open();

        Assert.Equal(["yst00001", "yst00002", "yst00010"], vm.Rows.Select(r => r.Key));
        Assert.Equal([1, 2, 3], vm.Rows.Select(r => r.Number));
        Assert.Equal("Cena 2", vm.Rows[1].Title);
        Assert.Equal(3, vm.NoSummaryCount);
        Assert.Equal(0, vm.EditedCount);
    }

    [Fact]
    public void O_nome_da_biblia_aparece_na_lista_e_a_cena_com_resumo_ganha_o_visto()
    {
        _project.SaveBible(GameBible.From([new BibleScene("yst00002") { Name = "Volta para casa", Summary = "Ren chega." }]));

        var vm = Open();

        var row = vm.Rows.Single(r => r.Key == "yst00002");
        Assert.Equal("Volta para casa", row.Subtitle);
        Assert.True(row.ShowCheck);
        Assert.Equal(1, vm.NamedCount);
        Assert.Equal(2, vm.NoSummaryCount);
    }

    [Fact]
    public void Escrever_num_campo_grava_no_bible_json_e_o_marca_como_seu()
    {
        _project.SaveBible(GameBible.From([new BibleScene("yst00002") { Name = "Volta", Summary = "Resumo da IA." }]));
        var vm = Open();
        vm.SelectedRow = vm.Rows.Single(r => r.Key == "yst00002");

        Assert.Equal("IA", vm.OwnerOfSummary);
        vm.SummaryText = "Meu resumo.";

        Assert.Equal("Sua", vm.OwnerOfSummary);
        Assert.Equal("IA", vm.OwnerOfTone);
        var saved = _project.LoadBible().Scene("yst00002")!;
        Assert.Equal("Meu resumo.", saved.Summary);
        Assert.Equal([BibleField.Summary], saved.Edited);
        Assert.Equal(1, vm.EditedCount);
    }

    [Fact]
    public void Abrir_a_cena_nao_a_marca_como_editada()
    {
        _project.SaveBible(GameBible.From([new BibleScene("yst00002") { Summary = "Resumo.", Tone = ["seco"] }]));
        var vm = Open();

        vm.SelectedRow = vm.Rows.Single(r => r.Key == "yst00002");
        vm.SelectedRow = vm.Rows[0];
        vm.SelectedRow = vm.Rows.Single(r => r.Key == "yst00002");

        Assert.Empty(_project.LoadBible().Scene("yst00002")!.Edited);
        Assert.Equal("seco", vm.ToneText);
    }

    [Fact]
    public void Editar_a_sinopse_do_jogo_grava_e_marca_como_sua()
    {
        var vm = Open();

        vm.SynopsisText = "Uma cidade onde sempre chove.";

        Assert.Equal("Sua", vm.OwnerOfSynopsis);
        Assert.True(_project.LoadBible().SynopsisEdited);
    }

    [Fact]
    public void Os_filtros_mostram_so_as_editadas_ou_so_as_sem_resumo()
    {
        _project.SaveBible(GameBible.From([new BibleScene("yst00002") { Summary = "Resumo." }.EditSummary("Resumo.")]));
        var vm = Open();

        vm.SetFilterCommand.Execute(BibleFilter.Edited);
        Assert.Equal(["yst00002"], vm.Rows.Select(r => r.Key));

        vm.SetFilterCommand.Execute(BibleFilter.NoSummary);
        Assert.Equal(["yst00001", "yst00010"], vm.Rows.Select(r => r.Key));

        vm.SetFilterCommand.Execute(BibleFilter.All);
        Assert.Equal(3, vm.Rows.Count);
    }

    [Fact]
    public async Task Abrir_a_geracao_mostra_o_preco_antes_de_rodar_e_nao_chama_a_IA()
    {
        var vm = Open();

        vm.OpenGenerateCommand.Execute(null);
        await vm.Generation;

        Assert.True(vm.IsGenerateOpen);
        Assert.StartsWith("Custo (aproximado): R$ ", vm.EstimateText, StringComparison.Ordinal);
        Assert.StartsWith("Já gasto no projeto: R$ ", vm.SpentText, StringComparison.Ordinal);
        Assert.Equal("Campos que você editou (0 cenas)", vm.EditedKeptText);
        Assert.Empty(_provider.SceneCalls);
        Assert.StartsWith("Gerar de novo · R$ ", vm.GenerateLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gerar_escreve_a_biblia_preserva_o_que_e_seu_e_fecha_a_janela()
    {
        _project.SaveBible(GameBible.From([new BibleScene("yst00002") { Summary = "Meu resumo." }.EditSummary("Meu resumo.")]));
        var vm = Open();
        vm.OpenGenerateCommand.Execute(null);
        await vm.Generation;

        vm.ConfirmGenerateCommand.Execute(null);
        await vm.Generation;

        Assert.False(vm.IsGenerateOpen);
        Assert.False(vm.IsRunning);
        var bible = _project.LoadBible();
        Assert.Equal(3, bible.Scenes.Count);
        Assert.Equal("Meu resumo.", bible.Scene("yst00002")!.Summary);
        Assert.StartsWith("3 cenas escritas", vm.Notice, StringComparison.Ordinal);
        Assert.Equal(0, vm.NoSummaryCount);
        Assert.NotEmpty(vm.SynopsisText);
        Assert.NotEmpty(vm.Timeline);
    }

    [Fact]
    public async Task Sem_provedor_a_geracao_pede_a_chave_em_vez_de_falhar()
    {
        var vm = new BibleViewModel();
        vm.Open(ProjectSummaryReader.Read(_root));
        vm.OpenGenerateCommand.Execute(null);
        await vm.Generation;

        vm.ConfirmGenerateCommand.Execute(null);

        Assert.Contains("chave de API", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.False(vm.IsGenerateOpen);
    }

    [Fact]
    public async Task Reescrever_um_campo_troca_so_aquele_campo()
    {
        _project.SaveBible(GameBible.From([new BibleScene("yst00002") { Name = "Antigo", Summary = "Antigo", Tone = ["seco"] }]));
        var provider = new FakeBibleProvider((_, key) => new BibleScene(key) { Name = "Novo", Summary = "Novo", Tone = ["leve"] });
        var vm = new BibleViewModel { ProviderFactory = (_, _) => provider };
        vm.Open(ProjectSummaryReader.Read(_root));
        vm.SelectedRow = vm.Rows.Single(r => r.Key == "yst00002");

        await vm.RewriteFieldCommand.ExecuteAsync(BibleField.Tone);

        Assert.Equal("leve", vm.ToneText);
        Assert.Equal("Antigo", vm.SummaryText);
    }

    private sealed class StubClient(Func<ClaudeReply> reply) : IClaudeMessageClient
    {
        public int Calls { get; private set; }

        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(reply());
        }
    }

    private BibleViewModel OpenWith(StubClient client, BudgetLimits? limits = null)
    {
        var provider = new ClaudeBibleProvider(client);
        var vm = new BibleViewModel { ProviderFactory = (_, _) => provider };
        if (limits is not null)
        {
            vm.Limits = () => limits;
        }

        vm.Open(ProjectSummaryReader.Read(_root));
        vm.SelectedRow = vm.Rows.Single(r => r.Key == "yst00002");
        return vm;
    }

    [Fact]
    public async Task Reescrever_com_recusa_da_IA_mostra_a_mensagem_e_registra_o_gasto()
    {
        var client = new StubClient(() => new ClaudeReply("refusal", string.Empty, new ClaudeUsage(2_000, 500, 0, 0)));
        var vm = OpenWith(client);

        await vm.RewriteFieldCommand.ExecuteAsync(BibleField.Tone);

        Assert.Equal(1, client.Calls);
        Assert.Contains("não quis ler", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.True(AiUsageLog.Load(_project).Dollars > 0m);
    }

    [Fact]
    public async Task Reescrever_com_falha_de_rede_nao_escapa_do_comando_e_fala_em_portugues()
    {
        var provider = new FakeBibleProvider(describe: (_, _) => throw new HttpRequestException("socket"));
        var vm = new BibleViewModel { ProviderFactory = (_, _) => provider };
        vm.Open(ProjectSummaryReader.Read(_root));
        vm.SelectedRow = vm.Rows.Single(r => r.Key == "yst00002");

        await vm.RewriteFieldCommand.ExecuteAsync(BibleField.Tone);

        Assert.Contains("Falha de rede", vm.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reescrever_recusa_o_pedido_que_nao_cabe_no_limite_e_nao_chama_a_IA()
    {
        var client = new StubClient(() => throw new InvalidOperationException("não devia chamar"));
        var vm = OpenWith(client, new BudgetLimits(0.0001m, null));

        await vm.RewriteFieldCommand.ExecuteAsync(BibleField.Tone);

        Assert.Equal(0, client.Calls);
        Assert.Contains("limite", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(0m, AiUsageLog.Load(_project).Dollars);
    }

    [Fact]
    public async Task Gerar_recusa_a_estimativa_que_nao_cabe_no_limite_e_nao_chama_a_IA()
    {
        var client = new StubClient(() => throw new InvalidOperationException("não devia chamar"));
        var vm = OpenWith(client, new BudgetLimits(0.0001m, null));
        vm.OpenGenerateCommand.Execute(null);
        await vm.Generation;

        vm.ConfirmGenerateCommand.Execute(null);
        await vm.Generation;

        Assert.Equal(0, client.Calls);
        Assert.Contains("limite", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.False(vm.IsRunning);
        Assert.False(_project.LoadBible().Scenes.Any());
    }

    [Fact]
    public async Task Bible_json_ilegivel_nao_deixa_gerar_nao_paga_e_nao_toca_o_backup()
    {
        _project.SaveBible(GameBible.From([new BibleScene("yst00002") { Summary = "Boa" }]));
        _project.KeepPreviousBible();
        var backup = File.ReadAllText(_project.PreviousBiblePath);
        File.WriteAllText(_project.BiblePath, "{ isto não é json");
        var client = new StubClient(() => new ClaudeReply("end_turn", "{}", new ClaudeUsage(1, 1, 0, 0)));
        var vm = OpenWith(client);

        vm.OpenGenerateCommand.Execute(null);
        await vm.Generation;

        Assert.False(vm.ConfirmGenerateCommand.CanExecute(null));
        Assert.Contains("inválido", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(0, client.Calls);
        Assert.Equal(backup, File.ReadAllText(_project.PreviousBiblePath));
    }

    [Fact]
    public void Voltar_a_ultima_geracao_restaura_a_biblia_anterior()
    {
        _project.SaveBible(GameBible.From([new BibleScene("yst00002") { Summary = "Antes" }]));
        _project.KeepPreviousBible();
        _project.SaveBible(GameBible.From([new BibleScene("yst00002") { Summary = "Depois" }]));
        var vm = Open();
        vm.SelectedRow = vm.Rows.Single(r => r.Key == "yst00002");

        Assert.True(vm.CanUndo);
        vm.UndoCommand.Execute(null);

        Assert.Equal("Antes", vm.SummaryText);
    }

    [Fact]
    public void Exportar_grava_o_markdown_na_pasta_do_projeto()
    {
        _project.SaveBible(GameBible.From([new BibleScene("yst00002") { Name = "Volta", Summary = "Resumo." }]));
        var vm = Open();

        vm.ExportCommand.Execute(null);

        var text = File.ReadAllText(Path.Combine(_root, "biblia.md"));
        Assert.StartsWith("# Bíblia de Minha VN", text, StringComparison.Ordinal);
        Assert.Contains("### yst00002 · Volta", text, StringComparison.Ordinal);
        Assert.DoesNotContain('—', vm.Notice);
    }
}
