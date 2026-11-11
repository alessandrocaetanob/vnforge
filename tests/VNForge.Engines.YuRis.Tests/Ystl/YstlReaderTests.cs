using System.Buffers.Binary;
using VNForge.Core;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;
using VNForge.Engines.YuRis.Ystl;

namespace VNForge.Engines.YuRis.Tests.Ystl;

public class YstlReaderTests
{
    private const string ChapterTwoPath = @"userscript\02_capitulo.yst";
    private const string AYst = "a.yst";

    private const string Source = "yst_list.ybn";

    // Montado à mão, fora do gerador: v500, um script AYst, FILETIME 1, 2 variáveis, 3 rótulos, 4 textos.
    private const string GoldenHex =
        "5953544C" + "F4010000" + "01000000"
        + "00000000" + "05000000" + "612E797374" + "0100000000000000" + "02000000" + "03000000" + "04000000";

    private static YstlFile Read(byte[] bytes) => YstlReader.Read(bytes, Source);

    private static InvalidFileException ReadFails(byte[] bytes) => Assert.Throws<InvalidFileException>(() => Read(bytes));

    private static SyntheticYstlBuilder TwoScripts() => new SyntheticYstlBuilder()
        .AddScript(new SyntheticYstlScript(@"userscript\01_prologo.yst", VariableCount: 5, LabelCount: 7, TextCount: 120))
        .AddScript(new SyntheticYstlScript(ChapterTwoPath, VariableCount: 1, LabelCount: 0, TextCount: 4));

    [Theory]
    [InlineData(@"userscript\01_prologo.yst", "01_prologo")]
    [InlineData("userscript/01_prologo.yst", "01_prologo")]
    [InlineData(@"a\b\c.d.yst", "c.d")]
    [InlineData("global.yst", "global")]
    [InlineData("semextensao", "semextensao")]
    [InlineData(@"pasta\.yst", ".yst")]
    public void ReadableName_TiraPastaEExtensao(string path, string expected)
    {
        var script = new YstlScript(0, path, 0, 0, 0, null);

        Assert.Equal(expected, script.ReadableName);
    }

    [Theory]
    [InlineData(@"userscript\01_cap1\manha.yst", "userscript/01_cap1")]
    [InlineData("userscript/01_prologo.yst", "userscript")]
    [InlineData("global.yst", "")]
    [InlineData(@"\global.yst", "")]
    public void Folder_ChegaComBarraNormal(string path, string expected)
    {
        var script = new YstlScript(0, path, 0, 0, 0, null);

        Assert.Equal(expected, script.Folder);
    }

    [Fact]
    public void ModifiedUtc_ConverteOFileTime()
    {
        var ystl = new SyntheticYstlBuilder().AddScript(AYst).Build();

        var script = Assert.Single(Read(ystl.Bytes).Scripts);

        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), script.ModifiedUtc);
    }

    [Fact]
    public void Read_VariaveisNegativas_ScriptSemYstb()
    {
        var ystl = new SyntheticYstlBuilder()
            .AddScript(new SyntheticYstlScript("global.yst", VariableCount: -1))
            .AddScript(new SyntheticYstlScript("vazio.yst", VariableCount: 0))
            .Build();

        var file = Read(ystl.Bytes);

        Assert.Equal(-1, file.Scripts[0].VariableCount);
        Assert.False(file.Scripts[0].HasCompiledFile);
        Assert.True(file.Scripts[1].HasCompiledFile);
    }

    [Fact]
    public void BinaryFileName_TemCincoDigitos()
    {
        Assert.Equal("yst00000.ybn", new YstlScript(0, AYst, 0, 0, 0, null).BinaryFileName);
        Assert.Equal("yst00123.ybn", new YstlScript(123, AYst, 0, 0, 0, null).BinaryFileName);
        Assert.Equal("yst123456.ybn", new YstlScript(123456, AYst, 0, 0, 0, null).BinaryFileName);
    }

    [Theory]
    [InlineData("yst00002.ybn")]
    [InlineData("yst_list.ybn")]
    [InlineData("ysc.ybn")]
    [InlineData("yst.ybn")]
    [InlineData("yst-1.ybn")]
    [InlineData("yst0000a.ybn")]
    [InlineData("yst00000.bin")]
    [InlineData("yst99999999999.ybn")]
    public void TryGetByBinaryName_NomeQueNaoCorresponde_DaFalso(string name)
    {
        var file = Read(TwoScripts().Build().Bytes);

        Assert.False(file.TryGetByBinaryName(name, out _));
    }

    // ---- Campo txt_count: as fontes divergem no limiar (474 no ystl.py, 477 no yuri.md) ----

    [Theory]
    [InlineData(474u)]
    [InlineData(475u)]
    [InlineData(476u)]
    public void Read_FaixaAmbigua474a476_LeOsDoisLayouts(uint version)
    {
        foreach (var writesTextCount in new[] { true, false })
        {
            var ystl = TwoScripts().WithVersion(version).WithTextCount(writesTextCount).Build();

            var file = Read(ystl.Bytes);

            Assert.Equal(writesTextCount, file.HasTextCount);
            Assert.Equal(writesTextCount ? 120u : null, file.Scripts[0].TextCount);
            Assert.Equal(7u, file.Scripts[0].LabelCount);
            Assert.Equal(ChapterTwoPath, file.Scripts[1].Path);
        }
    }

    // ---- Arquivo inválido ----

    [Fact]
    public void Read_ContagemImpossivelParaOTamanhoDoArquivo_Erro()
    {
        var bytes = TwoScripts().Build().Bytes;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), uint.MaxValue);

        var error = ReadFails(bytes);

        Assert.Contains(uint.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Reason);
        Assert.Contains("scripts", error.Reason);
    }

    [Fact]
    public void Read_CaminhoMaiorQueOArquivo_Erro()
    {
        var bytes = new SyntheticYstlBuilder().AddScript(AYst).Build().Bytes;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), uint.MaxValue);

        var error = ReadFails(bytes);

        Assert.Contains("caminho do script 0", error.Reason);
    }

    [Fact]
    public void Read_BytesSobrandoNoFim_Erro()
    {
        var bytes = TwoScripts().Build().Bytes.Concat(new byte[] { 1, 2, 3 }).ToArray();

        var error = ReadFails(bytes);

        Assert.Contains("sobram", error.Reason);
    }

    [Fact]
    public void Read_VetorDeOuro()
    {
        var file = Read(Convert.FromHexString(GoldenHex));

        Assert.Equal(500u, file.Version);
        Assert.True(file.HasTextCount);
        var script = Assert.Single(file.Scripts);
        Assert.Equal(new YstlScript(0, AYst, 1, 2, 3, 4), script);
    }
}
