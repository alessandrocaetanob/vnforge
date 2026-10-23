using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Caixa de texto com mais linhas que o <c>wrap.maxLines</c> do perfil de formatação. Mede o texto como o build o gravaria: a tradução passa pelo
/// pipeline quando a linha é de um papel que o perfil formata. Sem <c>maxLines</c>, a regra não confere nada. É aviso: o revisor pode aceitar a caixa maior.
/// </summary>
public sealed class BoxLineLimitRule : UnitQaRule
{
    public const string RuleId = "caixa-acima-do-limite";

    public override string Id => RuleId;

    public override string Description => "Caixa de texto com mais linhas que o máximo do perfil de formatação (wrap.maxLines).";

    public override QaSeverity Severity => QaSeverity.Warning;

    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        var profile = context.Formatting;
        if (profile.Wrap.MaxLines is not { } maxLines || BuiltText.For(unit, profile) is not { } built)
        {
            return [];
        }

        IReadOnlyList<IReadOnlyList<int>> boxes;
        try
        {
            boxes = LineMeasure.Columns(built, profile.Wrap.ToOptions().Width);
        }
        catch (ArgumentException)
        {
            return [];
        }

        return [.. boxes
            .Select((lines, index) => (lines.Count, index))
            .Where(box => box.Count > maxLines)
            .Select(box => $"caixa {box.index + 1} com {box.Count} linhas (máximo {maxLines}).")];
    }
}
