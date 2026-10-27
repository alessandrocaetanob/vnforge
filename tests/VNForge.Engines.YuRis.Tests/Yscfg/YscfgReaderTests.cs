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
    public void Read_LeOTituloNoFimDoArquivo()
    {
        var file = Read(SyntheticYscfgBuilder.Build(Title));

        Assert.Equal(485u, file.Version);
        Assert.Equal(Title, file.Title);
    }

    [Fact]
    public void Read_ArquivoNoTamanhoDoJogoDeReferencia()
    {
        // O jogo de referência tem 106 bytes: 8 de cabeçalho, 68 de configuração, 2 do tamanho e 28 do título.
        var bytes = SyntheticYscfgBuilder.Build(new string('あ', 14));

        Assert.Equal(106, bytes.Length);
        var file = Read(bytes);
        Assert.Equal(new string('あ', 14), file.Title);
        Assert.Equal(76, file.TitleOffset);
    }

    [Fact]
    public void Read_TituloVazio()
    {
        var file = Read(SyntheticYscfgBuilder.Build(string.Empty));

        Assert.Equal(string.Empty, file.Title);
    }

    [Fact]
    public void Read_TituloAscii()
    {
        Assert.Equal("My Game", Read(SyntheticYscfgBuilder.Build("My Game")).Title);
    }

    [Fact]
    public void Read_SemAssinatura_Recusa()
    {
        var bytes = SyntheticYscfgBuilder.Build(Title);
        bytes[0] = (byte)'X';

        var ex = Assert.Throws<InvalidFileException>(() => Read(bytes));

        Assert.Contains("YSCF", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_VersaoForaDaFaixa_Recusa()
    {
        Assert.Throws<UnknownVersionException>(() => Read(SyntheticYscfgBuilder.Build(Title, version: 9000)));
    }

    [Fact]
    public void Read_TituloTruncado_Recusa()
    {
        var bytes = SyntheticYscfgBuilder.Build(Title);

        var ex = Assert.Throws<InvalidFileException>(() => Read(bytes[..^2]));

        Assert.Contains("título", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_SobraDepoisDoTitulo_Recusa()
    {
        var bytes = SyntheticYscfgBuilder.Build(Title).Concat(new byte[] { 0x41, 0x42 }).ToArray();

        Assert.Throws<InvalidFileException>(() => Read(bytes));
    }

    [Fact]
    public void Read_TituloGravadoEmCp1252_LeComoWindows1252()
    {
        // O build com a estratégia cp1252 grava "Título" com o í em 0xED, que o Shift-JIS estrito recusa.
        var file = Read(SyntheticYscfgBuilder.Build(VNForge.Core.Text.GameEncodings.Windows1252.GetBytes("Título")));

        Assert.Equal("Título", file.Title);
        Assert.False(file.TitleIsShiftJis);
    }

    [Fact]
    public void Read_TituloEmShiftJis_MarcaShiftJis()
    {
        Assert.True(Read(SyntheticYscfgBuilder.Build(Title)).TitleIsShiftJis);
    }

    [Fact]
    public void Read_TituloComByteDeControle_Recusa()
    {
        Assert.Throws<InvalidFileException>(() => Read(SyntheticYscfgBuilder.Build([0x41, 0x01, 0x42])));
    }

    [Fact]
    public void Read_ArquivoCurto_Recusa()
    {
        Assert.Throws<InvalidFileException>(() => Read("YSCF"u8.ToArray()));
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
