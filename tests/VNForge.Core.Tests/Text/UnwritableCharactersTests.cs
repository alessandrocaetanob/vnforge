using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public sealed class UnwritableCharactersTests
{
    public static TheoryData<string, string> Table => new()
    {
        { "“", "\"" }, { "”", "\"" }, { "„", "\"" },
        { "‘", "'" }, { "’", "'" }, { "‚", "'" }, { "‹", "'" }, { "›", "'" },
        { "…", "..." }, { "‥", ".." },
        { "–", "-" }, { "—", "-" }, { "―", "-" }, { "─", "-" },
        { "•", "*" }, { "★", "*" }, { "☆", "*" }, { "※", "*" },
        { "™", "TM" },
        { "Š", "S" }, { "š", "s" }, { "Ž", "Z" }, { "ž", "z" },
        { "Œ", "OE" }, { "œ", "oe" }, { "Ÿ", "Y" }, { "ƒ", "f" },
        { "ˆ", "^" }, { "˜", "~" },
        { "ï", "i" },
        { "♪", "~" }, { "♫", "~" }, { "♥", "~" }, { "♡", "~" },
        { "～", "~" }, { "〜", "~" },
        { "・", " " }, { "・・・", "..." },
        { "「", "\"" }, { "」", "\"" }, { "『", "\"" }, { "』", "\"" },
        { "→", "->" }, { "←", "<-" },
        { "　", " " },
        { "（", "(" }, { "）", ")" }, { "！", "!" }, { "？", "?" }, { "Ａ", "A" }, { "１", "1" },
    };

    public static TheoryData<string, string> Accents => new()
    {
        { "ő", "o" }, { "ā", "a" }, { "ě", "e" }, { "Ő", "O" },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Troca_cada_item_da_tabela_na_cp1252(string original, string esperado)
    {
        var encoder = TextEncoderRegistry.Default.Create("cp1252");

        var corrigido = UnwritableCharacters.Fix($"a{original}b", encoder);

        Assert.Equal($"a{esperado}b", corrigido);
        Assert.Empty(encoder.FindUnencodable(corrigido));
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void Na_sjis_troca_so_o_que_ela_nao_grava(string original, string esperado)
    {
        var encoder = TextEncoderRegistry.Default.Create("sjis");
        var grava = encoder.FindUnencodable(original).Count == 0;

        var corrigido = UnwritableCharacters.Fix($"a{original}b", encoder);

        Assert.Equal(grava ? $"a{original}b" : $"a{esperado}b", corrigido);
    }

    [Theory]
    [MemberData(nameof(Accents))]
    public void Troca_a_letra_acentuada_fora_da_pagina_pela_letra_sem_o_acento(string original, string esperado)
    {
        var encoder = TextEncoderRegistry.Default.Create("cp1252");

        Assert.Equal($"a{esperado}b", UnwritableCharacters.Fix($"a{original}b", encoder));
    }

    [Fact]
    public void Na_sjis_o_acento_fica_para_a_pessoa_decidir()
    {
        var encoder = TextEncoderRegistry.Default.Create("sjis");

        Assert.Equal("aő ação b", UnwritableCharacters.Fix("aő ação b", encoder));
        Assert.Null(UnwritableCharacters.SubstituteFor(0x00E7, encoder));
    }

    [Fact]
    public void Na_sjis_o_que_ela_grava_fica()
    {
        var encoder = TextEncoderRegistry.Default.Create("sjis");

        Assert.Equal("a♪～「」b", UnwritableCharacters.Fix("a♪～「」b", encoder));
    }

    [Fact]
    public void Nao_toca_marcador_de_controle()
    {
        var encoder = TextEncoderRegistry.Default.Create("cp1252");

        var corrigido = UnwritableCharacters.Fix("a…\\n{VAR:x…}♪\\p", encoder);

        Assert.Equal("a...\\n{VAR:x…}~\\p", corrigido);
    }

    [Fact]
    public void Marcador_invalido_deixa_o_texto_como_esta()
    {
        var encoder = TextEncoderRegistry.Default.Create("cp1252");

        Assert.Equal("a… \\z", UnwritableCharacters.Fix("a… \\z", encoder));
    }

    [Fact]
    public void Caractere_sem_substituto_fica_como_esta()
    {
        var encoder = TextEncoderRegistry.Default.Create("cp1252");

        Assert.Equal("a † ‡ ‰ \U0001F600 亜 b", UnwritableCharacters.Fix("a † ‡ ‰ \U0001F600 亜 b", encoder));
    }

    [Fact]
    public void So_troca_quando_o_substituto_e_gravavel()
    {
        var semAspasRetas = new StubEncoder(c => c < 0x80 && c != '"');
        var soAscii = new StubEncoder(c => c < 0x80);

        Assert.Equal("“x”", UnwritableCharacters.Fix("“x”", semAspasRetas));
        Assert.Equal("\"x\"", UnwritableCharacters.Fix("“x”", soAscii));
    }

    [Theory]
    [InlineData("… “ ・・・ ♪ ő Ａ café †")]
    [InlineData("。、【x】")]
    public void E_idempotente(string texto)
    {
        var encoder = TextEncoderRegistry.Default.Create("cp1252");

        var uma = UnwritableCharacters.Fix(texto, encoder);

        Assert.Equal(uma, UnwritableCharacters.Fix(uma, encoder));
    }

    [Fact]
    public void Mantem_a_pontuacao_japonesa_da_tabela_antiga()
    {
        var encoder = TextEncoderRegistry.Default.Create("cp1252");

        Assert.Equal("Oi. (x), [y]", UnwritableCharacters.Fix("Oi。 （x）、 【y】", encoder));
    }

    [Fact]
    public void SubstituteFor_diz_o_que_o_corretor_usaria()
    {
        var encoder = TextEncoderRegistry.Default.Create("cp1252");

        Assert.Equal("...", UnwritableCharacters.SubstituteFor(0x2026, encoder));
        Assert.Equal(" ", UnwritableCharacters.SubstituteFor(0x30FB, encoder));
        Assert.Equal("o", UnwritableCharacters.SubstituteFor(0x0151, encoder));
        Assert.Null(UnwritableCharacters.SubstituteFor('a', encoder));
        Assert.Null(UnwritableCharacters.SubstituteFor(0x2020, encoder));
    }

    private sealed class StubEncoder(Func<char, bool> writes) : ITextEncoder
    {
        public string Id => "stub";

        public byte[] Encode(string text) => throw new NotSupportedException();

        public IReadOnlyList<int> FindUnencodable(string text) =>
            [.. Enumerable.Range(0, text.Length).Where(i => !writes(text[i]))];
    }
}
