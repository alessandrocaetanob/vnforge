using VNForge.App.Services;
using VNForge.Core.Dictionaries;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A sugestão de uma linha pede à IA o original que o lote leria, com o pré-dicionário do projeto (#539).</summary>
public sealed class PreDictionarySuggesterTests : IDisposable
{
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    private sealed class Capturing : ILineSuggester
    {
        public TextUnit? Received { get; private set; }

        public Task<LineSuggestion> SuggestAsync(TextUnit unit, CancellationToken ct)
        {
            Received = unit;
            return Task.FromResult(new LineSuggestion("Oi", FromFakeProvider: true));
        }
    }

    private static TextUnit Line(string source) => new("a:000001", TextUnitKind.Dialogue, "蓮", source, null, TranslationStatus.Untranslated);

    [Fact]
    public async Task O_original_vai_normalizado_para_a_IA()
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveFormatting(FormattingProfile.Default with { Dictionary = new DictionaryRules([new DictionaryRule("ぼ、ぼく", "ぼく")], []) });
        var inner = new Capturing();

        await PreDictionarySuggester.For(inner, root).SuggestAsync(Line("ぼ、ぼく、行く"), TestContext.Current.CancellationToken);

        Assert.Equal("ぼく、行く", inner.Received!.Source);
        Assert.Equal("a:000001", inner.Received.Id);
    }

    [Fact]
    public async Task Sem_regra_no_pre_ou_sem_projeto_a_linha_vai_como_esta()
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var inner = new Capturing();
        var line = Line("ぼ、ぼく、行く");

        Assert.Same(inner, PreDictionarySuggester.For(inner, root));
        Assert.Same(inner, PreDictionarySuggester.For(inner, null));
        await inner.SuggestAsync(line, TestContext.Current.CancellationToken);
        Assert.Same(line, inner.Received);
    }
}
