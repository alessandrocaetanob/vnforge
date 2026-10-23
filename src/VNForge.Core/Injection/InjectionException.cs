namespace VNForge.Core.Injection;

/// <summary>Falha esperada de <c>inject</c> ou <c>restore</c> (build ausente, backup corrompido, hash divergente, gravação recusada).</summary>
public sealed class InjectionException : VNForgeException
{
    public InjectionException(string message)
        : base(message)
    {
    }

    public InjectionException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
