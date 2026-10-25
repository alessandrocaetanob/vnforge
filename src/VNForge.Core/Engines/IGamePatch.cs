namespace VNForge.Core.Engines;

/// <summary>
/// Uma alteração opcional na pasta do jogo que não é script nem pacote (ex.: troca do nome da fonte no executável).
/// </summary>
/// <remarks>
/// Quem aplica é o fluxo de injeção (<c>GameInjector.PlanPatch</c> e <c>InjectPatch</c>). Ele chama <see cref="CanApply"/> na pasta do
/// jogo, copia <see cref="AffectedFiles"/> para uma pasta de trabalho e chama <see cref="Apply"/> nessa cópia, nunca no jogo; só então faz o
/// backup dos originais, grava o arquivo alterado e sabe restaurá-lo. Por isso <see cref="Apply"/> trabalha só com o <c>gameDir</c> que
/// recebe, que tem a mesma estrutura de pastas da pasta do jogo, e um patch nunca faz o próprio backup.
/// </remarks>
public interface IGamePatch
{
    /// <summary>Identificador estável do patch, em minúsculas (ex.: <c>"exe-font"</c>).</summary>
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
    /// Aplica o patch. Chamar só depois de <see cref="CanApply"/> devolver verdadeiro. Pode lançar <see cref="InvalidOperationException"/>
    /// se o arquivo que recebe não é o que <see cref="CanApply"/> aceitou. É idempotente: aplicar de novo num arquivo já alterado não muda nada.
    /// </summary>
    /// <param name="gameDir">Pasta com os <see cref="AffectedFiles"/> a alterar: a cópia de trabalho do fluxo de injeção, ou a pasta do jogo.</param>
    void Apply(string gameDir);
}
