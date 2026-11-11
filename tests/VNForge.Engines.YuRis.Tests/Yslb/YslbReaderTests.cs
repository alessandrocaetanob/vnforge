using VNForge.Core;
using VNForge.Engines.YuRis.Tests.Yslb.Synthetic;
using VNForge.Engines.YuRis.Yslb;

namespace VNForge.Engines.YuRis.Tests.Yslb;

public class YslbReaderTests
{
    private const string Source = "ysl.ybn";

    // Montado à mão, fora do gerador: v485, um rótulo "A" na instrução 7 do script 2, IF 1 e LOOP 3, com a tabela idx_begin zerada.
    private static readonly string GoldenHex =
        "59534C42" + "E5010000" + "01000000"
        + new string('0', 256 * 8)
        + "01" + "41" + "00000000" + "07000000" + "0200" + "01" + "03";

    private static YslbFile Read(byte[] bytes) => YslbReader.Read(bytes, Source);

    [Fact]
    public void Read_ContagemMaiorQueOArquivo_RecusaSemAlocar()
    {
        var bytes = new SyntheticYslbBuilder().AddLabel("A", 1).Build();
        BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes, 8);

        Assert.Throws<InvalidFileException>(() => Read(bytes));
    }

    [Fact]
    public void Read_SobraDepoisDoUltimoRotulo_Recusa()
    {
        var bytes = new SyntheticYslbBuilder().AddLabel("A", 1).Build();

        var ex = Assert.Throws<InvalidFileException>(() => Read([.. bytes, 0, 0]));

        Assert.Contains("sobram", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_VetorDeOuro()
    {
        var file = Read(Convert.FromHexString(GoldenHex));

        Assert.Equal(485u, file.Version);
        Assert.Equal(new YslbLabel("A", 7, 2, 1, 3), Assert.Single(file.Labels));
    }
}
