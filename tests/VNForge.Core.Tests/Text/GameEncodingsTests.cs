using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public class GameEncodingsTests
{
    // Bytes conferidos na tabela da página 932 da Microsoft (bestfit932.txt):
    // こ=82B1 ん=82F1 に=82C9 ち=82BF は=82CD; 日=93FA 本=967B 語=8CEA; 「=8175 」=8176.
    [Theory]
    [InlineData("こんにちは", "82B182F182C982BF82CD")]
    [InlineData("日本語", "93FA967B8CEA")]
    [InlineData("「A日」", "81754193FA8176")]
    public void ShiftJis_CodificaEDecodificaTextoJapones(string texto, string bytesHex)
    {
        byte[] esperado = Convert.FromHexString(bytesHex);

        Assert.Equal(esperado, GameEncodings.ShiftJis.GetBytes(texto));
        Assert.Equal(texto, GameEncodings.ShiftJis.GetString(esperado));
    }

    [Fact]
    public void ShiftJis_UsaPagina932()
    {
        Assert.Equal(932, GameEncodings.ShiftJis.CodePage);
    }

    [Fact]
    public void Windows1252_UsaPagina1252()
    {
        Assert.Equal(1252, GameEncodings.Windows1252.CodePage);
    }

    [Fact]
    public void Windows1252_CodificaAcentosEEuro()
    {
        // 0xE7 = ç, 0xE3 = ã, 0x80 = € (em Latin-1 puro, 0x80 seria um controle)
        byte[] esperado = [0x61, 0xE7, 0xE3, 0x6F, 0x80];

        Assert.Equal(esperado, GameEncodings.Windows1252.GetBytes("ação€"));
        Assert.Equal("ação€", GameEncodings.Windows1252.GetString(esperado));
    }

    [Fact]
    public void AcessosRepetidos_DevolvemAMesmaInstancia()
    {
        // Sanidade: a thread-safety do registro vem do construtor estático, não deste teste.
        var instancias = new System.Text.Encoding[16];

        Parallel.For(0, instancias.Length, i => instancias[i] = GameEncodings.ShiftJis);

        Assert.All(instancias, e => Assert.Same(GameEncodings.ShiftJis, e));
    }
}
