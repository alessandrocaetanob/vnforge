using System.Text;

namespace VNForge.Core.Text;

/// <summary>
/// Estratégia <c>cp1252</c>: grava o texto exibido em Windows-1252, um byte por letra acentuada.
/// O byte <c>0xEF</c> (<c>ï</c>) é o prefixo dos códigos de controle do YU-RIS, então <c>ï</c> é recusado.
/// Caractere fora da página 1252 não é substituído nem descartado: <see cref="CodePageTextEncoder.Encode"/> lança exceção.
/// </summary>
public sealed class Cp1252TextEncoder() : CodePageTextEncoder(GameEncodings.Windows1252)
{
    private const char ControlPrefixLetter = 'ï';

    public override string Id => "cp1252";

    protected override string DisplayName => "a estratégia cp1252 não grava";

    protected override bool IsRefused(Rune rune) => rune.Value == ControlPrefixLetter;

    protected override string Hint(string text, IReadOnlyList<int> invalid) =>
        invalid.Any(i => text[i] == ControlPrefixLetter)
            ? " O ï vira o byte 0xEF, o prefixo dos códigos de controle."
            : string.Empty;
}
