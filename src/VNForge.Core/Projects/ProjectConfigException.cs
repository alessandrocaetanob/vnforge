namespace VNForge.Core.Projects;

public sealed class ProjectConfigException : VNForgeException
{
    public ProjectConfigException(string field, string reason)
        : base($"vnforge.json inválido em {field}: {reason}")
    {
        Field = field;
    }

    public ProjectConfigException(string message)
        : base(message)
    {
    }

    /// <summary>Caminho JSON do campo com problema (por exemplo <c>$.engine.id</c>); nulo quando o erro é do arquivo.</summary>
    public string? Field { get; }
}
