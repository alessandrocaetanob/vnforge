using System.Text;

namespace VNForge.Core.Text;

/// <summary>
/// Estratégia <c>cp1252</c>: grava o texto exibido em Windows-1252, um byte por letra acentuada.
/// Dois grupos de caracteres da própria página 1252 são recusados, porque o motor do YU-RIS os lê errado (doc 06, "Acentos e fonte"):
/// o byte <c>0xEF</c> (<c>ï</c>) é o prefixo dos códigos de controle, e os bytes <c>0x81</c> a <c>0x9F</c> (aspas curvas, reticências, meia-risca, travessão,
/// marcador, <c>™</c> e afins) são lead bytes do Shift-JIS: o motor junta o byte com o seguinte e a letra depois deles some.
/// Caractere fora da página 1252 não é substituído nem descartado: <see cref="CodePageTextEncoder.Encode"/> lança exceção.
/// </summary>
public sealed class Cp1252TextEncoder() : CodePageTextEncoder(GameEncodings.Windows1252), IExplainingTextEncoder
{
    private const char ControlPrefixLetter = 'ï';
    private const int FirstLeadByte = 0x81;
    private const int LastLeadByte = 0x9F;

    public override string Id => "cp1252";

    protected override string DisplayName => "a estratégia cp1252 não grava";

    protected override bool IsRefused(Rune rune) => rune.Value == ControlPrefixLetter || IsLeadByteCharacter(rune);

    protected override string Hint(string text, IReadOnlyList<int> invalid)
    {
        var hints = new List<string>();
        if (invalid.Any(i => text[i] == ControlPrefixLetter))
        {
            hints.Add(ExplainRefusal(ControlPrefixLetter)!);
        }

        var lead = invalid.FirstOrDefault(i => Rune.TryGetRuneAt(text, i, out var rune) && IsLeadByteCharacter(rune), -1);
        if (lead >= 0)
        {
            hints.Add(ExplainRefusal(char.ConvertToUtf32(text, lead))!);
        }

        return hints.Count == 0 ? string.Empty : " " + string.Join(' ', hints);
    }

    /// <inheritdoc />
    public string? ExplainRefusal(int codePoint)
    {
        if (codePoint == ControlPrefixLetter)
        {
            return "O ï vira o byte 0xEF, o prefixo dos códigos de controle.";
        }

        if (!Rune.TryCreate(codePoint, out var rune) || !IsLeadByteCharacter(rune))
        {
            return null;
        }

        var swap = codePoint switch
        {
            0x201C or 0x201D or 0x201E => "aspas retas (\")",
            0x2018 or 0x2019 or 0x201A => "aspas retas (')",
            0x2026 => "três pontos (...)",
            0x2013 or 0x2014 or 0x2022 => "um hífen (-)",
            _ => "um caractere comum do teclado",
        };
        return $"Na cp1252 ele vira o byte 0x{LeadByte(rune):X2}, que o jogo lê como o início de um caractere duplo e engole a letra seguinte. Troque por {swap}.";
    }

    /// <summary>Verdadeiro quando a página 1252 grava o caractere num byte de <c>0x81</c> a <c>0x9F</c>, que o motor trata como lead byte do Shift-JIS.</summary>
    private static bool IsLeadByteCharacter(Rune rune) => LeadByte(rune) is >= FirstLeadByte and <= LastLeadByte;

    /// <summary>O byte único que a página 1252 usa para o caractere; o que ela não grava (troca por <c>?</c>) cai fora da faixa.</summary>
    private static int LeadByte(Rune rune)
    {
        var bytes = GameEncodings.Windows1252.GetBytes(rune.ToString());
        return bytes.Length == 1 ? bytes[0] : -1;
    }
}
