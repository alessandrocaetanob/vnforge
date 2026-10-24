using VNForge.Core;

namespace VNForge.Cli;

/// <summary>
/// Uso inválido da linha de comando (opção desconhecida, opções que se excluem). Sai com código 1.
/// <paramref name="hint"/> é a segunda linha do erro em texto ("→ ..."): o que fazer para resolver. O <c>--json</c> não a leva.
/// </summary>
public sealed class UsageException(string message, string? hint = null) : VNForgeException(message)
{
    public string? Hint { get; } = hint;
}
