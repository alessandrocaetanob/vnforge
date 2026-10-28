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

    private static TextUnit Spoken(int n, string source, string? speaker, string? translation = null, TranslationStatus status = TranslationStatus.Untranslated, string? chosen = null) =>
        new($"s:{n}", TextUnitKind.Dialogue, speaker, source, translation, status) { ChosenSpeaker = chosen };

    [Fact]
    public void Ficha_traz_as_cinco_falas_mais_longas_com_a_traducao_da_mais_longa_para_a_menor()
    {
        var units = Enumerable.Range(1, 8).Select(n => Spoken(n, new string('あ', n), "美咲", $"t{n}", TranslationStatus.Translated));

        var sheet = SpeakerSamples.Sheets(units)["美咲"];

        Assert.Equal([8, 7, 6, 5, 4], sheet.Longest.Select(l => l.Source.Length));
        Assert.Equal(["t8", "t7", "t6", "t5", "t4"], sheet.Longest.Select(l => l.Translation));
    }

    [Fact]
    public void Ficha_desempata_pela_ordem_do_texto_e_nao_repete_a_mesma_fala()
    {
        var sheet = SpeakerSamples.Sheets([Spoken(0, "いいえ", "美咲"), Spoken(1, "はいはい", "美咲"), Spoken(2, "はいはい", "美咲"), Spoken(3, "うんうん", "美咲")])["美咲"];

        Assert.Equal(["はいはい", "うんうん", "いいえ"], sheet.Longest.Select(l => l.Source));
    }

    [Fact]
    public void Ficha_deixa_a_traducao_vazia_como_nula_e_ignora_narracao_sinais_e_falas_em_branco()
    {
        var sheets = SpeakerSamples.Sheets([
            Spoken(0, "こんにちは", "美咲", "  "), Spoken(1, "narração", null), Spoken(2, "…", "？？？"), Spoken(3, "   ", "美咲")]);

        Assert.Equal(["美咲"], sheets.Keys);
        var only = Assert.Single(sheets["美咲"].Longest);
        Assert.Equal(("こんにちは", null), (only.Source, only.Translation));
    }

    [Fact]
    public void Ficha_conta_so_as_linhas_da_IA_esperando_revisao_e_segue_o_falante_escolhido()
    {
        var sheets = SpeakerSamples.Sheets([
            Spoken(0, "a", "美咲", "x", TranslationStatus.Machine),
            Spoken(1, "b", "美咲", "x", TranslationStatus.Translated),
            Spoken(2, "c", "美咲", "x", TranslationStatus.Machine),
            Spoken(3, "d", "美咲", null, TranslationStatus.Untranslated),
            Spoken(4, "e", "健太", "x", TranslationStatus.Machine, chosen: "美咲")]);

        Assert.Equal(3, sheets["美咲"].AwaitingReview);
        Assert.False(sheets.ContainsKey("健太"));
    }

    [Fact]
    public void Ficha_acha_o_personagem_pelo_nome_de_largura_diferente()
    {
        var sheets = SpeakerSamples.Sheets([Spoken(0, "a", "Ａ子")]);

        Assert.True(sheets.ContainsKey("A子"));
    }
}
