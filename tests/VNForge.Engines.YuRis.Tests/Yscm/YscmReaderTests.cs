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

    [Fact]
    public void Read_ComandosNaOrdemDoId_ComParametros()
    {
        var yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

        var file = Read(yscm.Bytes);

        Assert.Equal(yscm.Commands.Count, file.Commands.Count);
        for (var i = 0; i < yscm.Commands.Count; i++)
        {
            Assert.Equal((byte)i, file.Commands[i].Id);
            Assert.Equal(yscm.Commands[i].Name, file.Commands[i].Name);
            Assert.Equal(yscm.Commands[i].Arguments.Select(a => a.Name), file.Commands[i].Arguments.Select(a => a.Name));
        }

        Assert.Equal(["LABEL", "PINT1", "PSTR2"], file.Commands[yscm.IdOf(GosubCommand)].Arguments.Select(a => a.Name));
        Assert.Equal(yscm.ErrorMessageCount, file.ErrorMessageCount);
    }

    [Fact]
    public void Read_GuardaTipoEFlagsDoParametro()
    {
        var yscm = new SyntheticYscmBuilder()
            .AddCommandWithArguments("WORD", new SyntheticYscmArgument("TEXT", 7, 9))
            .Build();

        var argument = Assert.Single(Assert.Single(Read(yscm.Bytes).Commands).Arguments);

        Assert.Equal(new YscmArgument("TEXT", 7, 9), argument);
    }

    [Fact]
    public void Read_DecodificaNomesEmShiftJis()
    {
        var yscm = new SyntheticYscmBuilder()
            .AddCommand("名前", "台詞")
            .Build();

        var command = Assert.Single(Read(yscm.Bytes).Commands);

        Assert.Equal("名前", command.Name);
        Assert.Equal("台詞", Assert.Single(command.Arguments).Name);
    }

    [Theory]
    [InlineData(200u)]
    [InlineData(299u)]
    [InlineData(500u)]
    [InlineData(599u)]
    public void Read_AceitaQualquerVersaoDaFaixaConhecida(uint version)
    {
        var yscm = new SyntheticYscmBuilder().WithVersion(version).WithTextCommands().Build();

        Assert.Equal(version, Read(yscm.Bytes).Version);
    }

    [Theory]
    [InlineData(37)]
    [InlineData(0x90)]
    public void Read_AceitaAsDuasQuantidadesDeMensagensDeErro(int errorMessages)
    {
        var yscm = new SyntheticYscmBuilder().WithTextCommands().WithErrorMessageCount(errorMessages).Build();

        var file = Read(yscm.Bytes);

        Assert.Equal(yscm.Commands.Count, file.Commands.Count);
        Assert.Equal(errorMessages, file.ErrorMessageCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(36)]
    [InlineData(38)]
    [InlineData(0x8F)]
    [InlineData(0x91)]
    public void Read_OutraQuantidadeDeMensagensDeErro_ErroCitaAContagemEncontrada(int errorMessages)
    {
        var bytes = new SyntheticYscmBuilder().WithTextCommands().WithErrorMessageCount(errorMessages).Build().Bytes;

        var error = ReadFails(bytes);

        Assert.Contains($"{errorMessages} mensagens de erro", error.Reason);
        Assert.Contains("37 ou 144", error.Reason);
        Assert.Contains("pode estar errada", error.Reason);
    }

    [Fact]
    public void Read_AceitaListaVaziaEListaDe256Comandos()
    {
        Assert.Empty(Read(new SyntheticYscmBuilder().Build().Bytes).Commands);

        var full = new SyntheticYscmBuilder().AddFillerCommands(256).Build();
        Assert.Equal((byte)255, Read(full.Bytes).Commands[^1].Id);
    }

    [Fact]
    public void Read_ViaFluxo_DaOMesmoResultadoQueViaBytes()
    {
        var yscm = new SyntheticYscmBuilder().WithTextCommands().Build();
        using var stream = new MemoryStream(yscm.Bytes);

        var file = YscmReader.Read(stream);

        Assert.Equal("<fluxo>", file.Source);
        Assert.Equal(yscm.Commands.Select(c => c.Name), file.Commands.Select(c => c.Name));
    }

    [Fact]
    public void ReadFile_LeDoDisco_ECitaOCaminhoNoErro()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vnforge-ysc-{Guid.NewGuid():N}.ybn");
        try
        {
            File.WriteAllBytes(path, new SyntheticYscmBuilder().AddCommand("WORD").Build().Bytes);

            var file = YscmReader.ReadFile(path);

            Assert.Equal(path, file.Source);
            var error = Assert.Throws<InvalidFileException>(() => file.GetOpcode(ReturnCodeCommand));
            Assert.Contains(path, error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- Opcodes ----

    [Fact]
    public void GetOpcodes_DevolveOIdDeCadaComando()
    {
        var yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

        var opcodes = Read(yscm.Bytes).GetOpcodes();

        Assert.Equal(
            new YuRisOpcodes(
                Word: yscm.IdOf("WORD"),
                Expression: yscm.IdOf("_"),
                ReturnCode: yscm.IdOf(ReturnCodeCommand),
                Gosub: yscm.IdOf(GosubCommand),
                Cg: yscm.IdOf("CG")),
            opcodes);
        Assert.Equal(
            [(byte)6, (byte)3, (byte)7, (byte)4, (byte)9],
            [opcodes.Word, opcodes.Expression, opcodes.ReturnCode, opcodes.Gosub, opcodes.Cg]);
    }

    [Fact]
    public void GetOpcodes_AchaOsComandosEmOutraOrdem()
    {
        var yscm = new SyntheticYscmBuilder()
            .AddCommand("CG")
            .AddCommand(ReturnCodeCommand)
            .AddCommand(GosubCommand)
            .AddCommand("_")
            .AddCommand("WORD")
            .Build();

        var opcodes = Read(yscm.Bytes).GetOpcodes();

        Assert.Equal(new YuRisOpcodes(Word: 4, Expression: 3, ReturnCode: 1, Gosub: 2, Cg: 0), opcodes);
    }

    [Theory]
    [InlineData("WORD")]
    [InlineData("_")]
    [InlineData(ReturnCodeCommand)]
    [InlineData(GosubCommand)]
    [InlineData("CG")]
    public void GetOpcodes_ComandoAusente_ErroNomeiaOComando(string missing)
    {
        var builder = new SyntheticYscmBuilder();
        foreach (var name in new[] { "WORD", "_", ReturnCodeCommand, GosubCommand, "CG" }.Where(n => n != missing))
        {
            builder.AddCommand(name);
        }

        var file = Read(builder.Build().Bytes);

        var error = Assert.Throws<InvalidFileException>(() => file.GetOpcodes());
        Assert.Contains($"'{missing}'", error.Reason);
        Assert.Contains(Source, error.Message);
    }

    [Fact]
    public void GetOpcodes_VariosAusentes_ErroNomeiaTodos()
    {
        var file = Read(new SyntheticYscmBuilder().AddCommand("WORD").AddCommand(GosubCommand).Build().Bytes);

        var error = Assert.Throws<InvalidFileException>(() => file.GetOpcodes());

        Assert.Contains("'_'", error.Reason);
        Assert.Contains("'RETURNCODE'", error.Reason);
        Assert.Contains("'CG'", error.Reason);
        Assert.DoesNotContain("'WORD'", error.Reason);
        Assert.DoesNotContain("'GOSUB'", error.Reason);
    }

    [Fact]
    public void GetOpcode_ComandoAusente_ErroNomeiaOComando()
    {
        var file = Read(new SyntheticYscmBuilder().WithTextCommands().Build().Bytes);

        var error = Assert.Throws<InvalidFileException>(() => file.GetOpcode("ES.SEL.SET"));

        Assert.Contains("'ES.SEL.SET'", error.Reason);
    }

    [Fact]
    public void GetOpcode_DiferenciaCaixa()
    {
        var file = Read(new SyntheticYscmBuilder().AddCommand("WORD").Build().Bytes);

        Assert.False(file.TryGetOpcode("word", out _));
        Assert.Throws<InvalidFileException>(() => file.GetOpcode("word"));
    }

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

    [Fact]
    public void Read_ArquivoCurto_Erro()
    {
        var error = ReadFails([0x59, 0x53, 0x43]);

        Assert.Contains("precisa de pelo menos 8", error.Reason);
    }

    [Fact]
    public void Read_AssinaturaErrada_Erro()
    {
        var bytes = new SyntheticYscmBuilder().WithTextCommands().Build().Bytes;
        bytes[0] = (byte)'X';

        var error = ReadFails(bytes);

        Assert.Contains("YSCM", error.Reason);
        Assert.Contains(Source, error.Message);
    }

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
    public void Read_TruncadoNoMeioDoNomeDoComando_Erro()
    {
        var bytes = new SyntheticYscmBuilder().AddCommand("WORD", "TEXT").Build().Bytes;

        // 16 do cabeçalho + "WOR": falta o fim do nome e o byte 0.
        var error = ReadFails(bytes[..19]);

        Assert.Contains("o nome do comando 0", error.Reason);
        Assert.Contains("byte 0", error.Reason);
    }

    [Fact]
    public void Read_TruncadoNoMeioDeUmParametro_ErroNomeiaComandoEParametro()
    {
        var bytes = new SyntheticYscmBuilder().AddCommand("WORD", "TEXT").Build().Bytes;

        // 16 do cabeçalho, o nome do comando com o zero final, a contagem, o nome do parâmetro com o zero final e o tipo: falta o byte de flags.
        var error = ReadFails(bytes[..28]);

        Assert.Contains("parâmetro 0", error.Reason);
        Assert.Contains("'WORD'", error.Reason);
    }

    [Fact]
    public void Read_ContagemDeParametrosDaUltimaEntradaMaisUm_Erro()
    {
        var bytes = new SyntheticYscmBuilder().AddCommand("CG", "FILE").Build().Bytes;
        bytes[16 + "CG".Length + 1] = 2;

        var error = ReadFails(bytes);

        Assert.Contains("pode estar errada", error.Reason);
    }

    [Fact]
    public void Read_ContagemDeParametrosDeUmComandoDoMeioMaisUm_Erro()
    {
        var bytes = new SyntheticYscmBuilder().AddCommand("WORD", "TEXT").AddCommand("CG", "FILE").Build().Bytes;
        bytes[16 + "WORD".Length + 1] = 2;

        ReadFails(bytes);
    }

    [Fact]
    public void Read_ContagemDeComandosMaisUm_Erro()
    {
        var bytes = new SyntheticYscmBuilder().WithTextCommands().Build().Bytes;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 11);

        ReadFails(bytes);
    }

    [Fact]
    public void Read_ContagemDeComandosMenosUm_ErroNaCauda()
    {
        var bytes = new SyntheticYscmBuilder().WithTextCommands().Build().Bytes;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 9);

        var error = ReadFails(bytes);

        Assert.Contains("pode estar errada", error.Reason);
    }

    [Fact]
    public void Read_SemATabelaFinalCompleta_Erro()
    {
        var bytes = new SyntheticYscmBuilder().WithTextCommands().Build().Bytes;

        var error = ReadFails(bytes[..^1]);

        Assert.Contains("256", error.Reason);
        Assert.Contains("cauda não fecha", error.Reason);
    }

    [Fact]
    public void Read_ByteSobrandoDepoisDaTabela_Erro()
    {
        var bytes = new SyntheticYscmBuilder().WithTextCommands().Build().Bytes.Append((byte)0).ToArray();

        ReadFails(bytes);
    }

    [Fact]
    public void Read_SemCauda_Erro()
    {
        var yscm = new SyntheticYscmBuilder().WithTextCommands().WithErrorMessageCount(0).WithTableSize(0).Build();

        var error = ReadFails(yscm.Bytes);

        Assert.Contains("cauda não fecha", error.Reason);
    }
}
