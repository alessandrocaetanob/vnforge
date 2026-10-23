using System.Buffers.Binary;
using VNForge.Core;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Ystb;
using VNForge.Engines.YuRis.Yscm;

namespace VNForge.Engines.YuRis.Tests.Ystb;

public class YstbReaderTests
{
    private const string ResourcesDir = "recursos";
    private const string ReturnCodeCommand = "RETURNCODE";

    private const string Source = "yst00001.ybn";

    // Montado à mão, fora do gerador, sobre o YSCM de WithTextCommands (WORD = 6, RETURNCODE = 7), v500, chave 0xD36FAC96.
    // Em claro: instruções 06 01 00 00 | 07 01 00 00; argumentos (id 0, tipo 3, size 3, offset 0) e (id 0, tipo 1, size 5,
    // offset 0); recursos 82 A0 41 ("あA"); linhas 1 e 2. Cada seção é cifrada de novo desde o início, com D3 6F AC 96.
    private static readonly string GoldenHex =
        "59535442" + "F4010000" + "02000000" + "08000000" + "18000000" + "03000000" + "08000000" + "00000000"
        + "D56EAC96D46EAC96"
        + "D36FAF96D06FAC96D36FAC96D36FAD96D66FAC96D36FAC96"
        + "51CFED"
        + "D26FAC96D16FAC96";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private static readonly YscmFile Commands = YscmReader.Read(Yscm.Bytes, "ysc.ybn");

    private static SyntheticYstbBuilder NewBuilder() => new(Yscm);

    private static YstbFile Read(byte[] bytes) => YstbReader.Read(bytes, Source, Commands);

    private static InvalidFileException ReadFails(byte[] bytes) => Assert.Throws<InvalidFileException>(() => Read(bytes));

    private static SyntheticYstb TextScript(uint key) => NewBuilder()
        .WithKey(key)
        .AddWord("こんにちは", 9)
        .AddCommand("_", 10, new SyntheticYstbArgument(0, 3, 0, 0, 0))
        .AddWord("さようなら", 11)
        .AddReturnCode(7, 12)
        .Build();

    private static void SetUInt32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    [Fact]
    public void Read_VetorDeOuro()
    {
        var file = Read(Convert.FromHexString(GoldenHex));

        Assert.Equal(500u, file.Version);
        Assert.Equal(0xD36FAC96u, file.Key);
        Assert.Equal(YstbKeyOrigin.Derived, file.KeyOrigin);
        Assert.Equal(0u, file.Padding);
        Assert.Equal(2, file.ArgumentCount);
        Assert.Equal(new byte[] { 0x82, 0xA0, 0x41 }, file.Resources.ToArray());
        Assert.Collection(
            file.Instructions,
            word =>
            {
                Assert.Equal((byte)6, word.Opcode);
                Assert.Equal("WORD", word.CommandName);
                Assert.Equal(1u, word.LineNumber);
                Assert.Equal(new YstbArgument(0, 3, 0, 3, 0), Assert.Single(word.Arguments));
                Assert.Equal("あA", GameEncodings.ShiftJis.GetString(file.GetResource(word.Arguments[0]).Span));
            },
            returnCode =>
            {
                Assert.Equal((byte)7, returnCode.Opcode);
                Assert.Equal(ReturnCodeCommand, returnCode.CommandName);
                Assert.Equal(2u, returnCode.LineNumber);
                Assert.Equal(1, returnCode.FirstArgumentIndex);
                Assert.Equal(new YstbArgument(0, 1, 0, 5, 0), Assert.Single(returnCode.Arguments));
            });
    }

    [Fact]
    public void Gerador_ProduzOVetorDeOuro()
    {
        var generated = NewBuilder()
            .AddCommand("WORD", 1, new SyntheticYstbArgument(0, 3, 0, 3, 0))
            .AddResource([0x82, 0xA0, 0x41])
            .AddReturnCode(5, 2)
            .Build();

        Assert.Equal(GoldenHex, Convert.ToHexString(generated.Bytes));
    }

    [Theory]
    [InlineData(0xD36FAC96u)]
    [InlineData(0x07B4024Au)]
    public void Read_ChaveConhecida_DerivadaDosBytes8A11(uint key)
    {
        var script = TextScript(key);

        var file = Read(script.Bytes);

        Assert.Equal(key, file.Key);
        Assert.Equal(YstbKeyOrigin.Derived, file.KeyOrigin);
        Assert.Contains(key, YstbReader.KnownKeys);
        AssertMatchesScript(script, file);
    }

    [Fact]
    public void Read_ChaveForaDaLista_DerivadaDosBytes8A11()
    {
        const uint key = 0x5A17C3E9;
        var script = TextScript(key);

        var file = Read(script.Bytes);

        Assert.DoesNotContain(key, YstbReader.KnownKeys);
        Assert.Equal(key, file.Key);
        Assert.Equal(YstbKeyOrigin.Derived, file.KeyOrigin);
        AssertMatchesScript(script, file);
    }

    [Theory]
    [InlineData(0xD36FAC96u)]
    [InlineData(0x07B4024Au)]
    public void Read_PrimeiroArgumentoComOffsetDiferenteDeZero_UsaAChaveConhecida(uint key)
    {
        var script = NewBuilder()
            .WithKey(key)
            .AddCommand(ReturnCodeCommand, 1, new SyntheticYstbArgument(0, 1, 0, 3, 0x01020304))
            .AddWord("テキスト", 2)
            .Build();

        var file = Read(script.Bytes);

        Assert.Equal(key, file.Key);
        Assert.Equal(YstbKeyOrigin.Known, file.KeyOrigin);
        AssertMatchesScript(script, file);
    }

    [Fact]
    public void Read_ChaveConhecidaEDerivadaQuaseCerta_PrefereAConhecida()
    {
        // O offset 0x00010000 muda só o byte 2 da chave derivada (0xD36FAD96). Sem argumento conferido que pegue o desvio
        // (RETURNCODE não é conferido), a derivada passaria; só a ordem "conhecidas antes" devolve a chave real.
        var script = NewBuilder()
            .WithKey(0xD36FAC96)
            .AddCommand(ReturnCodeCommand, 1, new SyntheticYstbArgument(0, 1, 0, 3, 0x00010000))
            .AddReturnCode(4, 2)
            .Build();

        var file = Read(script.Bytes);

        Assert.Equal(0xD36FAC96u, file.Key);
        Assert.Equal(YstbKeyOrigin.Known, file.KeyOrigin);
        AssertMatchesScript(script, file);
    }

    [Fact]
    public void Read_ArgumentoDeComandoComumForaDosRecursos_Erro()
    {
        var script = NewBuilder().AddCommand("_", 1, new SyntheticYstbArgument(0, 3, 0, 5, 0)).AddResource([1, 2, 3]).Build();

        var error = ReadFails(script.Bytes);

        Assert.Contains("do comando _", error.Reason);
        Assert.Contains(ResourcesDir, error.Reason);
    }

    [Fact]
    public void Read_ArgumentoDeTamanhoZero_AceitaQualquerOffset()
    {
        var script = NewBuilder().AddCommand("_", 1, new SyntheticYstbArgument(0, 3, 0, 0, 0xFFFFFFF0)).Build();

        var file = Read(script.Bytes);

        Assert.Empty(file.GetResource(file.Instructions[0].Arguments[0]).ToArray());
    }

    [Fact]
    public void Read_IfElseComTresArgumentos_SoOPrimeiroEConferido()
    {
        var wild = new SyntheticYstbArgument(1, 0, 0, 9000, 77);
        var script = NewBuilder()
            .AddCommand("IF", 1, new SyntheticYstbArgument(0, 3, 0, 0, 0), wild, wild)
            .AddCommand("ELSE", 2, new SyntheticYstbArgument(0, 3, 0, 0, 0), wild, wild)
            .Build();

        AssertMatchesScript(script, Read(script.Bytes));
    }

    [Theory]
    [InlineData("IF")]
    [InlineData("ELSE")]
    public void Read_IfElseComTresArgumentos_PrimeiroForaDosRecursos_Erro(string name)
    {
        var script = NewBuilder().AddCommand(name, 1, new SyntheticYstbArgument(0, 3, 0, 4, 0), default, default).Build();

        Assert.Contains($"do comando {name}", ReadFails(script.Bytes).Reason);
    }

    [Fact]
    public void Read_IfComDoisArgumentos_ConferePeloPadrao()
    {
        var script = NewBuilder()
            .AddCommand("IF", 1, new SyntheticYstbArgument(0, 3, 0, 0, 0), new SyntheticYstbArgument(1, 0, 0, 9000, 77))
            .Build();

        Assert.Contains("do comando IF", ReadFails(script.Bytes).Reason);
    }

    [Fact]
    public void Read_Loop_SegundoArgumentoNaoEConferido()
    {
        var script = NewBuilder()
            .AddCommand("LOOP", 1, new SyntheticYstbArgument(0, 3, 0, 0, 0), new SyntheticYstbArgument(1, 0, 0, 9000, 77))
            .Build();
        AssertMatchesScript(script, Read(script.Bytes));

        var bad = NewBuilder().AddCommand("LOOP", 1, new SyntheticYstbArgument(0, 3, 0, 4, 0), default).Build();
        Assert.Contains("do comando LOOP", ReadFails(bad.Bytes).Reason);
    }

    [Fact]
    public void KnownKeys_NaoPodeSerAlteradaPorCast()
    {
        var keys = (IList<uint>)YstbReader.KnownKeys;

        Assert.Throws<NotSupportedException>(() => keys[0] = 1);
        Assert.Equal([0xD36FAC96u, 0x07B4024Au], YstbReader.KnownKeys);
    }

    [Fact]
    public void Read_ChaveForaDaListaEPrimeiroOffsetDiferenteDeZero_FalhaNomeandoAChave()
    {
        var script = NewBuilder()
            .WithKey(0x5A17C3E9)
            .AddCommand(ReturnCodeCommand, 1, new SyntheticYstbArgument(0, 1, 0, 3, 0x01020304))
            .AddWord("テキスト", 2)
            .Build();

        var error = ReadFails(script.Bytes);

        Assert.Contains("nenhuma chave decifrou", error.Reason);
        Assert.Contains("0xD36FAC96", error.Reason);
        Assert.Contains("0x07B4024A", error.Reason);
    }

    [Fact]
    public void Read_SemCifra_ChaveZero()
    {
        var script = TextScript(0);

        var file = Read(script.Bytes);

        Assert.Equal(0u, file.Key);
        AssertMatchesScript(script, file);
    }

    [Fact]
    public void Read_SemArgumentos_ChaveConhecidaNasInstrucoes()
    {
        var script = NewBuilder()
            .WithKey(0xD36FAC96)
            .AddCommand("ELSE", 4)
            .AddCommand("RETURN", 5)
            .Build();

        var file = Read(script.Bytes);

        Assert.Equal(YstbKeyOrigin.Known, file.KeyOrigin);
        Assert.Equal(0xD36FAC96u, file.Key);
        Assert.Equal(["ELSE", "RETURN"], file.Instructions.Select(i => i.CommandName));
    }

    [Fact]
    public void Read_SemArgumentosEEmClaro_SemChave()
    {
        var script = NewBuilder().WithKey(0).AddCommand("ELSE", 4).Build();

        var file = Read(script.Bytes);

        Assert.Equal(YstbKeyOrigin.None, file.KeyOrigin);
        Assert.Equal(0u, file.Key);
        Assert.Equal("ELSE", Assert.Single(file.Instructions).CommandName);
    }

    [Fact]
    public void Read_ScriptVazio()
    {
        var file = Read(NewBuilder().Build().Bytes);

        Assert.Empty(file.Instructions);
        Assert.Equal(0, file.ArgumentCount);
        Assert.Equal(YstbKeyOrigin.None, file.KeyOrigin);
    }

    [Fact]
    public void Read_GuardaOQueAReescritaPrecisa()
    {
        var script = NewBuilder().WithPadding(0xCAFEBABE).AddWord("あ", 3).AddReturnCode(1, 4, 0x77).Build();

        var file = Read(script.Bytes);

        Assert.Equal(0xCAFEBABEu, file.Padding);
        Assert.Equal(script.PlainArguments, file.ArgumentBytes.ToArray());
        Assert.Equal(script.PlainResources, file.Resources.ToArray());
        Assert.Equal(SyntheticYstbBuilder.KeyV290, file.Key);
    }

    [Fact]
    public void Read_GuardaExtraENumerosDeLinha()
    {
        var script = NewBuilder()
            .AddInstruction(Yscm.IdOf("GOSUB"), 0x4321, 123456, new SyntheticYstbArgument(0, 3, 0, 0, 0), new SyntheticYstbArgument(1, 1, 8, 0, 10))
            .AddReturnCode(0, 0xFFFFFFFF)
            .Build();

        var file = Read(script.Bytes);

        var gosub = file.Instructions[0];
        Assert.Equal((ushort)0x4321, gosub.Extra);
        Assert.Equal(123456u, gosub.LineNumber);
        Assert.Equal(new YstbArgument(1, 1, 8, 0, 10), gosub.Arguments[1]);
        Assert.Equal(0xFFFFFFFFu, file.Instructions[1].LineNumber);
        Assert.Equal(2, file.Instructions[1].FirstArgumentIndex);
    }

    [Fact]
    public void Read_ArgumentoDeReturnCodeComOffsetForaDosRecursos_Aceita()
    {
        var script = NewBuilder().AddWord("あ", 1).AddReturnCode(1000, 2, 0xFFFFFF00).Build();

        var file = Read(script.Bytes);

        var returnCode = file.Instructions[1].Arguments[0];
        Assert.Equal(0xFFFFFF00u, returnCode.Offset);
        var error = Assert.Throws<InvalidFileException>(() => file.GetResource(returnCode));
        Assert.Contains(ResourcesDir, error.Reason);
    }

    [Fact]
    public void Read_PelaApiDeFluxoEDeArquivo()
    {
        var script = TextScript(SyntheticYstbBuilder.KeyV290);
        using (var stream = new MemoryStream(script.Bytes))
        {
            AssertMatchesScript(script, YstbReader.Read(stream, Commands));
        }

        var path = Path.Combine(Path.GetTempPath(), $"vnforge-{Guid.NewGuid():N}.ybn");
        File.WriteAllBytes(path, script.Bytes);
        try
        {
            var file = YstbReader.ReadFile(path, Commands);
            Assert.Equal(path, file.Source);
            AssertMatchesScript(script, file);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(299u)]
    [InlineData(290u)]
    [InlineData(200u)]
    public void Read_VersaoAbaixoDe300_ErroDizendoQueOLayoutNaoESuportado(uint version)
    {
        var script = NewBuilder().WithVersion(version).AddWord("あ", 1).Build();

        var error = ReadFails(script.Bytes);

        Assert.Contains("layout", error.Reason);
        Assert.Contains("não é suportado", error.Reason);
        Assert.Contains(version.ToString(), error.Reason);
    }

    [Theory]
    [InlineData(300u)]
    [InlineData(599u)]
    public void Read_VersoesNosLimitesDaFaixa(uint version)
    {
        var file = Read(NewBuilder().WithVersion(version).AddWord("あ", 1).Build().Bytes);

        Assert.Equal(version, file.Version);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(199u)]
    [InlineData(600u)]
    [InlineData(uint.MaxValue)]
    public void Read_VersaoForaDaFaixa_VersaoDesconhecida(uint version)
    {
        var script = NewBuilder().WithVersion(version).AddWord("あ", 1).Build();

        Assert.Throws<UnknownVersionException>(() => Read(script.Bytes));
    }

    [Fact]
    public void Read_NaoComecaComYstb_Erro()
    {
        var bytes = TextScript(SyntheticYstbBuilder.KeyV290).Bytes;
        bytes[0] = (byte)'X';

        Assert.Contains("assinatura", ReadFails(bytes).Reason);
    }

    [Fact]
    public void Read_ArquivoVazio_Erro()
    {
        Assert.Contains("pelo menos 8", ReadFails([]).Reason);
    }

    [Fact]
    public void Read_ContagemDeInstrucoesIncoerenteComOTamanho_Erro()
    {
        var bytes = TextScript(SyntheticYstbBuilder.KeyV290).Bytes;
        SetUInt32(bytes, 8, 5);

        var error = ReadFails(bytes);

        Assert.Contains("instruções", error.Reason);
        Assert.Contains("5", error.Reason);
    }

    [Fact]
    public void Read_NumerosDeLinhaComTamanhoErrado_Erro()
    {
        var bytes = TextScript(SyntheticYstbBuilder.KeyV290).Bytes;
        SetUInt32(bytes, 0x18, 4);

        Assert.Contains("números de linha", ReadFails(bytes).Reason);
    }

    [Fact]
    public void Read_SecaoDeArgumentosForaDeMultiploDe12_Erro()
    {
        var bytes = TextScript(SyntheticYstbBuilder.KeyV290).Bytes;
        SetUInt32(bytes, 0x10, 13);

        Assert.Contains("múltiplo de 12", ReadFails(bytes).Reason);
    }

    [Fact]
    public void Read_TamanhosQueNaoFecham_ComOTamanhoDoArquivo_ErroComAsPosicoes()
    {
        var bytes = TextScript(SyntheticYstbBuilder.KeyV290).Bytes;
        SetUInt32(bytes, 0x14, 1);

        var error = ReadFails(bytes);

        Assert.Contains($"o arquivo tem {bytes.Length}", error.Reason);
    }

    [Fact]
    public void Read_BytesSobrandoNoFim_Erro()
    {
        var bytes = TextScript(SyntheticYstbBuilder.KeyV290).Bytes.Concat(new byte[] { 0 }).ToArray();

        Assert.Contains($"o arquivo tem {bytes.Length}", ReadFails(bytes).Reason);
    }

    [Fact]
    public void Read_ContagensGigantes_ErroSemAlocar()
    {
        var bytes = TextScript(SyntheticYstbBuilder.KeyV290).Bytes;
        SetUInt32(bytes, 8, 0x3FFFFFFF);
        SetUInt32(bytes, 0xC, 0xFFFFFFFC);
        SetUInt32(bytes, 0x10, 0xFFFFFFF0);
        SetUInt32(bytes, 0x14, uint.MaxValue);
        SetUInt32(bytes, 0x18, 0xFFFFFFFC);

        var error = ReadFails(bytes);

        Assert.Contains($"o arquivo tem {bytes.Length}", error.Reason);
    }

    [Fact]
    public void Read_TamanhosQueEstouramInt_Erro()
    {
        var script = NewBuilder().Build();
        var bytes = script.Bytes;
        SetUInt32(bytes, 0x14, uint.MaxValue);

        Assert.Contains($"o arquivo tem {bytes.Length}", ReadFails(bytes).Reason);
    }

    [Fact]
    public void Read_OpcodeForaDoYscm_ErroComPosicao()
    {
        var script = NewBuilder().AddWord("あ", 1).AddInstruction(200, 0, 2).AddReturnCode(0, 3).Build();

        var error = ReadFails(script.Bytes);

        Assert.Contains("instrução 1", error.Reason);
        Assert.Contains("opcode 200", error.Reason);
        Assert.Contains("posição 36", error.Reason);
    }

    [Fact]
    public void Read_InstrucaoComMaisArgumentosQueOsRestantes_Erro()
    {
        var script = NewBuilder().AddWord("あ", 1).Build();

        // O mesmo script com a contagem de argumentos da instrução elevada de 1 para 2, cifrada com a mesma chave.
        var instructions = SyntheticYstbBuilder.Encrypt([script.Instructions[0].Opcode, 2, 0, 0], script.Key);
        var bytes = script.Bytes;
        instructions.CopyTo(bytes, 32);

        var error = ReadFails(bytes);

        Assert.Contains("declara 2 argumentos", error.Reason);
        Assert.Contains("restam 1", error.Reason);
    }

    [Fact]
    public void Read_ArgumentosSobrandoSemInstrucao_Erro()
    {
        var script = NewBuilder().AddWord("あ", 1).Build();
        var instructions = SyntheticYstbBuilder.Encrypt([script.Instructions[0].Opcode, 0, 0, 0], script.Key);
        var bytes = script.Bytes;
        instructions.CopyTo(bytes, 32);

        var error = ReadFails(bytes);

        Assert.Contains("sobram 1 argumentos sem instrução", error.Reason);
    }

    [Fact]
    public void Read_WordApontandoParaForaDosRecursos_Erro()
    {
        var script = NewBuilder().AddCommand("WORD", 1, new SyntheticYstbArgument(0, 3, 0, 10, 0)).AddResource([1, 2, 3]).Build();

        var error = ReadFails(script.Bytes);

        Assert.Contains("WORD", error.Reason);
        Assert.Contains(ResourcesDir, error.Reason);
    }

    [Fact]
    public void Read_WordComSomaDeOffsetESizeQueEstouraUInt_Erro()
    {
        var script = NewBuilder()
            .AddCommand("WORD", 1, new SyntheticYstbArgument(0, 3, 0, 0xFFFFFFFF, 0))
            .AddResource([1, 2, 3])
            .Build();

        Assert.Contains(ResourcesDir, ReadFails(script.Bytes).Reason);
    }

    [Fact]
    public void Read_OperadorDeAtribuicaoInvalido_Erro()
    {
        var script = NewBuilder().AddCommand("_", 1, new SyntheticYstbArgument(0, 3, 9, 0, 0)).Build();

        var error = ReadFails(script.Bytes);

        Assert.Contains("operador de atribuição 9", error.Reason);
    }

    [Fact]
    public void Read_YscmSemOsComandosDeTexto_Erro()
    {
        var yscm = new SyntheticYscmBuilder().AddCommand("IF", "COND").AddCommand("ELSE").Build();
        var commands = YscmReader.Read(yscm.Bytes, "ysc.ybn");
        var script = new SyntheticYstbBuilder(yscm).AddCommand("ELSE", 1).Build();

        var error = Assert.Throws<InvalidFileException>(() => YstbReader.Read(script.Bytes, Source, commands));

        Assert.Contains("'WORD'", error.Reason);
    }

    [Fact]
    public void Read_ArquivoTruncadoEmQualquerPonto_NuncaDevolveDadosNemLancaOutraExcecao()
    {
        var bytes = TextScript(SyntheticYstbBuilder.KeyV290).Bytes;

        for (var length = 0; length < bytes.Length; length++)
        {
            var truncated = bytes[..length];
            var exception = Record.Exception(() => Read(truncated));
            Assert.IsType<InvalidFileException>(exception);
        }
    }

    [Fact]
    public void Read_ByteAlteradoEmQualquerPonto_OuLeOuFalhaComErroDoVnforge()
    {
        var original = TextScript(SyntheticYstbBuilder.KeyV290).Bytes;

        for (var i = 0; i < original.Length; i++)
        {
            var bytes = (byte[])original.Clone();
            bytes[i] ^= 0xFF;
            var exception = Record.Exception(() => Read(bytes));
            Assert.True(exception is null or VNForgeException, $"byte {i}: {exception}");
        }
    }

    private static void AssertMatchesScript(SyntheticYstb script, YstbFile file)
    {
        Assert.Equal(script.Version, file.Version);
        Assert.Equal(script.Padding, file.Padding);
        Assert.Equal(script.Key, file.Key);
        Assert.Equal(script.PlainArguments, file.ArgumentBytes.ToArray());
        Assert.Equal(script.PlainResources, file.Resources.ToArray());
        Assert.Equal(script.Instructions.Count, file.Instructions.Count);

        var firstArgument = 0;
        for (var i = 0; i < script.Instructions.Count; i++)
        {
            var expected = script.Instructions[i];
            var actual = file.Instructions[i];
            Assert.Equal(i, actual.Index);
            Assert.Equal(expected.Opcode, actual.Opcode);
            Assert.Equal(Commands.Commands[expected.Opcode].Name, actual.CommandName);
            Assert.Equal(expected.Extra, actual.Extra);
            Assert.Equal(expected.LineNumber, actual.LineNumber);
            Assert.Equal(firstArgument, actual.FirstArgumentIndex);
            Assert.Equal(expected.Arguments.Select(a => new YstbArgument(a.Id, a.Type, a.AssignOp, a.Size, a.Offset)), actual.Arguments);
            firstArgument += expected.Arguments.Count;
        }

        Assert.Equal(firstArgument, file.ArgumentCount);
    }
}
