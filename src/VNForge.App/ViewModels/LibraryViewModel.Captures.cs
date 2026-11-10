using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

public sealed partial class LibraryViewModel
{
    /// <summary>O jogo da Biblioteca ligado ao projeto de tradução dessa pasta, ou nulo quando o projeto não está na Biblioteca. A aba Jogar guarda capturas e notas por jogo.</summary>
    internal LibraryGame? GameOfProject(string projectRoot)
    {
        var normalized = GameLibrary.NormalizeFolder(projectRoot);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return games.Find(g => g.ProjectPath is { } path && string.Equals(path, normalized, comparison));
    }
}
