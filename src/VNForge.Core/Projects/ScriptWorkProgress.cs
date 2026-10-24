namespace VNForge.Core.Projects;

/// <summary>Andamento da leitura (<see cref="ProjectExtractor.Extract"/>) ou da reconstrução (<see cref="ProjectBuilder.Build"/>) dos scripts.</summary>
/// <param name="Done">Scripts já tratados.</param>
/// <param name="Total">Scripts do jogo descompactado.</param>
/// <param name="Name">Nome do script em andamento (<c>yst00012.ybn</c>); vazio no relato final.</param>
public sealed record ScriptWorkProgress(int Done, int Total, string Name);
