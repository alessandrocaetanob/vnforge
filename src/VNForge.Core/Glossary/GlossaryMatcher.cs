namespace VNForge.Core.Glossary;

/// <summary>Procura termos do glossário no original de uma linha. Mesmo critério do QA e do lote de tradução: contido, ordinal, sem os espaços das pontas do termo.</summary>
public static class GlossaryMatcher
{
    /// <summary>Os termos cujo original aparece em <paramref name="source"/>, na ordem do glossário; termo vazio nunca acerta.</summary>
    public static IReadOnlyList<GlossaryTerm> Find(string? source, IEnumerable<GlossaryTerm> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (string.IsNullOrEmpty(source))
        {
            return [];
        }

        return terms
            .Where(t => t.Source.Trim() is { Length: > 0 } term && source.Contains(term, StringComparison.Ordinal))
            .ToList();
    }
}
