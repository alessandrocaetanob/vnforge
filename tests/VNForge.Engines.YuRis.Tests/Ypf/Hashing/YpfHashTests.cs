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
    public void Crc32_RetornaVetoresConhecidos(string text, uint expected)
    {
        Assert.Equal(expected, YpfHash.Crc32(Ascii(text)));
    }

    // MurmurHash2 com cauda de 1, 2 e 3 bytes (e bloco mais cauda de 1): valores de uma implementação de referência em Python
    // que também passa na verificação do SMHasher abaixo.
    [Theory]
    [InlineData("A", 0x25F31569u)]
    [InlineData("ab", 0x1AA14063u)]
    [InlineData("abc", 0x13577C9Bu)]
    [InlineData("abcde", 0x5F09A8DEu)]
    public void MurmurHash2_RetornaVetoresConhecidosParaCadaTamanhoDeCauda(string text, uint expected)
    {
        Assert.Equal(expected, YpfHash.MurmurHash2(Ascii(text)));
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
}
