using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Formatting;

[Trait("Risco", "R4")]
public sealed class PageSplitterTests
{
    // Caixa de 3 linhas e 40 colunas; a palavra de 9 letras faz 4 palavras por linha (39 colunas).
    private static readonly FormattingProfile Box = FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: 40, MaxLines: 3, CharWidth: CharWidthMode.Uniform) };

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("aaaaaaaaa", count));

    private static TextUnit Unit(TextUnitKind kind = TextUnitKind.Dialogue) =>
        new("a:000001", kind, null, "原文", null, TranslationStatus.Untranslated);

    private static PageSplit Split(string text, FormattingProfile? profile = null, TextUnitKind kind = TextUnitKind.Dialogue) =>
        PageSplitter.Split(profile ?? Box, Unit(kind), text);

    private static LinePreview Preview(string text) => LinePreviewer.Create(Box, Unit(), text);

    [Fact]
    public void Divide_so_a_caixa_que_nao_cabe()
    {
        var split = Split($@"curta\p{Words(16)}\p");

        Assert.Equal($@"curta\p{Words(12)}\p{Words(4)}\p", split.Text);
        Assert.Equal(3, split.Pages);
        Assert.Equal(1, split.Added);
    }

    [Fact]
    public void Sem_quebra_automatica_divide_pelas_quebras_do_autor()
    {
        var profile = Box with { Wrap = Box.Wrap with { Enabled = false } };

        var split = Split(@"um\ndois\ntres\nquatro\ncinco", profile);

        Assert.Equal(@"um\ndois\ntres\pquatro\ncinco", split.Text);
    }

    [Fact]
    public void Sem_limite_de_linhas_nao_muda()
    {
        var profile = Box with { Wrap = Box.Wrap with { MaxLines = null } };

        var split = Split(Words(40), profile);

        Assert.False(split.Changed);
    }
}
