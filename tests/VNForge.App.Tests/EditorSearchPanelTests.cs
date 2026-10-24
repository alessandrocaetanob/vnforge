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
    public async Task O_painel_de_busca_comeca_fechado_e_o_Ctrl_F_abre_so_com_a_busca()
    {
        var vm = await Abrir();
        Assert.False(vm.IsSearchOpen);

        vm.OpenSearchCommand.Execute(null);

        Assert.True(vm.IsSearchOpen);
        Assert.False(vm.IsReplaceVisible);
    }

    [Fact]
    public async Task O_Ctrl_H_abre_com_a_substituicao_e_o_Ctrl_F_nao_a_esconde()
    {
        var vm = await Abrir();

        vm.OpenReplaceCommand.Execute(null);
        Assert.True(vm.IsSearchOpen);
        Assert.True(vm.IsReplaceVisible);

        vm.OpenSearchCommand.Execute(null);
        Assert.True(vm.IsReplaceVisible);
    }

    [Fact]
    public async Task Abrir_o_painel_avisa_a_tela_para_pôr_o_foco_no_campo_certo()
    {
        var vm = await Abrir();
        var avisos = new List<bool>();
        vm.SearchOpened += (_, replace) => avisos.Add(replace);

        vm.OpenSearchCommand.Execute(null);
        vm.OpenReplaceCommand.Execute(null);

        Assert.Equal([false, true], avisos);
    }

    [Fact]
    public async Task Fechar_o_painel_esconde_a_substituicao_e_mantem_o_que_foi_digitado()
    {
        var vm = await Abrir();
        vm.OpenReplaceCommand.Execute(null);
        vm.Search.Pattern = "tradução";

        vm.CloseSearchCommand.Execute(null);

        Assert.False(vm.IsSearchOpen);
        Assert.False(vm.IsReplaceVisible);
        Assert.Equal("tradução", vm.Search.Pattern);
    }

    [Fact]
    public async Task A_busca_do_painel_usa_o_projeto_aberto_no_Editor()
    {
        var vm = await Abrir();

        Assert.Equal(raiz, vm.Search.Project?.Root);
        vm.Search.Pattern = "tradução 1";
        await vm.Search.FindCommand.ExecuteAsync(null);

        Assert.Equal("yst00001 · linha 2", Assert.Single(vm.Search.Results).Location);
    }

    [Fact]
    public async Task Clicar_num_resultado_seleciona_a_linha_na_grade()
    {
        var vm = await Abrir();
        vm.Search.Pattern = "tradução 2";
        await vm.Search.FindCommand.ExecuteAsync(null);

        vm.Search.SelectedResult = vm.Search.Results.Single();
        await vm.Loading;

        Assert.Equal("yst00001:000002", vm.SelectedLine?.Id);
        Assert.Equal("yst00001:000002", vm.Detail?.Id);
    }

    [Fact]
    public async Task Resultado_de_outro_script_troca_o_script_e_tira_o_filtro_que_esconde_a_linha()
    {
        var vm = await Abrir();
        vm.SelectFilterCommand.Execute(vm.Filters.Single(f => f.Filter == LineStatusFilter.Pending));
        vm.Search.Pattern = "segundo script";
        await vm.Search.FindCommand.ExecuteAsync(null);

        await vm.OpenSearchResultAsync(vm.Search.Results.Single());

        Assert.Equal("yst00002", vm.SelectedScript?.Key);
        Assert.Equal("yst00002:000000", vm.SelectedLine?.Id);
        Assert.True(vm.Filters.Single(f => f.Filter == LineStatusFilter.All).IsSelected);
    }

    [Fact]
    public async Task Clicar_de_novo_no_mesmo_resultado_volta_para_a_linha()
    {
        var vm = await Abrir();
        vm.Search.Pattern = "tradução 2";
        await vm.Search.FindCommand.ExecuteAsync(null);
        var linha = vm.Search.Results.Single();
        vm.Search.SelectedResult = linha;
        vm.SelectedLine = vm.Lines[0];

        await vm.OpenSearchResultAsync(linha);

        Assert.Equal("yst00001:000002", vm.SelectedLine?.Id);
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

    [Fact]
    public async Task Trocar_de_projeto_leva_a_busca_junto_e_descarta_os_resultados()
    {
        var vm = await Abrir();
        vm.Search.Pattern = "tradução";
        await vm.Search.FindCommand.ExecuteAsync(null);
        Assert.True(vm.Search.HasResults);

        using var outra = new TempFolder();
        var outraRaiz = SampleProjects.Create(outra.Path, "Outro", TranslationStatus.Translated);
        vm.Project = EditorFixtures.Summary("Outro", outraRaiz);
        await vm.Loading;

        Assert.Equal(outraRaiz, vm.Search.Project?.Root);
        Assert.False(vm.Search.HasResults);
    }
}
