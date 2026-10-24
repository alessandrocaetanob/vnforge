using System.Text.RegularExpressions;

namespace VNForge.App.Services;

/// <summary>O texto de uma linha como a grade o mostra: sem os marcadores de pausa, cor e usuário e com a quebra de linha virada em espaço.</summary>
public static partial class LineText
{
    [GeneratedRegex(@"\\[pcu]")]
    private static partial Regex Pauses();

    public static string Plain(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : Pauses().Replace(text, string.Empty).Replace("\\n", " ", StringComparison.Ordinal);

    /// <summary>Quantos caracteres a tradução tem para quem lê: o texto de <see cref="Plain"/>.</summary>
    public static int Length(string? text) => Plain(text).Length;
}
