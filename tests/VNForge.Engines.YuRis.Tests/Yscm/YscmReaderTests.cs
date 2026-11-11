using System.Buffers.Binary;
using VNForge.Core;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Yscm;

namespace VNForge.Engines.YuRis.Tests.Yscm;

public class YscmReaderTests
{
    private const string ReturnCodeCommand = "RETURNCODE";
    private const string GosubCommand = "GOSUB";

    private const string Source = "ysc.ybn";

    // Montado à mão, fora do gerador: v500, "WORD" com o parâmetro "T" (tipo 2) e "_" sem parâmetros, 37 mensagens
    // de erro vazias (um byte 0 cada) e a tabela final de 256 bytes zerados.
    private static readonly string GoldenHex =
        "5953434D" + "F4010000" + "02000000" + "00000000"
        + "574F524400" + "01" + "5400" + "02" + "00"
        + "5F00" + "00"
        + new string('0', 2 * 37)
        + new string('0', 2 * 256);

    private static YscmFile Read(byte[] bytes) => YscmReader.Read(bytes, Source);

    private static InvalidFileException ReadFails(byte[] bytes) => Assert.Throws<InvalidFileException>(() => Read(bytes));

    // ---- Opcodes ----

    [Fact]
    public void GetOpcode_NomeRepetido_ErroDizQueOOpcodeEAmbiguo()
    {
        var file = Read(new SyntheticYscmBuilder().AddCommand("WORD").AddCommand("CG").AddCommand("WORD").Build().Bytes);

        var error = Assert.Throws<InvalidFileException>(() => file.GetOpcode("WORD"));

        Assert.Contains("'WORD'", error.Reason);
        Assert.Contains("ambíguo", error.Reason);
        Assert.Contains("0, 2", error.Reason);
        Assert.Equal((byte)1, file.GetOpcode("CG"));
    }

    // ---- Arquivo inválido ----

    [Theory]
    [InlineData(0u)]
    [InlineData(199u)]
    [InlineData(600u)]
    [InlineData(uint.MaxValue)]
    public void Read_VersaoForaDaFaixa_LancaVersaoDesconhecida(uint version)
    {
        var bytes = new SyntheticYscmBuilder().WithVersion(version).WithTextCommands().Build().Bytes;

        var error = Assert.Throws<UnknownVersionException>(() => Read(bytes));

        Assert.Equal(version.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Version.Split(',')[0]);
        Assert.Equal(Source, error.Path);
    }

    [Fact]
    public void Read_MaisDe256Comandos_Erro()
    {
        var bytes = new SyntheticYscmBuilder().WithTextCommands().Build().Bytes;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 257);

        var error = ReadFails(bytes);

        Assert.Contains("257", error.Reason);
        Assert.Contains("256", error.Reason);
    }

    [Fact]
    public void Read_ContagemMaiorQueOsComandosDoArquivo_ErroNomeiaOComandoQueFaltou()
    {
        // Sem cauda, o terceiro comando declarado não existe.
        var bytes = new SyntheticYscmBuilder().AddCommand("WORD").AddCommand("CG").Build().Bytes[..32];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 3);

        var error = ReadFails(bytes);

        Assert.Contains("comando 2", error.Reason);
    }

    [Fact]
    public void Read_VetorDeOuro()
    {
        var file = Read(Convert.FromHexString(GoldenHex));

        Assert.Equal(500u, file.Version);
        Assert.Equal(37, file.ErrorMessageCount);
        Assert.Collection(
            file.Commands,
            word =>
            {
                Assert.Equal((byte)0, word.Id);
                Assert.Equal("WORD", word.Name);
                Assert.Equal(new YscmArgument("T", 2, 0), Assert.Single(word.Arguments));
            },
            expression =>
            {
                Assert.Equal((byte)1, expression.Id);
                Assert.Equal("_", expression.Name);
                Assert.Empty(expression.Arguments);
            });
    }
}
