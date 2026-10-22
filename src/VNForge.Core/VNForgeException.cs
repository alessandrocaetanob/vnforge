namespace VNForge.Core;

/// <summary>
/// Base dos erros esperados do Core (arquivo inválido, versão desconhecida e similares).
/// A mensagem é em português e vai direto ao usuário; qualquer outra exceção é tratada como bug.
/// </summary>
public class VNForgeException : Exception
{
    public VNForgeException(string message)
        : base(message)
    {
    }

    public VNForgeException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
