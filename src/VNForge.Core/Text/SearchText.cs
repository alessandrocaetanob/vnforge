using System.Globalization;
using System.Text;

namespace VNForge.Core.Text;

/// <summary>Normalização do texto de busca.</summary>
public static class SearchText
{
    /// <summary>Minúsculas e sem acento, para "conferencia" achar "Conferência".</summary>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}
