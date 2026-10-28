using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Search;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O ponto de restauração das operações em lote na lista de tarefas: a tarefa nasce com "Desfazer", sobrevive ao fechar o app e conta o que ficou.</summary>
public sealed class BatchHistoryTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 26, 14, 32, 0, TimeSpan.Zero);

    private readonly TempFolder temp = new();
    private readonly FakeTimeProvider clock = new(Start);
    private readonly string root;

    public BatchHistoryTests() =>
        root = StartFixtures.Create(
            temp.SubFolder("vn"),
            "Minha VN",
            lines: [("yst00001", TranslationStatus.Translated), ("yst00001", TranslationStatus.Translated), ("yst00001", TranslationStatus.Approved)]);

    public void Dispose() => temp.Dispose();

    private (BackgroundTaskService Service, BatchHistory History, TaskBarViewModel Bar) Build()
    {
        var service = new BackgroundTaskService(time: clock);
        var history = new BatchHistory(service, clock);
        return (service, history, new TaskBarViewModel(service, clock, history));
    }

    private TextUnit Line(int index) => ProjectFolder.Open(root).LoadText("yst00001")[index];

    private ReplacePreview Preview(string text, string with) =>
        ProjectSearch.PreviewReplace(ProjectFolder.Open(root), new SearchOptions(text), with, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Operacao_em_lote_entra_na_lista_concluida_e_com_Desfazer()
    {
        var (service, history, bar) = Build();
        var preview = Preview("tradução", "ZZ");

        var result = history.Run(root, "Substituir lote", folder => ProjectSearch.Apply(folder, preview, clock));

        Assert.Equal(3, result.Applied);
        var entry = Assert.Single(bar.Entries);
        Assert.Equal("Substituir lote", entry.Title);
        Assert.Equal(BackgroundTaskState.Completed, entry.State);
        Assert.True(entry.CanUndo);
        Assert.Equal(3, service.Tasks.Single().Restore!.LineCount);
        Assert.Equal("ZZ 0", Line(0).Translation);

        await entry.UndoCommand.ExecuteAsync(null);

        Assert.Equal("tradução 0", Line(0).Translation);
        Assert.False(entry.CanUndo);
        Assert.True(entry.HasUndoMessage);
        Assert.Equal("3 linhas voltaram ao que eram.", entry.UndoMessage);
    }

    [Fact]
    public void Operacao_que_nao_mudou_linha_nenhuma_nao_entra_na_lista()
    {
        var (service, history, _) = Build();

        history.Run(root, "Nada", folder => folder.LoadText("yst00001").Count);

        Assert.Empty(service.Tasks);
    }

    [Fact]
    public async Task O_Desfazer_vale_em_outra_sessao_e_o_ponto_nao_entra_duas_vezes()
    {
        var first = Build();
        var preview = Preview("tradução", "ZZ");
        first.History.Run(root, "Substituir lote", folder => ProjectSearch.Apply(folder, preview, clock));
        clock.Advance(TimeSpan.FromDays(3));

        var (_, history, bar) = Build();
        history.LoadPersisted(root);
        history.LoadPersisted(root);

        var entry = Assert.Single(bar.Entries);
        Assert.Equal("Substituir lote", entry.Title);
        Assert.True(entry.CanUndo);
        Assert.Contains("3 linhas", entry.Subtitle);

        BatchUndone? undone = null;
        history.Undone += (_, e) => undone = e;
        await entry.UndoCommand.ExecuteAsync(null);

        Assert.NotNull(undone);
        Assert.Equal(root, undone.ProjectRoot);
        Assert.Empty(new RestorePointStore(ProjectFolder.Open(root), clock).List());
        Assert.Equal("tradução 0", Line(0).Translation);
    }

    [Fact]
    public async Task Linha_editada_depois_do_lote_fica_e_o_aviso_diz_quantas()
    {
        var (_, history, bar) = Build();
        var preview = Preview("tradução", "ZZ");
        history.Run(root, "Substituir lote", folder => ProjectSearch.Apply(folder, preview, clock));
        new LineEditor(ProjectFolder.Open(root), clock).SetTranslation("yst00001", Line(1).Id, "Minha versão");

        var entry = Assert.Single(bar.Entries);
        await entry.UndoCommand.ExecuteAsync(null);

        Assert.Equal("Minha versão", Line(1).Translation);
        Assert.Equal("tradução 0", Line(0).Translation);
        Assert.Equal("2 linhas voltaram ao que eram. 1 foi editada depois e ficou como está.", entry.UndoMessage);
    }

    [Fact]
    public async Task Ponto_que_ja_foi_desfeito_em_outra_janela_some_do_Desfazer_com_aviso()
    {
        var (_, history, bar) = Build();
        var preview = Preview("tradução", "ZZ");
        history.Run(root, "Substituir lote", folder => ProjectSearch.Apply(folder, preview, clock));
        var store = new RestorePointStore(ProjectFolder.Open(root), clock);
        store.Discard(Assert.Single(store.List()).Id);

        var entry = Assert.Single(bar.Entries);
        await entry.UndoCommand.ExecuteAsync(null);

        Assert.False(entry.CanUndo);
        Assert.Contains("Não foi possível desfazer", entry.UndoMessage);
    }

    [Theory]
    [InlineData(3, 0, 0, "3 linhas voltaram ao que eram.")]
    [InlineData(1, 1, 0, "1 linha voltou ao que era. 1 foi editada depois e ficou como está.")]
    [InlineData(0, 4, 0, "Nenhuma linha voltou. 4 foram editadas depois e ficaram como estão.")]
    [InlineData(2, 0, 1, "2 linhas voltaram ao que eram. 1 já não existe no script.")]
    public void O_aviso_do_Desfazer_diz_quantas_voltaram_e_quantas_ficaram(int restored, int edited, int missing, string expected) =>
        Assert.Equal(expected, BatchHistory.Describe(new RestoreUndoResult(restored, edited, missing)));
}
