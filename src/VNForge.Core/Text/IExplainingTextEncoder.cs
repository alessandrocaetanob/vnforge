namespace VNForge.Core.Text;

/// <summary>
/// Encoder que sabe dizer por que recusa um caractere que a página de código até representa, e o que usar no lugar.
/// A Conferência e a mensagem de erro de codificação acrescentam esse texto ao aviso.
/// </summary>
public interface IExplainingTextEncoder : ITextEncoder
{
    /// <summary>O motivo da recusa e a troca sugerida para o caractere <paramref name="codePoint"/>; nulo quando o encoder não tem o que acrescentar.</summary>
    string? ExplainRefusal(int codePoint);
}
