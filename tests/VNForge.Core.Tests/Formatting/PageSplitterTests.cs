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
    public void Fala_de_quatro_linhas_vira_duas_paginas_que_cabem()
    {
        var split = Split(Words(16));

        Assert.True(split.Changed);
        Assert.Equal(2, split.Pages);
        Assert.Equal(1, split.Added);
        Assert.Equal($@"{Words(12)}\p{Words(4)}", split.Text);

        var preview = Preview(split.Text);
        Assert.Equal([3, 1], preview.Boxes.Select(box => box.Count));
        Assert.All(preview.Boxes, box => Assert.True(box.Count <= 3));
    }

    [Fact]
    public void Fala_que_ja_cabe_nao_muda()
    {
        var text = Words(12);

        var split = Split(text);

        Assert.False(split.Changed);
        Assert.Equal(text, split.Text);
        Assert.Equal(1, split.Pages);
        Assert.Equal(0, split.Added);
    }

    [Fact]
    public void Divide_antes_do_virar_da_pagina_que_o_original_ja_tem_no_fim()
    {
        var split = Split($@"{Words(16)}\p");

        Assert.Equal($@"{Words(12)}\p{Words(4)}\p", split.Text);
        Assert.Equal(2, split.Pages);
    }

    [Fact]
    public void Fala_de_sete_linhas_vira_tres_paginas()
    {
        var split = Split(Words(28));

        Assert.Equal(3, split.Pages);
        Assert.Equal(2, split.Added);
        Assert.Equal($@"{Words(12)}\p{Words(12)}\p{Words(4)}", split.Text);
        Assert.All(Preview(split.Text).Boxes, box => Assert.True(box.Count <= 3));
    }

    [Fact]
    public void Quebra_do_autor_no_fim_da_ultima_linha_que_cabe_vira_virada_de_pagina()
    {
        var split = Split(@"um\ndois\ntres\nquatro");

        Assert.Equal(@"um\ndois\ntres\pquatro", split.Text);
        Assert.Equal(2, split.Pages);
    }

    [Fact]
    public void Espacos_antes_da_quebra_do_autor_saem_junto()
    {
        var split = Split(@"um \ndois   \ntres  \nquatro");

        Assert.Equal(@"um \ndois   \ntres\pquatro", split.Text);
    }

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
    public void Dividir_duas_vezes_nao_muda_mais_nada()
    {
        var once = Split(Words(16));

        var twice = Split(once.Text);

        Assert.False(twice.Changed);
        Assert.Equal(once.Text, twice.Text);
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

    // O texto gravado é a tradução como a pessoa a escreveu: a divisão só acrescenta o marcador de página, no lugar do espaço da emenda ou de uma quebra do autor.
    private static void AssertSoAcrescentouPaginas(string original, PageSplit split, FormattingProfile profile)
    {
        Assert.True(split.Changed);
        Assert.Equal(original, split.Text.Replace(@"\p", " ", StringComparison.Ordinal));
        Assert.All(LinePreviewer.Create(profile, Unit(), split.Text).Boxes, box => Assert.True(box.Count <= 3));
    }

    [Fact]
    public void Reticencias_do_perfil_nao_sao_gravadas_na_traducao()
    {
        var profile = Box with { Typography = new TypographyRule(Ellipsis: EllipsisStyle.Character) };
        var text = $"bbbbbbbbb... {Words(15)}...";

        var split = Split(text, profile);

        AssertSoAcrescentouPaginas(text, split, profile);
        Assert.DoesNotContain("…", split.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Espacos_colapsados_pelo_perfil_continuam_como_a_pessoa_escreveu()
    {
        var profile = Box with { Spacing = new SpacingRule(CollapseSpaces: true) };
        var text = $"aaaaaaaaa   aaaaaaaaa  {Words(14)}";

        var split = Split(text, profile);

        AssertSoAcrescentouPaginas(text, split, profile);
        Assert.StartsWith("aaaaaaaaa   aaaaaaaaa  ", split.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Largura_cheia_do_perfil_nao_e_gravada_na_traducao()
    {
        var profile = Box with { Wrap = Box.Wrap with { CharWidth = CharWidthMode.HalfWidthLatin }, Width = new WidthRule(WidthMode.Full) };
        var text = Words(8);

        var split = Split(text, profile);

        AssertSoAcrescentouPaginas(text, split, profile);
        Assert.Equal(2, split.Pages);
    }

    [Fact]
    public void Substituicao_do_perfil_nao_e_gravada_na_traducao()
    {
        var profile = Box with { Replacements = new ReplacementRules([new Replacement("Tokyo", "Tóquio")], []) };
        var text = $"Tokyo {Words(15)}";

        var split = Split(text, profile);

        AssertSoAcrescentouPaginas(text, split, profile);
        Assert.StartsWith("Tokyo ", split.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Regra_de_substituicao_que_expande_e_aplicada_so_uma_vez_na_medida()
    {
        // "a" vira "aa" no build; como a tradução gravada continua com "a", o texto dividido é medido uma vez só e cabe.
        var profile = Box with { Replacements = new ReplacementRules([new Replacement("a", "aa")], []) };
        var text = Words(8);

        var split = Split(text, profile);

        Assert.True(split.Changed);
        Assert.Equal(text, split.Text.Replace(@"\p", " ", StringComparison.Ordinal));
        Assert.All(LinePreviewer.Create(profile, Unit(), split.Text).Boxes, box => Assert.True(box.Count <= 3));
    }

    [Fact]
    public void Sem_limite_de_linhas_nao_muda()
    {
        var profile = Box with { Wrap = Box.Wrap with { MaxLines = null } };

        var split = Split(Words(40), profile);

        Assert.False(split.Changed);
    }

    [Fact]
    public void Texto_vazio_nao_muda()
    {
        var split = Split(string.Empty);

        Assert.False(split.Changed);
        Assert.Equal(string.Empty, split.Text);
    }

    [Fact]
    public void Marcador_invalido_vira_VNForgeException()
    {
        Assert.Throws<VNForgeException>(() => Split(@"texto \x invalido"));
    }

    [Fact]
    public void Palavra_unica_maior_que_a_caixa_e_cortada_e_dividida()
    {
        var split = Split(new string('b', 200));

        Assert.True(split.Changed);
        Assert.All(Preview(split.Text).Boxes, box => Assert.True(box.Count <= 3));
        Assert.Equal(new string('b', 200), split.Text.Replace(@"\p", string.Empty, StringComparison.Ordinal));
    }
}
