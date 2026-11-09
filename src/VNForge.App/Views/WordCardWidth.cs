using Avalonia.Data.Converters;

namespace VNForge.App.Views;

/// <summary>A largura do cartão do dicionário (#538): o <c>.pop</c> do quadro tem 360 px em 遅刻 e 372 em 行こう, então o cartão acompanha o conteúdo entre <see cref="Min"/> e <see cref="Max"/>. Quando o original, onde o cartão se alinha, é mais estreito (janela pequena), os dois limites descem até ele: o cartão não passa da coluna do inspetor nem cobre a lista de linhas. Antes da primeira medida o original vale 0 e valem os limites do quadro.</summary>
public static class WordCardWidth
{
    public const double Min = 360;
    public const double Max = 372;

    public static readonly IValueConverter FitMin = new FuncValueConverter<double, double>(box => Limit(Min, box));

    public static readonly IValueConverter FitMax = new FuncValueConverter<double, double>(box => Limit(Max, box));

    private static double Limit(double limit, double box) => box > 0 ? Math.Min(limit, box) : limit;
}
