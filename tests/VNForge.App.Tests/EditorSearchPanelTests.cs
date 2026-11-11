using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A busca e a substituição dentro do Editor (#234): abre por botão e atalho, e o resultado leva à linha da grade.</summary>
public sealed class EditorSearchPanelTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public EditorSearchPanelTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Translated, TranslationStatus.Translated, TranslationStatus.Machine, TranslationStatus.Untranslated);
        ProjectFolder.Open(raiz).SaveText(
            "yst00002",
            [new TextUnit("yst00002:000000", TextUnitKind.Dialogue, null, "原文", "tradução do segundo script", TranslationStatus.Translated)]);
    }

    public void Dispose() => pasta.Dispose();

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    [Fact]
    public async Task Resultado_de_uma_linha_que_nao_existe_mais_avisa_no_Editor()
    {
        var vm = await Abrir();

        await vm.OpenSearchResultAsync(new SearchResultRow("yst00001 · linha 99", "x", null, 1, 0, "yst00001", "yst00001:000099"));

        Assert.Contains("yst00001:000099", vm.ErrorMessage);
        Assert.Equal("yst00001:000000", vm.SelectedLine?.Id);
    }

    [Fact]
    public async Task Aplicar_a_substituicao_regrava_o_projeto_e_o_Editor_reabre_na_mesma_linha()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[1];
        vm.OpenReplaceCommand.Execute(null);
        vm.Search.Pattern = "tradução";
        vm.Search.Replacement = "nova";
        await vm.Search.PreviewCommand.ExecuteAsync(null);

        await vm.Search.ApplyCommand.ExecuteAsync(null);
        await vm.Loading;

        Assert.Equal("nova 1", ProjectFolder.Open(raiz).LoadText("yst00001")[1].Translation);
        Assert.Equal("yst00001:000001", vm.SelectedLine?.Id);
        Assert.Equal("nova 1", vm.SelectedLine?.Entry.Unit.Translation);
    }

    [Fact]
    public async Task Aplicar_grava_antes_a_edicao_pendente_e_nao_passa_por_cima_dela()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[0];
        vm.OpenReplaceCommand.Execute(null);
        vm.Search.Pattern = "tradução";
        vm.Search.Replacement = "nova";
        await vm.Search.PreviewCommand.ExecuteAsync(null);
        vm.Detail!.Translation = "tradução editada";

        await vm.Search.ApplyCommand.ExecuteAsync(null);
        await vm.Loading;

        var gravadas = ProjectFolder.Open(raiz).LoadText("yst00001");
        Assert.Equal("tradução editada", gravadas[0].Translation);
        Assert.Equal("nova 1", gravadas[1].Translation);
        Assert.Contains("mudou depois da prévia", vm.Search.Summary);
    }
}
