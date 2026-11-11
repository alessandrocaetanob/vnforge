using VNForge.Core;
using VNForge.Engines.YuRis.Tests.Yscfg.Synthetic;
using VNForge.Engines.YuRis.Yscfg;

namespace VNForge.Engines.YuRis.Tests.Yscfg;

public class YscfgReaderTests
{
    private const string Source = "yscfg.ybn";
    private const string Title = "テストの題名";

    private static YscfgFile Read(byte[] bytes) => YscfgReader.Read(bytes, Source);

    [Fact]
    public void Read_TituloGravadoEmCp1252_LeComoWindows1252()
    {
        // O build com a estratégia cp1252 grava "Título" com o í em 0xED, que o Shift-JIS estrito recusa.
        var file = Read(SyntheticYscfgBuilder.Build(VNForge.Core.Text.GameEncodings.Windows1252.GetBytes("Título")));

        Assert.Equal("Título", file.Title);
        Assert.False(file.TitleIsShiftJis);
    }

    [Fact]
    public void Read_TituloComByteDeControle_Recusa()
    {
        Assert.Throws<InvalidFileException>(() => Read(SyntheticYscfgBuilder.Build([0x41, 0x01, 0x42])));
    }

    [Fact]
    public void ReadFile_LeDoDisco()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vnforge-yscfg-{Guid.NewGuid():N}.ybn");
        try
        {
            File.WriteAllBytes(path, SyntheticYscfgBuilder.Build(Title));

            Assert.Equal(Title, YscfgReader.ReadFile(path).Title);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
