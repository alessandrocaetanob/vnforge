namespace VNForge.Core.Qa;

/// <summary>
/// Uma regra que sabe corrigir o que acha, sem risco de mudar o sentido da tradução. O <see cref="QaFixer"/> chama só as regras que implementam
/// esta interface; a correção é uma função pura do texto e idempotente: <c>Fix(Fix(t)) == Fix(t)</c>.
/// </summary>
public interface IQaFixer
{
    /// <summary>Id da regra (<see cref="IQaRule.Id"/>) que esta correção resolve.</summary>
    string Id { get; }

    /// <summary>A <paramref name="translation"/> sem as ocorrências que a regra aponta e que são seguras de corrigir; igual à entrada quando não há nada a fazer.</summary>
    string Fix(string translation);
}
