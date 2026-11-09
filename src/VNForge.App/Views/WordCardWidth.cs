using Avalonia.Data.Converters;

namespace VNForge.App.Views;

/// <summary>A largura do cartão do dicionário (#538): a do original no inspetor, entre 300 e 400 px. Sem o limite o cartão de aviso, que não tem sentidos, tomaria a largura do texto e cobriria a lista de linhas.</summary>
public static class WordCardWidth
{
    public const double Min = 300;
    public const double Max = 400;

    public static readonly IValueConverter Clamp = new FuncValueConverter<double, double>(width => Math.Clamp(width, Min, Max));
}
