using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Caractere que a estratégia de codificação atual não grava (<c>encoding.strategy</c>). O build recusa a linha, então é erro e "marcar como ok" não
/// o silencia. Caractere que a estratégia troca por um substituto (<c>fold</c>) não entra: isso é decisão da estratégia, listada pelo relatório de codificação.
/// Sem encoder no contexto, a regra não confere nada.
/// </summary>
public sealed class UnencodableCharacterRule : UnitQaRule
{
    public const string RuleId = "caractere-nao-codificavel";

    public override string Id => RuleId;

    public override string Description => "Caractere da tradução que a estratégia de codificação do projeto não grava.";

    public override QaSeverity Severity => QaSeverity.Error;

    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        if (context.Encoder is not { } encoder || BuiltText.For(unit, context.Formatting) is not { } built)
        {
            yield break;
        }

        foreach (var text in BuiltText.PlainSegments(built))
        {
            foreach (var index in encoder.FindUnencodable(text))
            {
                var length = char.IsHighSurrogate(text[index]) && index + 1 < text.Length ? 2 : 1;
                var character = text.Substring(index, length);
                var codePoint = char.ConvertToUtf32(character, 0);
                yield return $"o caractere '{character}' (U+{codePoint:X4}) não existe na estratégia '{encoder.Id}'.";
            }
        }
    }
}
