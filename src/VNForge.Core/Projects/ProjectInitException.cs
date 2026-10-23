namespace VNForge.Core.Projects;

/// <summary>Erro previsto ao criar um projeto a partir de uma pasta de jogo (pasta inexistente, engine não reconhecida, executável ambíguo).</summary>
public sealed class ProjectInitException(string message) : VNForgeException(message);
