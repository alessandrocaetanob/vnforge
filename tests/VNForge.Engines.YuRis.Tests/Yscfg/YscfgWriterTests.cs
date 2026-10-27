using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscfg.Synthetic;
using VNForge.Engines.YuRis.Yscfg;

namespace VNForge.Engines.YuRis.Tests.Yscfg;

public class YscfgWriterTests
{
    private const string Source = "yscfg.ybn";
    private const string Title = "テストの題名";

    [Fact]
    public void IdaEVolta_ComOMesmoTitulo_GeraArquivoIdentico()
    {
        var original = SyntheticYscfgBuilder.Build(Title);
        var file = YscfgReader.Read(original, Source);

        var rebuilt = YscfgWriter.WithTitle(file, GameEncodings.ShiftJis.GetBytes(Title));

        Assert.Equal(original, rebuilt);
    }

    [Fact]
    public void IdaEVolta_ComTituloVazio_GeraArquivoIdentico()
    {
        var original = SyntheticYscfgBuilder.Build(string.Empty);
        var file = YscfgReader.Read(original, Source);

        Assert.Equal(original, YscfgWriter.WithTitle(file, []));
    }

    [Fact]
    public void WithTitle_TrocaSoOCampoDoTitulo()
    {
        var original = SyntheticYscfgBuilder.Build(Title);
        var file = YscfgReader.Read(original, Source);
        var novo = GameEncodings.ShiftJis.GetBytes("Titulo novo, mais longo que o original");

        var rebuilt = YscfgWriter.WithTitle(file, novo);

        Assert.Equal(original.AsSpan(0, file.TitleOffset).ToArray(), rebuilt.AsSpan(0, file.TitleOffset).ToArray());
        Assert.Equal(file.TitleOffset + 2 + novo.Length, rebuilt.Length);
        var reread = YscfgReader.Read(rebuilt, Source);
        Assert.Equal("Titulo novo, mais longo que o original", reread.Title);
        Assert.Equal(file.Version, reread.Version);
        Assert.Equal(file.TitleOffset, reread.TitleOffset);
    }

    [Fact]
    public void WithTitle_TituloMaisCurto()
    {
        var file = YscfgReader.Read(SyntheticYscfgBuilder.Build(Title), Source);

        var rebuilt = YscfgWriter.WithTitle(file, "AB"u8.ToArray());

        Assert.Equal("AB", YscfgReader.Read(rebuilt, Source).Title);
    }

    [Fact]
    public void WithTitle_NaoAlteraOArquivoLido()
    {
        var original = SyntheticYscfgBuilder.Build(Title);
        var snapshot = original.ToArray();
        var file = YscfgReader.Read(original, Source);

        _ = YscfgWriter.WithTitle(file, "AB"u8.ToArray());

        Assert.Equal(snapshot, original);
    }

    [Fact]
    public void WithTitle_TituloMaiorQueDoisBytesDeTamanho_Recusa()
    {
        var file = YscfgReader.Read(SyntheticYscfgBuilder.Build(Title), Source);

        var ex = Assert.Throws<ArgumentException>(() => YscfgWriter.WithTitle(file, new byte[70_000]));

        Assert.Contains("65535", ex.Message, StringComparison.Ordinal);
    }
}
