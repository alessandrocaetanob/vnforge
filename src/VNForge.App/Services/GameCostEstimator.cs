using VNForge.AI.Cost;
using VNForge.Core.Text;

namespace VNForge.App.Services;

/// <summary>
/// O gasto estimado do jogo inteiro antes de o texto ser extraído, para o passo de IA do Novo projeto. Ainda não há linhas para contar, então
/// parte do número de scripts que a detecção achou e de uma linha de tamanho médio. É ordem de grandeza: a tela diz isso e o número exato
/// vem depois da extração (a estimativa por linhas reais dos Ajustes e de Traduzir com IA). O preço sai do <see cref="CostEstimator"/>, o mesmo da CLI.
/// </summary>
public static class GameCostEstimator
{
    /// <summary>Linhas por script: a média do projeto de exemplo do plano da interface v3 (48.211 linhas em 312 scripts). Não foi medida num jogo real.</summary>
    public const int LinesPerScript = 155;

    /// <summary>Uma fala de 20 caracteres em japonês, o tamanho que reproduz a conta de exemplo do plano (cerca de 20 tokens por linha).</summary>
    private const string SampleSource = "「今日も遅刻ギリギリだねと彼女は笑った」";

    /// <summary>As linhas esperadas para <paramref name="scripts"/> scripts; nulo quando a engine não disse quantos são.</summary>
    public static int? LinesFor(int? scripts) => scripts is > 0 ? scripts * LinesPerScript : null;

    /// <summary>O custo em dólar de traduzir <paramref name="lines"/> linhas de tamanho médio com cada modelo; vazio sem linhas.</summary>
    public static IReadOnlyDictionary<string, decimal> DollarsByModel(int lines, IEnumerable<string> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        if (lines <= 0)
        {
            return new Dictionary<string, decimal>();
        }

        var unit = new TextUnit("estimativa", TextUnitKind.Dialogue, null, SampleSource, null, TranslationStatus.Untranslated);
        return CostEstimator.EstimateForModels(Enumerable.Repeat(unit, lines), models).ToDictionary(p => p.Key, p => p.Value.Dollars);
    }
}
