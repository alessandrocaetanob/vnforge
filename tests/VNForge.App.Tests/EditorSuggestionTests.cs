using Microsoft.Extensions.Time.Testing;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A sugestão da segunda passada de editor (#532) no Editor: o ícone na grade, o cartão do inspetor, aceitar, ignorar e o fim dela quando a linha muda.</summary>
[Trait("Risco", "R2")]
public sealed class EditorSuggestionTests : IDisposable
{
    private static readonly DateTimeOffset Meio = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(Meio);
    private readonly string raiz;

    public EditorSuggestionTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste");
        ProjectFolder.Open(raiz).SaveText("yst00001",
        [
            Fala(0, "Os dois, cinco minutos atrasados.\\p", TranslationStatus.Machine),
            Fala(1, "Chovia.\\p", TranslationStatus.Machine),
            Fala(2, "Aham.\\p", TranslationStatus.Machine),
            Fala(3, "Ei.\\p", TranslationStatus.Machine),
        ]);
        Salvar(Sugestao(0, "Os dois, cinco minutos atrasados.\\p", "Vocês dois, cinco minutos atrasados.\\p", EditorSuggestionKind.Fluency),
            Sugestao(2, "Aham.\\p", "Uhum.\\p", EditorSuggestionKind.Voice),
            Sugestao(3, "Ei.\\p", "Ei, você.\\p", EditorSuggestionKind.Consistency));
    }

    public void Dispose() => pasta.Dispose();

    private static TextUnit Fala(int n, string traducao, TranslationStatus status) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, "美咲", $"原文{n}\\p", traducao, status);

    private static EditorSuggestion Sugestao(int n, string baseTexto, string proposta, EditorSuggestionKind tipo) =>
        new("yst00001", $"yst00001:{n:D6}", tipo, baseTexto, proposta, "Soa mais natural.", Meio);

    private void Salvar(params EditorSuggestion[] sugestoes) =>
        EditorSuggestionsFile.Save(ProjectFolder.Open(raiz), EditorSuggestionsFile.Empty.ReplacingScript("yst00001", sugestoes));

    private IReadOnlyList<EditorSuggestion> NoArquivo() => EditorSuggestionsFile.Load(ProjectFolder.Open(raiz)).Suggestions;

    private TextUnit Gravada(int n) => ProjectFolder.Open(raiz).LoadText("yst00001")[n];

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    [Fact]
    public async Task Aceitar_grava_a_proposta_como_traducao_da_pessoa_e_tira_a_sugestao_da_grade_e_do_arquivo()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[0];
        var detail = vm.Detail!;

        detail.AcceptEditorSuggestionCommand.Execute(null);

        Assert.Equal("Vocês dois, cinco minutos atrasados.\\p", Gravada(0).Translation);
        Assert.Equal(TranslationStatus.Translated, Gravada(0).Status);
        Assert.False(detail.HasEditorSuggestion);
        Assert.False(vm.Lines[0].HasEditorSuggestion);
        Assert.Equal(["yst00001:000002", "yst00001:000003"], NoArquivo().Select(s => s.UnitId));
        Assert.Equal(2, vm.EditorSuggestionLines);
    }
}
