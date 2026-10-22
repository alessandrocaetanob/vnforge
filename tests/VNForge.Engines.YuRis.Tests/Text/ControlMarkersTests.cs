using System.Text;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Text;

namespace VNForge.Engines.YuRis.Tests.Text;

public class ControlMarkersTests
{
    private static byte[] Sjis(string text) => GameEncodings.ShiftJis.GetBytes(text);

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] Bytes(params byte[] values) => values;

    private static byte[] ToBytes(string text) => ControlMarkers.ToBytes(text, new StrictSjisEncoder());

    // ---- Códigos conhecidos ----

    [Theory]
    [InlineData(0xF0, "\\n")]
    [InlineData(0xF2, "\\p")]
    [InlineData(0xF3, "\\c")]
    [InlineData(0xF5, "\\u")]
    public void Codigo_conhecido_vira_marcador_e_volta(byte code, string marker)
    {
        var raw = Join(Sjis("あ"), Bytes(0xEF, code), Sjis("い"));

        var text = ControlMarkers.ToMarkers(raw);

        Assert.Equal("あ" + marker + "い", text);
        Assert.Equal(raw, ToBytes(text));
    }

    [Fact]
    public void Ida_e_volta_com_todos_os_codigos_gera_os_mesmos_bytes()
    {
        var raw = Join(
            Sjis("今日は、"), Bytes(0xEF, 0xF0),
            Sjis("いい天気だね。"), Bytes(0xEF, 0xF3),
            Sjis("ABC abc 123"), Bytes(0xEF, 0xF2),
            Bytes(0xEF, 0xF5), Bytes(0xEF, 0xF0),
            Sjis("ｱｲｳ"));

        var text = ControlMarkers.ToMarkers(raw);

        Assert.Equal("今日は、\\nいい天気だね。\\cABC abc 123\\p\\u\\nｱｲｳ", text);
        Assert.Equal(raw, ToBytes(text));
    }

    [Fact]
    public void Bytes_vazios_viram_texto_vazio_e_voltam()
    {
        Assert.Equal(string.Empty, ControlMarkers.ToMarkers([]));
        Assert.Empty(ToBytes(string.Empty));
    }

    // ---- Código EF desconhecido ----

    [Fact]
    public void Codigo_EF_desconhecido_vira_marcador_generico_que_preserva_o_byte()
    {
        var raw = Join(Sjis("あ"), Bytes(0xEF, 0xF7), Sjis("い"), Bytes(0xEF, 0x41));

        var text = ControlMarkers.ToMarkers(raw);

        Assert.Equal("あ{EF:F7}い{EF:41}", text);
        Assert.Equal(raw, ToBytes(text));
    }

    [Fact]
    public void EF_no_fim_dos_bytes_vira_marcador_RAW_e_volta()
    {
        var raw = Join(Sjis("あ"), Bytes(0xEF));

        var text = ControlMarkers.ToMarkers(raw);

        Assert.Equal("あ{RAW:EF}", text);
        Assert.Equal(raw, ToBytes(text));
    }

    [Fact]
    public void Marcador_com_codigo_conhecido_em_hexa_gera_os_mesmos_bytes_do_atalho()
    {
        Assert.Equal(Bytes(0xEF, 0xF0), ToBytes("{EF:F0}"));
        Assert.Equal(Bytes(0xEF, 0xF0), ToBytes("{EF:f0}"));
        Assert.Equal(Bytes(0xEF, 0xF0), ToBytes("\\n"));
    }

    // ---- Barras e chaves que já existem no texto ----

    [Fact]
    public void Barra_invertida_literal_sai_escapada_e_nao_vira_marcador()
    {
        var raw = Sjis("a\\nb\\p{x}");

        var text = ControlMarkers.ToMarkers(raw);

        Assert.Equal("a\\\\nb\\\\p\\{x}", text);
        Assert.Equal(raw, ToBytes(text));
    }

    [Fact]
    public void Barra_literal_ao_lado_de_controle_real_continua_distinta()
    {
        var raw = Join(Sjis("\\"), Bytes(0xEF, 0xF0), Sjis("\\"));

        var text = ControlMarkers.ToMarkers(raw);

        Assert.Equal("\\\\\\n\\\\", text);
        Assert.Equal(raw, ToBytes(text));
    }

    [Fact]
    public void Chave_literal_sai_escapada_e_volta()
    {
        var raw = Sjis("{EF:F0}");

        var text = ControlMarkers.ToMarkers(raw);

        Assert.Equal("\\{EF:F0}", text);
        Assert.Equal(raw, ToBytes(text));
    }

    // ---- Limites de caractere do Shift-JIS ----

    [Fact]
    public void EF_como_segundo_byte_de_caractere_de_dois_bytes_nao_e_codigo_de_controle()
    {
        // 0x88EF é um kanji válido na página 932 (segundo byte 0xEF); o 0xF0 que vem depois não forma "EF F0".
        var kanji = Bytes(0x88, 0xEF);
        Assert.Single(GameEncodings.ShiftJis.GetString(kanji));
        var raw = Join(kanji, Bytes(0xF0, 0x41), Sjis("あ"));

        var text = ControlMarkers.ToMarkers(raw);

        Assert.DoesNotContain("\\n", text);
        Assert.DoesNotContain("{EF", text);
        Assert.Equal(raw, ToBytes(text));
    }

    [Fact]
    public void Nenhum_par_com_EF_na_abertura_e_caractere_valido_na_pagina_932()
    {
        var strict = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        for (var trail = 0; trail < 256; trail++)
        {
            Assert.ThrowsAny<DecoderFallbackException>(() => strict.GetString([0xEF, (byte)trail]));
        }
    }

    [Fact]
    public void Dois_caracteres_terminados_em_EF_seguidos_nao_geram_codigo()
    {
        var raw = Join(Bytes(0x88, 0xEF, 0x88, 0xEF), Sjis("あ"));

        var text = ControlMarkers.ToMarkers(raw);

        Assert.Equal(GameEncodings.ShiftJis.GetString(Bytes(0x88, 0xEF, 0x88, 0xEF)) + "あ", text);
        Assert.Equal(raw, ToBytes(text));
    }

    [Fact]
    public void Segundo_byte_igual_a_barra_ou_chave_nao_vira_escape()
    {
        // ソ = 83 5C (segundo byte 0x5C), ボ = 83 7B (segundo byte 0x7B).
        var raw = Join(Bytes(0x83, 0x5C, 0x83, 0x7B), Bytes(0xEF, 0xF2));

        var text = ControlMarkers.ToMarkers(raw);

        Assert.Equal("ソボ\\p", text);
        Assert.Equal(raw, ToBytes(text));
    }

    [Fact]
    public void Byte_EF_logo_apos_caractere_de_um_byte_e_codigo_de_controle()
    {
        var raw = Join(Bytes(0xB1), Bytes(0xEF, 0xF0), Bytes(0xB2));

        var text = ControlMarkers.ToMarkers(raw);

        Assert.Equal("ｱ\\nｲ", text);
        Assert.Equal(raw, ToBytes(text));
    }

    [Fact]
    public void Bytes_sem_representacao_viram_marcador_RAW_e_voltam_identicos()
    {
        // 0x8790 e 0x81E0 decodificam para o mesmo caractere (U+2252) e só um deles faz a volta; 0x81 antes de ASCII é inválido; 0xFD não existe.
        var raw = Join(Bytes(0x87, 0x90), Bytes(0x81, 0x20), Bytes(0xFD), Sjis("あ"));

        var text = ControlMarkers.ToMarkers(raw);

        Assert.Contains("{RAW:", text);
        Assert.Equal(raw, ToBytes(text));
    }

    [Fact]
    public void Todos_os_bytes_possiveis_em_qualquer_posicao_fazem_ida_e_volta()
    {
        var all = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var pairs = new List<byte>();
        for (var lead = 0; lead < 256; lead++)
        {
            for (var trail = 0; trail < 256; trail += 7)
            {
                pairs.Add((byte)lead);
                pairs.Add((byte)trail);
            }
        }

        foreach (var raw in new[] { all, pairs.ToArray() })
        {
            Assert.Equal(raw, ToBytes(ControlMarkers.ToMarkers(raw)));
        }
    }

    // ---- Codificação dos trechos de texto ----

    [Fact]
    public void Os_bytes_de_controle_nao_passam_pelo_codificador()
    {
        var encoder = new RecordingEncoder();

        var bytes = ControlMarkers.ToBytes("ï\\nï{EF:F7}", encoder);

        Assert.Equal(["ï", "ï"], encoder.Received);
        Assert.Equal(Bytes(0xEF, 0xEF, 0xF0, 0xEF, 0xEF, 0xF7), bytes);
    }

    [Fact]
    public void Texto_sem_trecho_nao_chama_o_codificador()
    {
        var encoder = new RecordingEncoder();

        ControlMarkers.ToBytes("\\n\\p", encoder);

        Assert.Empty(encoder.Received);
    }

    [Fact]
    public void Barra_e_chave_escapadas_chegam_ao_codificador_como_caracteres_literais()
    {
        var encoder = new RecordingEncoder();

        ControlMarkers.ToBytes("a\\\\b\\{c", encoder);

        Assert.Equal(["a\\b{c"], encoder.Received);
    }

    // ---- Marcadores inválidos ----

    [Theory]
    [InlineData("\\x")]
    [InlineData("abc\\")]
    [InlineData("\\N")]
    [InlineData("{")]
    [InlineData("{EF:F0")]
    [InlineData("{}")]
    [InlineData("{EF:}")]
    [InlineData("{EF:F}")]
    [InlineData("{EF:F0F0}")]
    [InlineData("{EF:ZZ}")]
    [InlineData("{RAW:}")]
    [InlineData("{RAW:E}")]
    [InlineData("{RAW:GG}")]
    [InlineData("{VAR:x}")]
    [InlineData("{NOME}")]
    public void Marcador_invalido_lanca_FormatException_citando_a_posicao(string text)
    {
        var ex = Assert.Throws<FormatException>(() => ToBytes(text));

        Assert.Contains("posição", ex.Message);
    }

    [Fact]
    public void Argumentos_nulos_lancam_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ControlMarkers.ToBytes(null!, new StrictSjisEncoder()));
        Assert.Throws<ArgumentNullException>(() => ControlMarkers.ToBytes("a", null!));
    }

    private sealed class StrictSjisEncoder : ITextEncoder
    {
        private static readonly Encoding Strict = CreateStrict();

        private static Encoding CreateStrict()
        {
            _ = GameEncodings.ShiftJis;
            return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }

        public string Id => "sjis";

        public byte[] Encode(string text) => Strict.GetBytes(text);

        public IReadOnlyList<int> FindUnencodable(string text) => [];
    }

    private sealed class RecordingEncoder : ITextEncoder
    {
        public List<string> Received { get; } = [];

        public string Id => "recording";

        public byte[] Encode(string text)
        {
            Received.Add(text);
            return text.Select(c => (byte)c).ToArray();
        }

        public IReadOnlyList<int> FindUnencodable(string text) => [];
    }
}
