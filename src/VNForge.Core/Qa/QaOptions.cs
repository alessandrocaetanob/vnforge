namespace VNForge.Core.Qa;

/// <summary>Filtros de uma conferência (<c>vnf check --rules --severity</c>).</summary>
/// <param name="Rules">Só estas regras rodam; nulo ou vazio roda todas.</param>
/// <param name="MinimumSeverity">Só ocorrências desta gravidade para cima: <see cref="QaSeverity.Warning"/> lista tudo, <see cref="QaSeverity.Error"/> só os erros.</param>
public sealed record QaOptions(IReadOnlyCollection<string>? Rules = null, QaSeverity MinimumSeverity = QaSeverity.Warning)
{
    /// <summary>Todas as regras, todas as gravidades.</summary>
    public static QaOptions All { get; } = new();
}
