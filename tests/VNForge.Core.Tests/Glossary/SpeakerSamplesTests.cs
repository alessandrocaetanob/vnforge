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
    public void Aceita_um_limite_menor()
    {
        var units = Enumerable.Range(0, 10).Select(n => Line(n, $"fala {n}", "美咲"));

        Assert.Equal(3, SpeakerSamples.From(units, maxLines: 3)["美咲"].Count);
    }

    private static TextUnit Spoken(int n, string source, string? speaker, string? translation = null, TranslationStatus status = TranslationStatus.Untranslated, string? chosen = null) =>
        new($"s:{n}", TextUnitKind.Dialogue, speaker, source, translation, status) { ChosenSpeaker = chosen };

    [Fact]
    public void Ficha_da_fala_repetida_fica_com_a_traducao_de_uma_ocorrencia_traduzida()
    {
        var sheet = SpeakerSamples.Sheets([Spoken(0, "はい", "美咲"), Spoken(1, "はい", "美咲", "Sim.", TranslationStatus.Translated)])["美咲"];

        var only = Assert.Single(sheet.Longest);
        Assert.Equal("Sim.", only.Translation);
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
    public void Ficha_com_muitas_falas_guarda_so_as_mais_longas_e_a_repetida_curta_nao_volta()
    {
        var units = Enumerable.Range(1, 40).Select(n => Spoken(n, new string('あ', n), "美咲")).ToList();
        units.AddRange(Enumerable.Range(41, 5).Select(n => Spoken(n, "あ", "美咲")));
        units.Add(Spoken(99, new string('あ', 3), "美咲", "Três."));

        var sheet = SpeakerSamples.Sheets(units, longest: 3)["美咲"];

        Assert.Equal([40, 39, 38], sheet.Longest.Select(l => l.Source.Length));
        Assert.Equal(46, sheet.Total);
    }
}
