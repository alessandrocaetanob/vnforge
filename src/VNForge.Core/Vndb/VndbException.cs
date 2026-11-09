namespace VNForge.Core.Vndb;

/// <summary>Erro esperado ao falar com o VNDB (rede fora, token inválido, limite de pedidos). A mensagem é em português e vai direto ao usuário.</summary>
public sealed class VndbException : VNForgeException
{
    public VndbException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>O código HTTP da resposta, quando houve resposta.</summary>
    public int? StatusCode { get; }
}
