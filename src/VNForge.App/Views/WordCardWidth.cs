using Avalonia.Data.Converters;

namespace VNForge.App.Views;

/// <summary>A largura do cartão do dicionário (#538): os 370 px do <c>.pop</c> do quadro, menos quando o original, onde o cartão se alinha, é mais estreito (janela pequena): aí o cartão não passa da coluna do inspetor nem cobre a lista de linhas.</summary>
public static class WordCardWidth
{
    public const double Value = 370;

    public static readonly IValueConverter Fit = new FuncValueConverter<double, double>(box => Math.Min(Value, box));
}
