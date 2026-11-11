using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public sealed class LineSuggestionTests : IDisposable
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
    public async Task Descartar_limpa_a_sugestao_sem_tocar_na_traducao()
    {
        using var vm = Abrir(1, new LineSuggester(() => null));
        await vm.SuggestCommand.ExecuteAsync(null);

        vm.DiscardSuggestionCommand.Execute(null);

        Assert.False(vm.HasSuggestion);
        Assert.Equal("tradução 1", vm.Translation);
    }

    [Fact]
    public async Task Recusa_do_modelo_vira_mensagem_em_portugues()
    {
        using var vm = Abrir(0, new LineSuggester(() => new RecusaProvider()));

        await vm.SuggestCommand.ExecuteAsync(null);

        Assert.False(vm.HasSuggestion);
        Assert.Contains("sugestão válida", vm.SuggestionError);
    }

    [Fact]
    public async Task Painel_descartado_nao_pede_sugestao()
    {
        var provedor = new FakeTranslationProvider();
        var vm = Abrir(0, new LineSuggester(() => provedor));
        vm.Dispose();

        await vm.SuggestCommand.ExecuteAsync(null);

        Assert.Empty(provedor.ReceivedBatches);
        Assert.False(vm.HasSuggestion);
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

    private sealed class EsperaSugestor : ILineSuggester
    {
        private readonly TaskCompletionSource<string> resposta = new();

        /// <summary>O token recebido foi cancelado.</summary>
        public bool Cancelado { get; private set; }

        public void Completar(string texto) => resposta.TrySetResult(texto);

        public async Task<LineSuggestion> SuggestAsync(TextUnit unit, CancellationToken ct)
        {
            ct.Register(() =>
            {
                Cancelado = true;
                resposta.TrySetCanceled(ct);
            });
            return new(await resposta.Task, FromFakeProvider: false);
        }
    }

    private sealed class RoteiroSugestor(params Func<CancellationToken, Task<LineSuggestion>>[] passos) : ILineSuggester
    {
        private int proximo;

        public Task<LineSuggestion> SuggestAsync(TextUnit unit, CancellationToken ct) => passos[proximo++](ct);
    }

    private sealed class RecusaProvider : ITranslationProvider
    {
        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
        {
            IReadOnlyList<TranslationResult> resultado = [.. batch.Lines.Select(u => new TranslationResult(u.Id, string.Empty, Refused: true))];
            return Task.FromResult(resultado);
        }
    }
}
