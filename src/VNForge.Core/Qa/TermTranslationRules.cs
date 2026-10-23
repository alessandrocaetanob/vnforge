using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Confere termos fixos: quando o original da linha contém um termo definido (<see cref="GlossaryTerm"/> ou <see cref="CharacterEntry"/>),
/// a tradução precisa conter o alvo definido. A comparação ignora maiúsculas e minúsculas, porque o termo pode abrir a frase.
/// </summary>
internal static class TermTranslation
{
    public static IEnumerable<string> Mismatches<T>(TextUnit unit, IEnumerable<T> entries, Func<T, (string Source, string Target)> parts, string noun)
    {
        if (!unit.HasTranslation)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            var (source, target) = parts(entry);
            if (source.Length == 0 || target.Length == 0)
            {
                continue;
            }

            if (unit.Source.Contains(source, StringComparison.Ordinal)
                && !unit.Translation!.Contains(target, StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{noun} '{source}' deveria ser traduzido como '{target}'";
            }
        }
    }
}

/// <summary>Termo do glossário no original, mas a tradução não usa o alvo definido.</summary>
public sealed class GlossaryTermRule : UnitQaRule
{
    public override string Id => "glossario-diferente";

    public override string Description => "Termo do glossário traduzido diferente do definido.";

    public override QaSeverity Severity => QaSeverity.Warning;

    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context) =>
        TermTranslation.Mismatches(unit, context.Glossary, t => (t.Source, t.Target), "termo");
}

/// <summary>Nome de personagem no original, mas a tradução não usa o nome definido em <c>characters.json</c>.</summary>
public sealed class CharacterNameRule : UnitQaRule
{
    public override string Id => "personagem-diferente";

    public override string Description => "Nome de personagem traduzido diferente do definido.";

    public override QaSeverity Severity => QaSeverity.Warning;

    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context) =>
        TermTranslation.Mismatches(unit, context.Characters, c => (c.Source, c.Target), "personagem");
}
