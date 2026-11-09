namespace VNForge.Core.Vndb;

/// <summary>O jogo do VNDB não pôde ser ligado ao projeto ou o <c>vndb.json</c> não é válido.</summary>
public sealed class VndbLinkException(string message, Exception? innerException = null) : VNForgeException(message, innerException);
