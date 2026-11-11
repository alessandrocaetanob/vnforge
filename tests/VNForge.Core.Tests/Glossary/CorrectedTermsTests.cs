using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Glossary;

public class CorrectedTermsTests
{
    private static TextUnit Linha(int n, string original, string? ia, string pessoa, TranslationStatus status = TranslationStatus.Translated) =>
        new($"a:{n:D6}", TextUnitKind.Dialogue, null, original, pessoa, status) { AiTranslation = ia };

    // Três falas com 魔法学院, onde a IA escreveu "escola de magia" e a pessoa trocou por "Academia".
    private static List<TextUnit> TresCorrigidas() =>
    [
        Linha(0, "魔法学院に行く", "Vou para a escola de magia hoje.", "Vou para a Academia hoje."),
        Linha(1, "魔法学院は広い", "A escola de magia é grande.", "A Academia é grande."),
        Linha(2, "魔法学院の朝", "Uma manhã na escola de magia.", "Uma manhã na Academia."),
    ];

    [Fact]
    public void Duas_falas_nao_bastam()
    {
        Assert.Empty(RepeatedTerms.Corrected(TresCorrigidas().Take(2)));
        Assert.Single(RepeatedTerms.Corrected(TresCorrigidas().Take(2), threshold: 2));
    }

    [Fact]
    public void Traducoes_diferentes_para_o_mesmo_trecho_nao_viram_par()
    {
        List<TextUnit> units =
        [
            Linha(0, "魔法学院に行く", "Vou para a escola de magia hoje.", "Vou para a Academia hoje."),
            Linha(1, "魔法学院は広い", "A escola de magia é grande.", "A Escola é grande."),
            Linha(2, "魔法学院の朝", "Uma manhã na escola de magia.", "Uma manhã no Colégio."),
        ];

        Assert.Empty(RepeatedTerms.Corrected(units));
    }
}
