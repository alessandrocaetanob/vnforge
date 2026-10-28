using VNForge.Core.Text;
using VNForge.Engines.YuRis.Text;

namespace VNForge.Engines.YuRis.Tests.Text;

/// <summary>
/// Na <c>cp1252</c> o exe do jogo ainda lê <c>0xE0</c> a <c>0xFC</c> como lead byte: a letra acentuada se junta ao byte seguinte, e um <c>EF</c> de controle
/// nessa posição se perde (#435). O <see cref="ControlMarkers.ToBytes"/> grava um <c>0x20</c> entre a letra e o <c>EF</c>; texto comum depois do acento, espaço incluído, não muda.
/// </summary>
public class Cp1252LeadByteSpacingTests
{
    private static readonly ITextEncoder Cp1252 = new Cp1252TextEncoder();

    private static byte[] Cp(string text) => GameEncodings.Windows1252.GetBytes(text);

    private static byte[] ToBytes(string text) => ControlMarkers.ToBytes(text, Cp1252);

    [Theory]
    [InlineData("á", 0xE1, "\\n", 0xF0)]
    [InlineData("é", 0xE9, "\\n", 0xF0)]
    [InlineData("ê", 0xEA, "\\n", 0xF0)]
    [InlineData("á", 0xE1, "\\p", 0xF2)]
    [InlineData("é", 0xE9, "\\p", 0xF2)]
    [InlineData("ê", 0xEA, "\\p", 0xF2)]
    [InlineData("á", 0xE1, "\\c", 0xF3)]
    [InlineData("é", 0xE9, "\\c", 0xF3)]
    [InlineData("ê", 0xEA, "\\c", 0xF3)]
    [InlineData("é", 0xE9, "\\u", 0xF5)]
    [InlineData("é", 0xE9, "{EF:F7}", 0xF7)]
    public void Controle_depois_de_letra_acentuada_ganha_um_espaco_entre_os_dois(string accent, byte accentByte, string marker, byte code)
    {
        var bytes = ToBytes("a" + accent + marker + "b");

        Assert.Equal([(byte)'a', accentByte, 0x20, 0xEF, code, (byte)'b'], bytes);
    }

    [Fact]
    public void Quebra_de_linha_do_relato_da_teste_13_sai_com_o_espaco_antes_do_EF_F0()
    {
        var bytes = ToBytes("do que já\\nera...?");

        Assert.Equal([.. "do que j"u8, 0xE1, 0x20, 0xEF, 0xF0, .. "era...?"u8], bytes);
    }

    [Fact]
    public void Controle_depois_de_pontuacao_ou_de_letra_sem_acento_continua_sem_espaco()
    {
        Assert.Equal([.. "antes agitada,"u8, 0xEF, 0xF0, .. "mergulhou"u8], ToBytes("antes agitada,\\nmergulhou"));
        Assert.Equal([.. "ate"u8, 0xEF, 0xF0, .. "ha"u8], ToBytes("ate\\nha"));
    }

    [Fact]
    public void Espaco_depois_de_letra_acentuada_sai_como_a_pagina_1252_grava()
    {
        Assert.Equal([.. "s"u8, 0xF3, 0x20, .. "na teoria"u8], ToBytes("só na teoria"));
        Assert.Equal([0xE9, 0x20, 0x20, (byte)'x'], ToBytes("é  x"));
    }

    [Theory]
    [InlineData("éramos")]
    [InlineData("está")]
    [InlineData("àquela")]
    [InlineData("coração")]
    public void Letra_depois_do_acento_sai_como_a_pagina_1252_grava(string text)
    {
        Assert.Equal(Cp(text), ToBytes(text));
    }

    [Fact]
    public void Dois_acentos_seguidos_formam_o_par_e_o_controle_que_vem_depois_fica_como_esta()
    {
        // O motor lê (E3 E9) como um caractere; o EF seguinte já começa um controle.
        Assert.Equal([0xE3, 0xE9, 0xEF, 0xF0], ToBytes("ãé\\n"));
        Assert.Equal([0xE3, 0xE9, 0xE1, 0x20, 0xEF, 0xF0], ToBytes("ãéá\\n"));
    }

    [Fact]
    public void Acento_no_fim_do_texto_nao_ganha_espaco()
    {
        Assert.Equal([.. "caf"u8, 0xE9], ToBytes("café"));
    }

    [Theory]
    [InlineData("ß", 0xDF)]
    [InlineData("ý", 0xFD)]
    [InlineData("ÿ", 0xFF)]
    public void Byte_fora_da_faixa_de_lead_byte_nao_ganha_espaco(string text, byte letter)
    {
        Assert.Equal([letter, 0xEF, 0xF0], ToBytes(text + "\\n"));
    }

    [Fact]
    public void Bytes_crus_do_marcador_RAW_nao_sao_mexidos()
    {
        Assert.Equal([0xE9, 0xEF, 0xF0], ToBytes("{RAW:E9}\\n"));
        Assert.Equal([0xEF, 0xF0, 0xE9, 0x20], ToBytes("\\n{RAW:E9}{RAW:20}"));
    }

    [Fact]
    public void Dois_controles_seguidos_nao_ganham_espaco_entre_eles()
    {
        Assert.Equal([0xE9, 0x20, 0xEF, 0xF0, 0xEF, 0xF2], ToBytes("é\\n\\p"));
    }

    [Fact]
    public void Acento_antes_do_delimitador_do_falante_da_cp1252_nao_ganha_espaco()
    {
        // « (AB) e » (BB) são texto comum para o motor: a evidência do #62 vale para eles.
        Assert.Equal([.. "Jos"u8, 0xE9, 0xAB], ToBytes("José«"));
        Assert.Equal([.. "oi"u8, 0xE1, 0xBB], ToBytes("oiá»"));
    }

    [Theory]
    [InlineData("sjis")]
    [InlineData("fold")]
    public void Shift_JIS_e_fold_gravam_os_bytes_de_sempre(string strategy)
    {
        var encoder = TextEncoderRegistry.Default.Create(strategy);

        Assert.Equal([.. "ate"u8, 0xEF, 0xF0, .. "ha  x"u8], ControlMarkers.ToBytes("ate\\nha  x", encoder));
        if (strategy == "fold")
        {
            Assert.Equal([.. "ate"u8, 0xEF, 0xF0, .. "ha  x"u8], ControlMarkers.ToBytes("até\\nhá  x", encoder));
        }
    }
}
