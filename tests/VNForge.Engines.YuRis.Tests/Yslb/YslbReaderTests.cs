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
    public void Read_VetorDeOuro()
    {
        var file = Read(Convert.FromHexString(GoldenHex));

        Assert.Equal(485u, file.Version);
        Assert.Equal(new YslbLabel("A", 7, 2, 1, 3), Assert.Single(file.Labels));
    }

    [Fact]
    public void Read_RotulosNaOrdemDoArquivo_ComNomeEmShiftJis()
    {
        var bytes = new SyntheticYslbBuilder()
            .AddLabel("SYSTEM_START", 62)
            .AddLabel("日本語", 113, 5)
            .Build();

        var file = Read(bytes);

        Assert.Equal(["SYSTEM_START", "日本語"], file.Labels.Select(l => l.Name));
        Assert.Equal((ushort)113, file.Labels[1].ScriptId);
        Assert.Equal(5u, file.Labels[1].CodePosition);
    }

    [Fact]
    public void Find_ProcuraPeloNomeExatoAntesDeIgnorarACaixa()
    {
        var file = Read(new SyntheticYslbBuilder().AddLabel("ES.SAVE.END", 51).AddLabel("es.Save.end", 19).Build());

        Assert.Equal((ushort)19, file.Find("es.Save.end")!.ScriptId);
        Assert.Equal((ushort)51, file.Find("ES.SAVE.END")!.ScriptId);
    }

    [Fact]
    public void Find_IgnoraACaixaQuandoSoHaUmRotuloComEsseNome()
    {
        var file = Read(new SyntheticYslbBuilder().AddLabel("ES.SEL.SET", 23).Build());

        Assert.Equal((ushort)23, file.Find("es.sel.set")!.ScriptId);
    }

    [Fact]
    public void Find_NomeEmDoisScriptsSemCaixaExata_NaoEscolheNenhum()
    {
        var file = Read(new SyntheticYslbBuilder().AddLabel("ES.SAVE.END", 51).AddLabel("es.Save.end", 19).Build());

        Assert.Null(file.Find("Es.Save.End"));
    }

    [Fact]
    public void Find_RotuloInexistente_DevolveNulo()
    {
        var file = Read(new SyntheticYslbBuilder().AddLabel("A", 1).Build());

        Assert.Null(file.Find("B"));
    }

    [Fact]
    public void Read_SemRotulos_DevolveListaVazia()
    {
        Assert.Empty(Read(new SyntheticYslbBuilder().Build()).Labels);
    }

    [Fact]
    public void Read_AssinaturaErrada_Recusa()
    {
        var bytes = new SyntheticYslbBuilder().Build();
        bytes[0] = (byte)'X';

        var ex = Assert.Throws<InvalidFileException>(() => Read(bytes));

        Assert.Contains("YSLB", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ContagemMaiorQueOArquivo_RecusaSemAlocar()
    {
        var bytes = new SyntheticYslbBuilder().AddLabel("A", 1).Build();
        BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes, 8);

        Assert.Throws<InvalidFileException>(() => Read(bytes));
    }

    [Fact]
    public void Read_ArquivoCortadoNoMeioDeUmRotulo_Recusa()
    {
        var bytes = new SyntheticYslbBuilder().AddLabel("ABC", 1).Build();

        Assert.Throws<InvalidFileException>(() => Read(bytes[..^3]));
    }

    [Fact]
    public void Read_SobraDepoisDoUltimoRotulo_Recusa()
    {
        var bytes = new SyntheticYslbBuilder().AddLabel("A", 1).Build();

        var ex = Assert.Throws<InvalidFileException>(() => Read([.. bytes, 0, 0]));

        Assert.Contains("sobram", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_VersaoDesconhecida_Recusa()
    {
        Assert.Throws<UnknownVersionException>(() => Read(new SyntheticYslbBuilder().WithVersion(9999).Build()));
    }
}
