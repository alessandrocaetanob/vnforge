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
}
