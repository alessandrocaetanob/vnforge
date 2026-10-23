using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Tradução com mais bytes que o limite do campo no jogo (<c>byteLimits</c> do perfil de formatação, por papel de linha: nomes, escolhas). Conta os bytes da
/// estratégia de codificação atual, só dos trechos de texto, sem os marcadores. É erro: o campo do jogo não comporta a linha. Sem encoder ou sem limite para o
/// papel da linha, não confere nada; caractere não codificável fica com <see cref="UnencodableCharacterRule"/>.
/// </summary>
public sealed class FieldByteLimitRule : UnitQaRule
{
    public const string RuleId = "tamanho-em-bytes";

    public override string Id => RuleId;

    public override string Description => "Tradução com mais bytes que o limite do campo no jogo (byteLimits do perfil de formatação).";

    public override QaSeverity Severity => QaSeverity.Error;

    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        if (context.Encoder is not { } encoder
            || !context.Formatting.ByteLimits.TryGetValue(unit.Kind, out var limit)
            || BuiltText.For(unit, context.Formatting) is not { } built)
        {
            return [];
        }

        var bytes = 0;
        foreach (var segment in BuiltText.PlainSegments(built))
        {
            if (encoder.FindUnencodable(segment).Count > 0)
            {
                return [];
            }

            bytes += encoder.Encode(segment).Length;
        }

        return bytes > limit
            ? [$"a tradução tem {bytes} bytes em '{encoder.Id}' e o campo comporta {limit}."]
            : [];
    }
}
