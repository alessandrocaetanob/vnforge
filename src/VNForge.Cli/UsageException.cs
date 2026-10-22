using VNForge.Core;

namespace VNForge.Cli;

/// <summary>Uso inválido da linha de comando (opção desconhecida, opções que se excluem). Sai com código 1.</summary>
public sealed class UsageException(string message) : VNForgeException(message);
