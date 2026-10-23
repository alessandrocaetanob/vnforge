namespace VNForge.Core.Text;

/// <summary>
/// Estratégia que troca alguns caracteres por outros antes de codificar (ex.: <c>fold</c> troca <c>«</c> por <c>"</c>). É o que o relatório de
/// codificação usa para dizer o substituto de cada caractere; quem só aceita ou recusa um caractere (<see cref="ShiftJisTextEncoder"/>) não implementa.
/// </summary>
public interface IReplacingTextEncoder : ITextEncoder
{
    /// <summary>Diz por qual texto <see cref="ITextEncoder.Encode"/> troca o caractere, sem lançar exceção e sem efeito colateral.</summary>
    /// <param name="character">Um caractere Unicode (um ou, em par substituto, dois <c>char</c>).</param>
    /// <returns>O texto que vai no lugar, ou <see langword="null"/> quando o caractere segue como está (codificável ou recusado).</returns>
    string? GetReplacement(string character);
}
