using Microsoft.Extensions.Time.Testing;
using VNForge.App.ViewModels;
using VNForge.App.Services;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Os textos que a passada de editor (#532) mostra no Modo revisão, como no quadro: o motivo com a promessa de que nada muda e o aviso da caixa do jogo que a sugestão resolve.</summary>
public sealed class EditorSuggestionReviewTextTests : IDisposable
{
    private static readonly DateTimeOffset Meio = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private const string Longa = "O nosso professor responsável já estava de pé, esperando por nós dois de braços cruzados na frente do portão da escola, debaixo da chuva.";
    private const string Curta = "O nosso professor responsável já estava de pé, esperando por nós de braços cruzados na frente do portão, debaixo da chuva.";

    private readonly TempFolder pasta = new();
    private readonly string raiz;

    public EditorSuggestionReviewTextTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste");
        var folder = ProjectFolder.Open(raiz);
        folder.SaveFormatting(FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: 40, MaxLines: 3, CharWidth: CharWidthMode.Uniform) });
        folder.SaveText("yst00001",
        [
            new("yst00001:000000", TextUnitKind.Dialogue, "健太", "担任の先生が腕を組んで立っていた。\\p", Longa + "\\p", TranslationStatus.Machine),
            new("yst00001:000001", TextUnitKind.Dialogue, "健太", "「うん」\\p", "Aham.\\p", TranslationStatus.Machine),
        ]);
        EditorSuggestionsFile.Save(folder, EditorSuggestionsFile.Empty.ReplacingScript("yst00001",
        [
            new("yst00001", "yst00001:000000", EditorSuggestionKind.Voice, Longa + "\\p", Curta + "\\p", "Tira \"dois de\" para caber na caixa.", Meio),
            new("yst00001", "yst00001:000001", EditorSuggestionKind.Fluency, "Aham.\\p", "Uhum.\\p", "Soa mais natural.", Meio),
        ]));
    }

    public void Dispose() => pasta.Dispose();

    private async Task<LineDetailViewModel> Abrir(int linha)
    {
        var vm = new EditorViewModel(clock: new FakeTimeProvider(Meio), postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        vm.SelectedLine = vm.Lines[linha];
        return vm.Detail!;
    }

    [Fact]
    public async Task O_motivo_do_Modo_revisao_termina_dizendo_que_nada_muda_ate_aceitar()
    {
        var detail = await Abrir(1);

        Assert.Equal("Soa mais natural. Nada muda até você aceitar.", detail.EditorSuggestionReviewReason);
        Assert.Equal("Soa mais natural.", detail.EditorSuggestionReason);
    }

    [Fact]
    public async Task Linha_que_passa_da_caixa_ganha_o_aviso_com_as_linhas_e_a_economia_da_sugestao()
    {
        var detail = await Abrir(0);

        Assert.True(detail.DoesNotFit);
        Assert.True(detail.HasEditorSuggestionFitNote);
        var economia = LineText.Length(Longa) - LineText.Length(Curta);
        Assert.Equal($"Passa 1 linha: a caixa tem 3 linhas. A sugestão economiza {economia} caracteres.", detail.EditorSuggestionFitNote);
    }
}
