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

    [Theory]
    [InlineData("… “ ・・・ ♪ ő Ａ café †")]
    public void E_idempotente(string texto)
    {
        var encoder = TextEncoderRegistry.Default.Create("cp1252");

        var uma = UnwritableCharacters.Fix(texto, encoder);

        Assert.Equal(uma, UnwritableCharacters.Fix(uma, encoder));
    }
}
