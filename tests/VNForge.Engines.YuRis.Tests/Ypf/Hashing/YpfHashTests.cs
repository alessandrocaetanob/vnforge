using System.Text;
using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Tests.Ypf.Hashing;

public class YpfHashTests
{
    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    // Adler32: "Wikipedia" = 0x11E60398 (https://en.wikipedia.org/wiki/Adler-32); vazio = 1 (RFC 1950, seção 8.2).
    // Os demais valores conferem com zlib.adler32 do Python.
    [Theory]
    [InlineData("", 0x00000001u)]
    [InlineData("Wikipedia", 0x11E60398u)]
    [InlineData("a", 0x00620062u)]
    [InlineData("abc", 0x024D0127u)]
    [InlineData("123456789", 0x091E01DEu)]
    public void Adler32_RetornaVetoresConhecidos(string text, uint expected)
    {
        Assert.Equal(expected, YpfHash.Adler32(Ascii(text)));
    }

    [Fact]
    public void Adler32_ProcessaEntradaMaiorQueUmBlocoDeReducao()
    {
        // 100.000 bytes 0xFF exigem várias reduções módulo 65521; valor de zlib.adler32 do Python.
        var data = new byte[100_000];
        Array.Fill(data, (byte)0xFF);

        Assert.Equal(0x149A302Cu, YpfHash.Adler32(data));
    }

    // CRC-32/ISO-HDLC: check value de "123456789" = 0xCBF43926 (catálogo de CRCs de Greg Cook).
    // "The quick brown fox..." = 0x414FA339 (zlib.crc32 do Python).
    [Theory]
    [InlineData("", 0x00000000u)]
    [InlineData("123456789", 0xCBF43926u)]
    [InlineData("The quick brown fox jumps over the lazy dog", 0x414FA339u)]
    public void Crc32_RetornaVetoresConhecidos(string text, uint expected)
    {
        Assert.Equal(expected, YpfHash.Crc32(Ascii(text)));
    }

    // MurmurHash2 de 32 bits (Austin Appleby), valor de verificação do SMHasher: 0x27864C1E.
    // Chaves {0}, {0,1}, ... com seed 256-i; os hashes (little-endian) são hasheados com seed 0.
    // Fonte: https://github.com/aappleby/smhasher (VerificationTest em KeysetTest.cpp, tabela em main.cpp).
    [Fact]
    public void MurmurHash2_PassaNaVerificacaoDoSmhasher()
    {
        var key = new byte[256];
        var hashes = new byte[256 * 4];
        for (var i = 0; i < 256; i++)
        {
            key[i] = (byte)i;
            var hash = YpfHash.MurmurHash2(key.AsSpan(0, i), (uint)(256 - i));
            BitConverter.TryWriteBytes(hashes.AsSpan(i * 4, 4), hash);
        }

        Assert.Equal(0x27864C1Eu, YpfHash.MurmurHash2(hashes));
    }

    [Fact]
    public void MurmurHash2_EntradaVaziaComSeedZeroRetornaZero()
    {
        Assert.Equal(0u, YpfHash.MurmurHash2([]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public void MurmurHash2_IncorporaOUltimoByteDaCauda(int length)
    {
        var data = Enumerable.Range(1, length).Select(i => (byte)i).ToArray();
        var changed = (byte[])data.Clone();
        changed[^1] ^= 0x01;

        Assert.NotEqual(YpfHash.MurmurHash2(data), YpfHash.MurmurHash2(changed));
    }
}
