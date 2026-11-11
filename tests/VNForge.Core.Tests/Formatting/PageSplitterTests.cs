using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Formatting;

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
    public void Mantem_os_marcadores_do_texto()
    {
        var text = $@"\c{{VAR:nome}} {Words(15)}";

        var split = Split(text);

        Assert.Equal(ControlMarkers.Markers(text).Append(@"\p"), ControlMarkers.Markers(split.Text));
        Assert.All(Preview(split.Text).Boxes, box => Assert.True(box.Count <= 3));
    }

    [Fact]
    public void Barra_invertida_escapada_antes_de_n_nao_e_quebra_de_linha()
    {
        var split = Split($@"\\n {Words(16)}");

        Assert.Equal(2, split.Pages);
        Assert.StartsWith(@"\\n aaaaaaaaa", split.Text);
        Assert.All(Preview(split.Text).Boxes, box => Assert.True(box.Count <= 3));
    }

    [Fact]
    public void Sem_quebra_automatica_divide_pelas_quebras_do_autor()
    {
        var profile = Box with { Wrap = Box.Wrap with { Enabled = false } };

        var split = Split(@"um\ndois\ntres\nquatro\ncinco", profile);

        Assert.Equal(@"um\ndois\ntres\pquatro\ncinco", split.Text);
    }

    [Fact]
    public void Papel_que_o_build_nao_formata_e_medido_como_esta()
    {
        var long16 = Words(16);

        Assert.False(Split(long16, kind: TextUnitKind.Name).Changed);
        Assert.Equal(@"um\ndois\ntres\pquatro", Split(@"um\ndois\ntres\nquatro", kind: TextUnitKind.Name).Text);
    }

    [Fact]
    public void Sem_limite_de_linhas_nao_muda()
    {
        var profile = Box with { Wrap = Box.Wrap with { MaxLines = null } };

        var split = Split(Words(40), profile);

        Assert.False(split.Changed);
    }

    [Fact]
    public void Marcador_invalido_vira_VNForgeException()
    {
        Assert.Throws<VNForgeException>(() => Split(@"texto \x invalido"));
    }
}
