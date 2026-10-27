using System.Buffers.Binary;
using VNForge.Core.Text;

namespace VNForge.Engines.YuRis.Tests.Yscfg.Synthetic;

/// <summary>
/// Monta um <c>yscfg.ybn</c> sintético: <c>YSCF</c>, a versão, a configuração e, no fim, o tamanho do título em 2 bytes e o título em
/// Shift-JIS (docs/06-yuris-notas-tecnicas.md). A configuração é a do arquivo de referência da versão 485 (68 bytes), sem o título do
/// jogo, que não entra no repositório.
/// </summary>
public static class SyntheticYscfgBuilder
{
    /// <summary>Os 68 bytes entre a versão e o tamanho do título no arquivo de referência (106 bytes, título de 28).</summary>
    public const string ReferenceConfigHex =
        "0000000000000000000400000003000001000000010203040500000001020000000000000000000001000000010000000100000000000000000000000000000000000000";

    public static byte[] Build(string title, uint version = 485, string configHex = ReferenceConfigHex) =>
        Build(GameEncodings.ShiftJis.GetBytes(title), version, configHex);

    public static byte[] Build(byte[] titleBytes, uint version = 485, string configHex = ReferenceConfigHex)
    {
        var config = Convert.FromHexString(configHex);
        var bytes = new byte[8 + config.Length + 2 + titleBytes.Length];
        "YSCF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), version);
        config.CopyTo(bytes, 8);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8 + config.Length), checked((ushort)titleBytes.Length));
        titleBytes.CopyTo(bytes, 8 + config.Length + 2);
        return bytes;
    }
}
