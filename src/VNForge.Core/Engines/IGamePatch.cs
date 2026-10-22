namespace VNForge.Core.Engines;

/// <summary>
/// Uma alteração opcional na pasta do jogo que não é script nem pacote (ex.: patch de codepage no executável).
/// </summary>
/// <remarks>
/// Quem aplica é o fluxo de injeção, que faz backup de <see cref="AffectedFiles"/> antes de chamar
/// <see cref="Apply"/> e sabe restaurá-los. Um patch nunca faz o próprio backup.
/// </remarks>
public interface IGamePatch
{
    /// <summary>Identificador estável do patch, em minúsculas (ex.: <c>"exe-codepage"</c>).</summary>
    string Id { get; }

    /// <summary>Descrição curta, em português, do que o patch altera.</summary>
    string Description { get; }

    /// <summary>Arquivos que <see cref="Apply"/> altera, relativos à pasta do jogo. O fluxo de injeção faz o backup deles.</summary>
    IReadOnlyList<string> AffectedFiles { get; }

    /// <summary>
    /// Confere, só lendo, se o patch serve para este jogo (ex.: o hash do executável é o esperado).
    /// </summary>
    /// <param name="gameDir">Caminho da pasta do jogo.</param>
    /// <param name="reason">Quando devolve falso, o motivo em português; senão, nulo.</param>
    bool CanApply(string gameDir, out string? reason);

    /// <summary>
    /// Aplica o patch. Chamar só depois do backup e de <see cref="CanApply"/> devolver verdadeiro.
    /// </summary>
    /// <param name="gameDir">Caminho da pasta do jogo.</param>
    void Apply(string gameDir);
}
