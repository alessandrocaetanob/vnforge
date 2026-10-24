using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class LineSuggestionTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public LineSuggestionTests() =>
        raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Untranslated, TranslationStatus.Machine, TranslationStatus.Untranslated);

    public void Dispose() => pasta.Dispose();

    private LineDetailViewModel Abrir(int n, ILineSuggester? sugestor)
    {
        var unidade = ProjectFolder.Open(raiz).LoadText("yst00001")[n];
        return new LineDetailViewModel(unidade, "yst00001", new LineEditor(ProjectFolder.Open(raiz), relogio), new NavegadorNulo(),relogio, acao => acao())
        {
            Suggester = sugestor,
        };
    }

    [Fact]
    public async Task Sugerir_sem_chave_usa_o_provedor_falso_e_nao_mexe_na_traducao()
    {
        using var vm = Abrir(0, new LineSuggester(() => null));

        await vm.SuggestCommand.ExecuteAsync(null);

        Assert.Equal("[PT] 原文0", vm.Suggestion);
        Assert.True(vm.HasSuggestion);
        Assert.Contains("sem chave", vm.SuggestionSource);
        Assert.Equal(string.Empty, vm.Translation);
        Assert.Null(vm.SuggestionError);
        Assert.False(vm.IsSuggesting);
    }

    [Fact]
    public async Task Sugerir_com_provedor_real_nao_diz_que_e_de_teste()
    {
        using var vm = Abrir(0, new LineSuggester(() => new FakeTranslationProvider(u => "Oi!")));

        await vm.SuggestCommand.ExecuteAsync(null);

        Assert.Equal("Oi!", vm.Suggestion);
        Assert.DoesNotContain("sem chave", vm.SuggestionSource);
    }

    [Fact]
    public async Task Aceitar_copia_a_sugestao_para_a_traducao_e_grava()
    {
        using var vm = Abrir(0, new LineSuggester(() => null));
        await vm.SuggestCommand.ExecuteAsync(null);

        vm.AcceptSuggestionCommand.Execute(null);

        Assert.Equal("[PT] 原文0", vm.Translation);
        Assert.False(vm.HasSuggestion);
        Assert.True(vm.Flush());
        Assert.Equal("[PT] 原文0", ProjectFolder.Open(raiz).LoadText("yst00001")[0].Translation);
    }

    [Fact]
    public async Task Descartar_limpa_a_sugestao_sem_tocar_na_traducao()
    {
        using var vm = Abrir(1, new LineSuggester(() => null));
        await vm.SuggestCommand.ExecuteAsync(null);

        vm.DiscardSuggestionCommand.Execute(null);

        Assert.False(vm.HasSuggestion);
        Assert.Equal("tradução 1", vm.Translation);
    }

    [Fact]
    public async Task Falha_do_provedor_vira_mensagem_em_portugues_sem_sugestao()
    {
        using var vm = Abrir(0, new LineSuggester(() => new FakeTranslationProvider { FailOnCall = 1 }));

        await vm.SuggestCommand.ExecuteAsync(null);

        Assert.False(vm.HasSuggestion);
        Assert.StartsWith("Não foi possível pedir a sugestão", vm.SuggestionError);
        Assert.False(vm.IsSuggesting);
    }

    [Fact]
    public async Task Nova_sugestao_apaga_a_anterior_e_o_erro()
    {
        var chamadas = 0;
        using var vm = Abrir(0, new LineSuggester(() => new FakeTranslationProvider(u => $"v{++chamadas}")));

        await vm.SuggestCommand.ExecuteAsync(null);
        await vm.SuggestCommand.ExecuteAsync(null);

        Assert.Equal("v2", vm.Suggestion);
    }

    [Fact]
    public void Sem_sugestor_o_comando_nao_pode_ser_executado()
    {
        using var vm = Abrir(0, null);

        Assert.False(vm.SuggestCommand.CanExecute(null));
    }

    [Fact]
    public async Task Descartar_o_painel_durante_o_pedido_ignora_a_resposta()
    {
        var espera = new TaskCompletionSource<string>();
        var vm = Abrir(0, new EsperaSugestor(espera.Task));

        var pedido = vm.SuggestCommand.ExecuteAsync(null);
        Assert.True(vm.IsSuggesting);
        vm.Dispose();
        espera.SetResult("tarde demais");
        await pedido;

        Assert.False(vm.HasSuggestion);
    }

    [Fact]
    public async Task O_sugestor_manda_so_a_linha_atual_ao_provedor()
    {
        var provedor = new FakeTranslationProvider();
        var sugestor = new LineSuggester(() => provedor);
        var unidade = new TextUnit("yst00001:000007", TextUnitKind.Dialogue, "美咲", @"こんにちは\p", "Oi.", TranslationStatus.Translated);

        var sugestao = await sugestor.SuggestAsync(unidade, CancellationToken.None);

        var lote = Assert.Single(provedor.ReceivedBatches);
        Assert.Equal("yst00001:000007", Assert.Single(lote.Lines).Id);
        Assert.Equal(@"[PT] こんにちは\p", sugestao.Text);
        Assert.False(sugestao.FromFakeProvider);
    }

    [Fact]
    public async Task O_sugestor_recusa_resposta_que_perde_marcadores()
    {
        var sugestor = new LineSuggester(() => new FakeTranslationProvider(u => "sem marcador"));
        var unidade = new TextUnit("yst00001:000007", TextUnitKind.Dialogue, null, @"こんにちは\p", null, TranslationStatus.Untranslated);

        var erro = await Assert.ThrowsAsync<InvalidOperationException>(() => sugestor.SuggestAsync(unidade, CancellationToken.None));

        Assert.Contains("sugestão válida", erro.Message);
    }

    [Fact]
    public async Task O_editor_entrega_um_sugestor_que_funciona_sem_chave()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;

        var painel = vm.Detail!;
        await painel.SuggestCommand.ExecuteAsync(null);

        Assert.Equal("[PT] 原文0", painel.Suggestion);
    }

    private sealed class NavegadorNulo : ILineNavigator
    {
        public void SelectNext()
        {
        }

        public void SelectPrevious()
        {
        }
    }

    private sealed class EsperaSugestor(Task<string> resposta) : ILineSuggester
    {
        public async Task<LineSuggestion> SuggestAsync(TextUnit unit, CancellationToken ct) =>
            new(await resposta, FromFakeProvider: false);
    }
}
