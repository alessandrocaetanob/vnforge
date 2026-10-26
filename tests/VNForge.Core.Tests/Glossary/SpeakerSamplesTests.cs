using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Glossary;

public class SpeakerSamplesTests
{
    private static TextUnit Line(int n, string source, string? speaker) =>
        new($"s:{n}", TextUnitKind.Dialogue, speaker, source, null, TranslationStatus.Untranslated);

    [Fact]
    public void Agrupa_as_falas_pelo_falante_e_ignora_a_narracao()
    {
        var samples = SpeakerSamples.From(
            [Line(0, "あ", "美咲"), Line(1, "い", "健太"), Line(2, "う", "美咲"), Line(3, "narração", null)],
            minLines: 1);

        Assert.Equal(["あ", "う"], samples["美咲"]);
        Assert.Equal(["い"], samples["健太"]);
        Assert.Equal(2, samples.Count);
    }

    [Fact]
    public void Falante_so_de_sinais_nao_vira_personagem_por_mais_que_fale()
    {
        var units = Enumerable.Range(0, 10).Select(n => Line(n, $"fala {n}", "？？？"))
            .Concat(Enumerable.Range(10, 5).Select(n => Line(n, $"fala {n}", "美咲")));

        var samples = SpeakerSamples.From(units);

        Assert.DoesNotContain("？？？", samples.Keys);
        Assert.Contains("美咲", samples.Keys);
    }

    [Fact]
    public void Falante_com_menos_de_tres_falas_nao_e_sugerido()
    {
        var samples = SpeakerSamples.From([
            Line(0, "a", "美咲"), Line(1, "b", "美咲"), Line(2, "c", "美咲"),
            Line(3, "d", "健太"), Line(4, "e", "健太"),
        ]);

        Assert.Equal(["美咲"], samples.Keys);
    }

    [Fact]
    public void Minimo_conta_falas_com_texto_e_ignora_as_vazias()
    {
        var samples = SpeakerSamples.From([Line(0, "a", "美咲"), Line(1, "  ", "美咲"), Line(2, "b", "美咲")]);

        Assert.Empty(samples);
    }

    [Fact]
    public void Minimo_conta_a_mesma_fala_repetida()
    {
        var samples = SpeakerSamples.From([Line(0, "うん", "美咲"), Line(1, "うん", "美咲"), Line(2, "うん", "美咲")]);

        Assert.Equal(["うん"], samples["美咲"]);
    }

    [Fact]
    public void Minimo_e_ajustavel()
    {
        Assert.Contains("健太", SpeakerSamples.From([Line(0, "a", "健太")], minLines: 1).Keys);
        Assert.Throws<ArgumentOutOfRangeException>(() => SpeakerSamples.From([], minLines: 0));
    }

    [Fact]
    public void Limita_a_amostra_a_vinte_falas_espalhadas_pelo_texto()
    {
        var units = Enumerable.Range(0, 100).Select(n => Line(n, $"fala {n}", "美咲"));

        var sample = SpeakerSamples.From(units)["美咲"];

        Assert.Equal(SpeakerSamples.DefaultLines, sample.Count);
        Assert.Equal("fala 0", sample[0]);
        Assert.True(sample[^1] != "fala 19", "a amostra não pode ser só o começo");
        Assert.Equal(sample.Distinct(), sample);
    }

    [Fact]
    public void Descarta_falas_repetidas_e_vazias()
    {
        var samples = SpeakerSamples.From([Line(0, "うん", "美咲"), Line(1, "うん", "美咲"), Line(2, "  ", "美咲"), Line(3, "はい", "美咲")]);

        Assert.Equal(["うん", "はい"], samples["美咲"]);
    }

    [Fact]
    public void Aceita_um_limite_menor()
    {
        var units = Enumerable.Range(0, 10).Select(n => Line(n, $"fala {n}", "美咲"));

        Assert.Equal(3, SpeakerSamples.From(units, maxLines: 3)["美咲"].Count);
    }
}
