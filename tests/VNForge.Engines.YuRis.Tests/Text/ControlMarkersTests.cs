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

    // ---- Código EF desconhecido ----

    [Fact]
    public void EF_no_fim_dos_bytes_vira_marcador_RAW_e_volta()
    {
        var raw = Join(Sjis("あ"), Bytes(0xEF));

        var text = ControlMarkers.ToMarkers(raw);

        Assert.Equal("あ{RAW:EF}", text);
        Assert.Equal(raw, ToBytes(text));
    }

    // ---- Barras e chaves que já existem no texto ----

    // ---- Limites de caractere do Shift-JIS ----

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
