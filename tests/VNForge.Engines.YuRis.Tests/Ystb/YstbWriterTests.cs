using System.Buffers.Binary;
using VNForge.Core;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Engines.YuRis.Tests.Ystb;

public class YstbWriterTests
{
    private const string Source = "ybin/YST00001.ybn";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private static readonly YscmFile Commands = YscmReader.Read(Yscm.Bytes, "ysc.ybn");

    private static readonly IReadOnlyDictionary<int, ReadOnlyMemory<byte>> NoChanges = new Dictionary<int, ReadOnlyMemory<byte>>();

    private static SyntheticYstbBuilder NewBuilder() => new(Yscm);

    private static YstbFile Read(byte[] bytes) => YstbReader.Read(bytes, Source, Commands);

    private static SyntheticYstbBuilder SampleScript(SyntheticYstbBuilder builder)
    {
        builder.AddWord("美咲「こんにちは」", 1);
        builder.AddReturnCode(5, 2, offset: 99999);
        builder.AddInstruction(Yscm.IdOf("GOSUB"), 0x1234, 3, builder.StringExpression("ES.SEL.SET"), builder.StringExpression("はい"));
        builder.AddWord("さようなら", 4);
        builder.AddCommand("IF", 5, builder.StringExpression("日本語"), new SyntheticYstbArgument(0, 1, 0, 99, 12345), new SyntheticYstbArgument(0, 1, 0, 98, 54321));
        builder.AddInstruction(Yscm.IdOf("RETURN"), 0xBEEF, 6);
        return builder;
    }

    private static byte[] ArgumentSlice(YstbFile file, int index) => file.ArgumentBytes.Span.Slice(index * 12, 12).ToArray();

    public static TheoryData<uint, uint> Keys => new()
    {
        { SyntheticYstbBuilder.KeyV290, 0 },
        { SyntheticYstbBuilder.KeyV200, 0 },
        { 0x12345678, 0 },
        { 0, 0 },
        { SyntheticYstbBuilder.KeyV290, 7 },
    };

    [Theory]
    [MemberData(nameof(Keys))]
    public void Write_SemTroca_GeraArquivoIdentico(uint key, uint padding)
    {
        var built = SampleScript(NewBuilder().WithKey(key).WithPadding(padding)).Build();

        var written = YstbWriter.Write(Read(built.Bytes), NoChanges);

        Assert.Equal(built.Bytes, written);
    }

    [Fact]
    public void Write_ScriptSemArgumentos_GeraArquivoIdentico()
    {
        var built = NewBuilder().AddInstruction(Yscm.IdOf("RETURN"), 0x0102, 9).Build();

        Assert.Equal(built.Bytes, YstbWriter.Write(Read(built.Bytes), NoChanges));
    }

    [Fact]
    public void Write_ComTroca_SoOArgumentoTrocadoGanhaSizeEOffsetNovos()
    {
        var built = SampleScript(NewBuilder()).Build();
        var original = Read(built.Bytes);
        var newText = "new text"u8.ToArray();

        var written = YstbWriter.Write(original, new Dictionary<int, ReadOnlyMemory<byte>> { [0] = newText });
        var reread = Read(written);

        Assert.Equal(original.ArgumentCount, reread.ArgumentCount);
        for (var i = 1; i < original.ArgumentCount; i++)
        {
            Assert.Equal(ArgumentSlice(original, i), ArgumentSlice(reread, i));
        }

        var first = reread.Instructions[0].Arguments[0];
        var before = original.Instructions[0].Arguments[0];
        Assert.Equal((before.Id, before.Type, before.AssignOp), (first.Id, first.Type, first.AssignOp));
        Assert.Equal((uint)newText.Length, first.Size);
        Assert.Equal((uint)original.Resources.Length, first.Offset);
        Assert.Equal(newText, reread.GetResource(first).ToArray());
        Assert.Equal([.. original.Resources.ToArray(), .. newText], reread.Resources.ToArray());
    }

    [Fact]
    public void Write_ComTroca_PreservaInstrucoesCabecalhoENumerosDeLinha()
    {
        var built = SampleScript(NewBuilder().WithPadding(3)).Build();
        var original = Read(built.Bytes);

        var reread = Read(YstbWriter.Write(original, new Dictionary<int, ReadOnlyMemory<byte>> { [3] = "x"u8.ToArray() }));

        Assert.Equal((original.Version, original.Padding, original.Key), (reread.Version, reread.Padding, reread.Key));
        Assert.Equal(
            original.Instructions.Select(i => (i.Opcode, i.Extra, i.LineNumber, i.Arguments.Count)),
            reread.Instructions.Select(i => (i.Opcode, i.Extra, i.LineNumber, i.Arguments.Count)));
    }

    [Fact]
    public void Write_ArgumentosDoReturnCodeEDoIfFicamIntactos()
    {
        var original = Read(SampleScript(NewBuilder()).Build().Bytes);
        var replacements = new Dictionary<int, ReadOnlyMemory<byte>> { [0] = "a"u8.ToArray(), [4] = "b"u8.ToArray() };

        var reread = Read(YstbWriter.Write(original, replacements));

        var returnCode = original.Instructions[1].FirstArgumentIndex;
        Assert.Equal(ArgumentSlice(original, returnCode), ArgumentSlice(reread, returnCode));
        var ifStart = original.Instructions[4].FirstArgumentIndex;
        Assert.Equal(ArgumentSlice(original, ifStart + 1), ArgumentSlice(reread, ifStart + 1));
        Assert.Equal(ArgumentSlice(original, ifStart + 2), ArgumentSlice(reread, ifStart + 2));
    }

    [Fact]
    public void Write_DuasTrocas_AnexamNaOrdemDoIndiceIndependenteDoDicionario()
    {
        var original = Read(SampleScript(NewBuilder()).Build().Bytes);
        var length = original.Resources.Length;
        var replacements = new Dictionary<int, ReadOnlyMemory<byte>> { [4] = "bbbb"u8.ToArray(), [0] = "aa"u8.ToArray() };

        var reread = Read(YstbWriter.Write(original, replacements));

        Assert.Equal(((uint)length, 2u), (reread.Instructions[0].Arguments[0].Offset, reread.Instructions[0].Arguments[0].Size));
        var second = reread.Instructions[original.Instructions.ToList().FindIndex(i => i.FirstArgumentIndex == 4)].Arguments[0];
        Assert.Equal(((uint)length + 2, 4u), (second.Offset, second.Size));
    }

    [Fact]
    public void Write_BytesVazios_DaoSizeZeroEOffsetNoFimDosRecursos()
    {
        var original = Read(SampleScript(NewBuilder()).Build().Bytes);

        var reread = Read(YstbWriter.Write(original, new Dictionary<int, ReadOnlyMemory<byte>> { [0] = ReadOnlyMemory<byte>.Empty }));

        var argument = reread.Instructions[0].Arguments[0];
        Assert.Equal((0u, (uint)original.Resources.Length), (argument.Size, argument.Offset));
        Assert.Empty(reread.GetResource(argument).ToArray());
        Assert.Equal(original.Resources.ToArray(), reread.Resources.ToArray());
    }

    [Fact]
    public void Write_CifraDeNovoComAMesmaChave()
    {
        var original = Read(SampleScript(NewBuilder().WithKey(0x12345678)).Build().Bytes);
        var newText = "texto"u8.ToArray();

        var written = YstbWriter.Write(original, new Dictionary<int, ReadOnlyMemory<byte>> { [3] = newText });

        var resourcesStart = 32 + (original.Instructions.Count * 4) + original.ArgumentBytes.Length;
        var plain = SyntheticYstbBuilder.Encrypt(written[resourcesStart..(resourcesStart + original.Resources.Length + newText.Length)], 0x12345678);
        Assert.Equal([.. original.Resources.ToArray(), .. newText], plain);
        Assert.Equal(0x12345678u, Read(written).Key);
        Assert.Equal((uint)(original.Resources.Length + newText.Length), BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(20)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void Write_IndiceDeArgumentoInexistente_Falha(int index)
    {
        var original = Read(SampleScript(NewBuilder()).Build().Bytes);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => YstbWriter.Write(original, new Dictionary<int, ReadOnlyMemory<byte>> { [index] = "x"u8.ToArray() }));
    }

    [Fact]
    public void Write_PrimeiroArgumentoComChaveDerivada_FalhaPorqueOLeitorNaoAbririaOArquivo()
    {
        var original = Read(SampleScript(NewBuilder().WithKey(0x12345678)).Build().Bytes);
        Assert.Equal(YstbKeyOrigin.Derived, original.KeyOrigin);

        var error = Assert.Throws<VNForgeException>(
            () => YstbWriter.Write(original, new Dictionary<int, ReadOnlyMemory<byte>> { [0] = "x"u8.ToArray() }));

        Assert.Contains("0x12345678", error.Message);
        Assert.Contains("primeiro argumento", error.Message);
    }

    [Fact]
    public void Write_PrimeiroArgumentoComChaveConhecida_Funciona()
    {
        var original = Read(SampleScript(NewBuilder()).Build().Bytes);

        var reread = Read(YstbWriter.Write(original, new Dictionary<int, ReadOnlyMemory<byte>> { [0] = "x"u8.ToArray() }));

        Assert.Equal(original.Key, reread.Key);
    }

    [Fact]
    public void Write_ResultadoPassandoDoLimiteDoLeitor_Falha()
    {
        var original = Read(SampleScript(NewBuilder()).Build().Bytes);
        var huge = new byte[32 * 1024 * 1024];

        var error = Assert.Throws<VNForgeException>(
            () => YstbWriter.Write(original, new Dictionary<int, ReadOnlyMemory<byte>> { [3] = huge }));

        Assert.Contains("MiB", error.Message);
    }
}
