namespace VNForge.Core.Jmdict;

/// <summary>Um sentido de uma entrada do JMdict: as classes gramaticais (códigos do JMdict, como <c>n</c>, <c>vs</c>, <c>v5k-s</c>) e as traduções em inglês.</summary>
public sealed record JmdictSense(IReadOnlyList<string> PartsOfSpeech, IReadOnlyList<string> Glosses)
{
    /// <summary>As traduções em uma linha, separadas por ponto e vírgula, como o JMdict as agrupa.</summary>
    public string GlossText => string.Join("; ", Glosses);
}

/// <summary>
/// Uma entrada do JMdict. <paramref name="Rank"/> vale de 1 (muito comum) a 100 (sem marca de frequência): vem dos <c>nf01</c> a <c>nf48</c>, e as marcas
/// <c>news1</c>, <c>ichi1</c>, <c>spec1</c> e <c>gai1</c> valem 12; as de nível 2 valem 30. Menor é mais comum.
/// </summary>
public sealed record JmdictEntry(int Id, IReadOnlyList<string> Kanji, IReadOnlyList<string> Readings, IReadOnlyList<JmdictSense> Senses, int Rank)
{
    public const int NoRank = 100;

    /// <summary>As classes gramaticais de todos os sentidos, sem repetir, na ordem em que aparecem. É o que a etiqueta da entrada mostra.</summary>
    public IReadOnlyList<string> PartsOfSpeech => [.. Senses.SelectMany(s => s.PartsOfSpeech).Distinct(StringComparer.Ordinal)];
}

/// <summary>
/// A palavra achada num texto. <paramref name="Start"/> e <paramref name="Length"/> dizem o trecho do texto que ela ocupa (a forma conjugada, se for o caso);
/// <paramref name="Word"/> é a forma de dicionário que bateu; <paramref name="Inflection"/> diz como o trecho foi conjugado (vazio quando o trecho já é a forma de dicionário).
/// <paramref name="Entries"/> vem da mais comum para a menos comum e nunca é vazia.
/// </summary>
public sealed record JmdictMatch(int Start, int Length, string Word, IReadOnlyList<JmdictEntry> Entries, IReadOnlyList<string> Inflection)
{
    public JmdictEntry Main => Entries[0];

    public bool IsInflected => Inflection.Count > 0;
}
