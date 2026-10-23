namespace VNForge.Core.Qa;

/// <summary>
/// Trecho exato da tradução que disparou uma regra de texto. <paramref name="Start"/> é o índice em <c>char</c> UTF-16 na tradução inteira
/// (marcadores contam), então quem corrige (<c>--fix</c>) pode trocar <c>Translation.Substring(Start, Length)</c> sem reler o texto.
/// </summary>
/// <param name="Start">Índice do primeiro caractere do trecho, a partir de 0.</param>
/// <param name="Length">Tamanho do trecho em <c>char</c>.</param>
/// <param name="Text">O trecho, igual a <c>Translation.Substring(Start, Length)</c>.</param>
public readonly record struct QaSpan(int Start, int Length, string Text);
