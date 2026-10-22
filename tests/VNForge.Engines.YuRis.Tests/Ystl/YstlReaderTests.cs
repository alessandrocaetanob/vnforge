using System.Buffers.Binary;
using VNForge.Core;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;
using VNForge.Engines.YuRis.Ystl;

namespace VNForge.Engines.YuRis.Tests.Ystl;

public class YstlReaderTests
{
    private const string Source = "yst_list.ybn";

    // Montado à mão, fora do gerador: v500, um script "a.yst", FILETIME 1, 2 variáveis, 3 rótulos, 4 textos.
    private const string GoldenHex =
        "5953544C" + "F4010000" + "01000000"
        + "00000000" + "05000000" + "612E797374" + "0100000000000000" + "02000000" + "03000000" + "04000000";

    private static YstlFile Read(byte[] bytes) => YstlReader.Read(bytes, Source);

    private static InvalidFileException ReadFails(byte[] bytes) => Assert.Throws<InvalidFileException>(() => Read(bytes));

    private static SyntheticYstlBuilder TwoScripts() => new SyntheticYstlBuilder()
        .AddScript(new SyntheticYstlScript(@"userscript\01_prologo.yst", VariableCount: 5, LabelCount: 7, TextCount: 120))
        .AddScript(new SyntheticYstlScript(@"userscript\02_capitulo.yst", VariableCount: 1, LabelCount: 0, TextCount: 4));

    [Fact]
    public void Read_VetorDeOuro()
    {
        var file = Read(Convert.FromHexString(GoldenHex));

        Assert.Equal(500u, file.Version);
        Assert.True(file.HasTextCount);
        var script = Assert.Single(file.Scripts);
        Assert.Equal(new YstlScript(0, "a.yst", 1, 2, 3, 4), script);
    }

    [Fact]
    public void Read_ScriptsNaOrdemDoArquivo_ComTodosOsCampos()
    {
        var ystl = TwoScripts().Build();

        var file = Read(ystl.Bytes);

        Assert.Equal(2, file.Scripts.Count);
        for (var i = 0; i < ystl.Scripts.Count; i++)
        {
            var expected = ystl.Scripts[i];
            var script = file.Scripts[i];
            Assert.Equal((uint)i, script.Index);
            Assert.Equal(expected.Path, script.Path);
            Assert.Equal(expected.ModifiedFileTime, script.ModifiedFileTime);
            Assert.Equal(expected.VariableCount, script.VariableCount);
            Assert.Equal(expected.LabelCount, script.LabelCount);
            Assert.Equal(expected.TextCount, script.TextCount);
        }
    }

    [Fact]
    public void Read_DecodificaCaminhoEmShiftJis()
    {
        var ystl = new SyntheticYstlBuilder().AddScript(@"シナリオ\第一章.yst").Build();

        var script = Assert.Single(Read(ystl.Bytes).Scripts);

        Assert.Equal(@"シナリオ\第一章.yst", script.Path);
        Assert.Equal("第一章", script.ReadableName);
    }

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

    [Fact]
    public void ModifiedUtc_ConverteOFileTime()
    {
        var ystl = new SyntheticYstlBuilder().AddScript("a.yst").Build();

        var script = Assert.Single(Read(ystl.Bytes).Scripts);

        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), script.ModifiedUtc);
    }

    [Fact]
    public void ModifiedUtc_ForaDoIntervalo_ENulo()
    {
        var script = new YstlScript(0, "a.yst", ulong.MaxValue, 0, 0, null);

        Assert.Null(script.ModifiedUtc);
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
        Assert.Equal("yst00000.ybn", new YstlScript(0, "a.yst", 0, 0, 0, null).BinaryFileName);
        Assert.Equal("yst00123.ybn", new YstlScript(123, "a.yst", 0, 0, 0, null).BinaryFileName);
        Assert.Equal("yst123456.ybn", new YstlScript(123456, "a.yst", 0, 0, 0, null).BinaryFileName);
    }

    [Fact]
    public void TryGetByBinaryName_AchaOScriptPeloNomeDoYstb()
    {
        var file = Read(TwoScripts().Build().Bytes);

        Assert.True(file.TryGetByBinaryName("yst00001.ybn", out var script));
        Assert.Equal(@"userscript\02_capitulo.yst", script.Path);
        Assert.True(file.TryGetByBinaryName("YST00000.YBN", out script));
        Assert.Equal("01_prologo", script.ReadableName);
        Assert.True(file.TryGetByBinaryName("yst1.ybn", out script));
        Assert.Equal(1u, script.Index);
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

    [Fact]
    public void TryGetByIndex_AchaPeloIndice()
    {
        var file = Read(TwoScripts().Build().Bytes);

        Assert.True(file.TryGetByIndex(1, out var script));
        Assert.Equal("02_capitulo", script.ReadableName);
        Assert.False(file.TryGetByIndex(2, out _));
    }

    [Fact]
    public void Read_ListaVazia()
    {
        var file = Read(new SyntheticYstlBuilder().Build().Bytes);

        Assert.Empty(file.Scripts);
    }

    [Fact]
    public void Read_ViaFluxo_DaOMesmoResultadoQueViaBytes()
    {
        var ystl = TwoScripts().Build();
        using var stream = new MemoryStream(ystl.Bytes);

        var file = YstlReader.Read(stream);

        Assert.Equal("<fluxo>", file.Source);
        Assert.Equal(ystl.Scripts.Select(s => s.Path), file.Scripts.Select(s => s.Path));
    }

    [Fact]
    public void ReadFile_LeDoDisco()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vnforge-ystl-{Guid.NewGuid():N}.ybn");
        try
        {
            File.WriteAllBytes(path, TwoScripts().Build().Bytes);

            var file = YstlReader.ReadFile(path);

            Assert.Equal(path, file.Source);
            Assert.Equal(2, file.Scripts.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- Campo txt_count: as fontes divergem no limiar (474 no ystl.py, 477 no yuri.md) ----

    [Theory]
    [InlineData(200u, false)]
    [InlineData(473u, false)]
    [InlineData(477u, true)]
    [InlineData(500u, true)]
    [InlineData(599u, true)]
    public void Read_TextCountSegueAVersaoForaDaFaixaAmbigua(uint version, bool hasTextCount)
    {
        var ystl = TwoScripts().WithVersion(version).Build();
        Assert.Equal(hasTextCount, ystl.WritesTextCount);

        var file = Read(ystl.Bytes);

        Assert.Equal(hasTextCount, file.HasTextCount);
        Assert.Equal(hasTextCount ? 120u : null, file.Scripts[0].TextCount);
        Assert.Equal(hasTextCount ? 4u : null, file.Scripts[1].TextCount);
        Assert.Equal(ystl.Scripts[0].LabelCount, file.Scripts[0].LabelCount);
    }

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
            Assert.Equal(@"userscript\02_capitulo.yst", file.Scripts[1].Path);
        }
    }

    [Theory]
    [InlineData(300u, true)]
    [InlineData(500u, false)]
    public void Read_ConfereOLayoutPeloConteudo_QuandoAVersaoNaoConcorda(uint version, bool writesTextCount)
    {
        var ystl = TwoScripts().WithVersion(version).WithTextCount(writesTextCount).Build();

        var file = Read(ystl.Bytes);

        Assert.Equal(writesTextCount, file.HasTextCount);
        Assert.Equal(2, file.Scripts.Count);
        Assert.Equal(@"userscript\02_capitulo.yst", file.Scripts[1].Path);
    }

    // ---- Arquivo inválido ----

    [Fact]
    public void Read_ArquivoCurto_Erro()
    {
        var error = ReadFails([0x59, 0x53, 0x54, 0x4C]);

        Assert.Contains("precisa de pelo menos 8", error.Reason);
    }

    [Fact]
    public void Read_AssinaturaErrada_Erro()
    {
        var bytes = TwoScripts().Build().Bytes;
        bytes[3] = (byte)'X';

        var error = ReadFails(bytes);

        Assert.Contains("YSTL", error.Reason);
        Assert.Contains(Source, error.Message);
    }

    [Theory]
    [InlineData(100u)]
    [InlineData(600u)]
    public void Read_VersaoForaDaFaixa_LancaVersaoDesconhecida(uint version)
    {
        var bytes = TwoScripts().WithVersion(version).Build().Bytes;

        var error = Assert.Throws<UnknownVersionException>(() => Read(bytes));

        Assert.Equal(Source, error.Path);
    }

    [Fact]
    public void Read_SemContagemDeScripts_Erro()
    {
        var error = ReadFails("YSTL"u8.ToArray().Concat(BitConverter.GetBytes(500u)).ToArray());

        Assert.Contains("a quantidade de scripts", error.Reason);
    }

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
    public void Read_IndiceForaDeSequencia_ErroNomeiaAPosicao()
    {
        var bytes = TwoScripts().WithIndexAt(1, 7).Build().Bytes;

        var error = ReadFails(bytes);

        Assert.Contains("posição 1", error.Reason);
        Assert.Contains("índice 7", error.Reason);
        Assert.Contains("tentei os layouts com e sem o campo de contagem de textos", error.Reason);
    }

    [Fact]
    public void Read_TruncadoNoMeioDeUmScript_ErroNomeiaOScript()
    {
        var bytes = TwoScripts().Build().Bytes;

        var error = ReadFails(bytes[..^6]);

        Assert.Contains("script 1", error.Reason);
        Assert.Contains("02_capitulo", error.Reason);
    }

    [Fact]
    public void Read_CaminhoMaiorQueOArquivo_Erro()
    {
        var bytes = new SyntheticYstlBuilder().AddScript("a.yst").Build().Bytes;
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
}
