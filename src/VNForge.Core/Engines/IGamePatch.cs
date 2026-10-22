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

    /// <summary>
    /// Arquivos que <see cref="Apply"/> altera, relativos à pasta do jogo. O fluxo de injeção resolve cada caminho
    /// com <see cref="GamePaths.Resolve"/>, que recusa os que saem da pasta, e faz o backup deles.
    /// Pode listar um arquivo que ainda não existe: o fluxo registra a ausência e a restauração o remove.
    /// </summary>
    IReadOnlyList<string> AffectedFiles { get; }

    /// <summary>
    /// Confere, só lendo, se o patch serve para este jogo (ex.: o hash do executável é o esperado).
    /// Devolve verdadeiro também quando o patch já está aplicado.
    /// </summary>
    /// <param name="gameDir">Caminho da pasta do jogo.</param>
    /// <param name="reason">Quando devolve falso, o motivo em português; senão, nulo.</param>
    bool CanApply(string gameDir, out string? reason);

    /// <summary>
    /// Aplica o patch. Chamar só depois do backup e de <see cref="CanApply"/> devolver verdadeiro.
    /// É idempotente: aplicar de novo num jogo já alterado não muda nada.
    /// </summary>
    /// <param name="gameDir">Caminho da pasta do jogo.</param>
    void Apply(string gameDir);
}
