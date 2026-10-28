using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>#453: a prévia da caixa de texto mede a abertura da fala como ela aparece no jogo.</summary>
public sealed class LinePreviewSpeakerMarksTests : IDisposable
{
    private const string Id = "a001:000001";

    private static readonly FormattingProfile Wide = FormattingProfile.Default with { Wrap = new WrapRule(true, 40, 3, CharWidthMode.Uniform) };

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static TextUnit Spoken(string? speaker = "Misaki") =>
        new(Id, TextUnitKind.Dialogue, speaker, "原文", "um dois", TranslationStatus.Translated);

    private ProjectFolder Project(string strategy, SpeakerDelimiterStyle style, params TextUnit[] units)
    {
        var folder = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto-" + Guid.NewGuid().ToString("N")),
            new ProjectConfig("Teste", new GameConfig("D:/Jogos/Teste", "Jogo.exe"), new EngineConfig("yuris", 500, "0xD36FAC96"), new LanguagesConfig("ja", "pt-BR"), new EncodingConfig(strategy), AiConfig.Default)
            {
                Style = StyleConfig.Default with { SpeakerDelimiter = style },
            });
        folder.SaveText("a001", units);
        return folder;
    }

    [Fact]
    public void SemSinal_AAberturaInvisivelOcupaUmaColunaNaPrimeiraLinhaESemFechamento()
    {
        var preview = LinePreviewer.Create(Wide, Spoken(), "um dois", SpeakerBoxMarks.For(new Cp1252TextEncoder()));

        var line = Assert.Single(Assert.Single(preview.Boxes));
        Assert.Equal("\u00A0um dois", line.Text);
        Assert.Equal(8, line.Columns);
        Assert.Equal("um dois", preview.Text);
    }

    [Fact]
    public void ComGuillemets_AberturaENoFechamentoContamUmaColunaCada()
    {
        var preview = LinePreviewer.Create(Wide, Spoken(), "um dois", SpeakerBoxMarks.For(new Cp1252TextEncoder(SpeakerDelimiterStyle.Guillemets)));

        var line = Assert.Single(Assert.Single(preview.Boxes));
        Assert.Equal("«um dois»", line.Text);
        Assert.Equal(9, line.Columns);
    }

    [Fact]
    public void ASomaVaiSoNaPrimeiraELultimaLinhaDeUmaFalaComVariasLinhasECaixas()
    {
        var preview = LinePreviewer.Create(Wide, Spoken(), @"um\ndois\ptres", SpeakerBoxMarks.For(new Cp1252TextEncoder(SpeakerDelimiterStyle.Guillemets)));

        Assert.Equal([[3, 4], [5]], preview.Boxes.Select(box => box.Select(line => line.Columns).ToArray()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Narracao_NaoSomaNada(string? speaker)
    {
        var preview = LinePreviewer.Create(Wide, Spoken(speaker), "um dois", SpeakerBoxMarks.For(new Cp1252TextEncoder()));

        Assert.Equal(7, Assert.Single(Assert.Single(preview.Boxes)).Columns);
    }

    [Fact]
    public void ONomeEscolhidoTambemConta_ENarracaoEscolhidaNao()
    {
        var marks = SpeakerBoxMarks.For(new Cp1252TextEncoder());

        Assert.Equal(8, LinePreviewer.Create(Wide, Spoken(null) with { ChosenSpeaker = "Inui" }, "um dois", marks).Boxes[0][0].Columns);
        Assert.Equal(7, LinePreviewer.Create(Wide, Spoken() with { ChosenSpeaker = "" }, "um dois", marks).Boxes[0][0].Columns);
    }

    [Fact]
    public void ShiftJisEFold_NaoSomamNada()
    {
        Assert.Equal(SpeakerBoxMarks.None, SpeakerBoxMarks.For(new ShiftJisTextEncoder()));
        Assert.Equal(SpeakerBoxMarks.None, SpeakerBoxMarks.For(new FoldTextEncoder()));
        Assert.Equal(SpeakerBoxMarks.None, SpeakerBoxMarks.For((ITextEncoder?)null));
        Assert.Equal(7, LinePreviewer.Create(Wide, Spoken(), "um dois", SpeakerBoxMarks.For(new FoldTextEncoder())).Boxes[0][0].Columns);
    }

    [Fact]
    public void SemOParametroAPreviaMedeSoATraducaoComoAntes()
    {
        Assert.Equal(7, LinePreviewer.Create(Wide, Spoken(), "um dois").Boxes[0][0].Columns);
    }

    [Theory]
    [InlineData("cp1252", SpeakerDelimiterStyle.None, 8)]
    [InlineData("cp1252", SpeakerDelimiterStyle.Guillemets, 9)]
    [InlineData("fold", SpeakerDelimiterStyle.None, 7)]
    [InlineData("fold", SpeakerDelimiterStyle.Guillemets, 7)]
    [InlineData("sjis", SpeakerDelimiterStyle.None, 7)]
    public void PreviaDoProjetoUsaAEstrategiaEOEstiloDoVnforgeJson(string strategy, SpeakerDelimiterStyle style, int columns)
    {
        var project = Project(strategy, style, Spoken());

        Assert.Equal(columns, LinePreviewer.Create(project, Id).Boxes[0][0].Columns);
    }

    [Fact]
    public void EstrategiaSemEncoderNaoSomaNada()
    {
        Assert.Equal(SpeakerBoxMarks.None, SpeakerBoxMarks.For("remap", SpeakerDelimiterStyle.None));
        Assert.Equal(SpeakerBoxMarks.None, SpeakerBoxMarks.For("desconhecida", SpeakerDelimiterStyle.Guillemets));
    }
}
