using System.Text;

namespace VNForge.Core.Formatting;

/// <summary>Largura de um caractere, em colunas da caixa de texto do jogo.</summary>
public delegate int CharWidth(Rune rune);

/// <summary>Tabelas de largura prontas para uso. Qualquer <see cref="CharWidth"/> pode ser injetada no lugar.</summary>
public static class CharWidths
{
    /// <summary>
    /// Padrão do jogo testado no M1: letra latina (incluindo acentuada) ocupa meia célula, ou seja, 1 coluna;
    /// japonês e demais caracteres ocupam uma célula inteira, ou seja, 2 colunas. Katakana de meia largura
    /// (U+FF61 a U+FF9F) conta 1.
    /// </summary>
    public static CharWidth HalfWidthLatin { get; } = static rune =>
    {
        int v = rune.Value;
        if (v < 0x2000 || (v >= 0xFF61 && v <= 0xFF9F))
        {
            return 1;
        }

        return 2;
    };

    /// <summary>Cada caractere ocupa 1 coluna. Útil para fontes monoespaçadas e para testes.</summary>
    public static CharWidth Uniform { get; } = static _ => 1;
}
