using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Engines.YuRis.Tests.Ystb;

public sealed class YstbDumperTests
{
    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private static SyntheticYstbBuilder NewBuilder() => new(Yscm);

    private static YstbDump Dump(SyntheticYstbBuilder builder)
    {
        var commands = YscmReader.Read(Yscm.Bytes, "ysc.ybn");
        var script = YstbReader.Read(builder.Build().Bytes, "yst00001.ybn", commands);
        return YstbDumper.Dump(script, commands);
    }

    [Fact]
    public void Dump_traz_cabecalho_chave_e_comandos_pelo_nome()
    {
        var builder = NewBuilder();
        builder.AddWord("こんにちは", 5);
        builder.AddReturnCode(0, 6);

        var dump = Dump(builder);

        Assert.Equal("yst00001.ybn", dump.Source);
        Assert.Equal(500u, dump.Version);
        Assert.Equal(SyntheticYstbBuilder.KeyV290, dump.Key);
        Assert.Equal(2, dump.ArgumentCount);
        Assert.Collection(
            dump.Instructions,
            i =>
            {
                Assert.Equal(0, i.Index);
                Assert.Equal("WORD", i.Command);
                Assert.Equal(Yscm.IdOf("WORD"), i.Opcode);
                Assert.Equal(5u, i.LineNumber);
            },
            i =>
            {
                Assert.Equal(1, i.Index);
                Assert.Equal("RETURNCODE", i.Command);
                Assert.Equal(6u, i.LineNumber);
            });
    }

    [Fact]
    public void Dump_do_WORD_mostra_o_texto_com_marcadores_de_controle()
    {
        var builder = NewBuilder();
        builder.AddWordBytes([.. GameEncodings.ShiftJis.GetBytes("美咲「やあ"), 0xEF, 0xF0, .. GameEncodings.ShiftJis.GetBytes("またね」"), 0xEF, 0xF2, 0xEF, 0x99], 1);

        var argument = Assert.Single(Assert.Single(Dump(builder).Instructions).Arguments);

        Assert.Equal(YstbArgumentKind.Text, argument.Kind);
        Assert.Equal("TEXT", argument.Name);
        Assert.Equal("美咲「やあ\\nまたね」\\p{EF:99}", argument.Value);
    }

    [Fact]
    public void Dump_de_string_de_expressao_tira_as_aspas_e_desfaz_os_escapes()
    {
        var builder = NewBuilder();
        builder.AddCommand("GOSUB", 1, builder.StringExpression("ES.SEL.SET"), builder.StringExpression("はい\\nいいえ", '`', id: 2));

        var instruction = Assert.Single(Dump(builder).Instructions);

        Assert.All(instruction.Arguments, a => Assert.Equal(YstbArgumentKind.String, a.Kind));
        Assert.Equal("ES.SEL.SET", instruction.Arguments[0].Value);
        Assert.Equal("はい\\nいいえ", instruction.Arguments[1].Value);
        Assert.Equal("PSTR2", instruction.Arguments[1].Name);
    }

    [Fact]
    public void Dump_de_expressao_que_nao_e_string_mostra_os_bytes_em_hexadecimal()
    {
        var builder = NewBuilder();
        builder.AddCommand("WAIT", 1, builder.ResourceArgument([0x4D, 0x01, 0x00, 0x22], assignOp: 1));

        var argument = Assert.Single(Assert.Single(Dump(builder).Instructions).Arguments);

        Assert.Equal(YstbArgumentKind.Expression, argument.Kind);
        Assert.Equal("4D010022", argument.Value);
        Assert.Equal("+=", argument.AssignOperator);
        Assert.Equal(1, argument.AssignOp);
        Assert.Equal(4u, argument.Size);
    }

    [Fact]
    public void Dump_nao_trata_size_e_offset_de_RETURNCODE_IF_e_LOOP_como_recursos()
    {
        var builder = NewBuilder();
        builder.AddReturnCode(7, 1, offset: 999);
        builder.AddCommand("IF", 2, builder.StringExpression("OK"), new SyntheticYstbArgument(1, 1, 0, 99, 12345), new SyntheticYstbArgument(2, 1, 0, 98, 54321));
        builder.AddCommand("LOOP", 3, builder.StringExpression("OK"), new SyntheticYstbArgument(1, 1, 0, 5, 77));

        var dump = Dump(builder);

        var returnCode = Assert.Single(dump.Instructions[0].Arguments);
        Assert.Equal(YstbArgumentKind.Value, returnCode.Kind);
        Assert.Equal(7u, returnCode.Size);
        Assert.Equal(999u, returnCode.Offset);
        Assert.Null(returnCode.Value);
        Assert.Equal(
            [YstbArgumentKind.String, YstbArgumentKind.Value, YstbArgumentKind.Value],
            dump.Instructions[1].Arguments.Select(a => a.Kind));
        Assert.Equal(98u, dump.Instructions[1].Arguments[2].Size);
        Assert.Equal(
            [YstbArgumentKind.String, YstbArgumentKind.Value],
            dump.Instructions[2].Arguments.Select(a => a.Kind));
    }

    [Fact]
    public void Dump_de_argumento_sem_bytes_e_vazio()
    {
        var builder = NewBuilder();
        builder.AddCommand("WAIT", 1, new SyntheticYstbArgument(0, 1, 0, 0, 0));

        var argument = Assert.Single(Assert.Single(Dump(builder).Instructions).Arguments);

        Assert.Equal(YstbArgumentKind.Empty, argument.Kind);
        Assert.Null(argument.Value);
    }

    [Fact]
    public void Dump_deixa_o_nome_nulo_quando_o_id_passa_da_lista_de_parametros()
    {
        var builder = NewBuilder();
        builder.AddCommand("WAIT", 1, builder.StringExpression("OK", id: 9));

        var argument = Assert.Single(Assert.Single(Dump(builder).Instructions).Arguments);

        Assert.Null(argument.Name);
        Assert.Equal(9, argument.Id);
    }
}
