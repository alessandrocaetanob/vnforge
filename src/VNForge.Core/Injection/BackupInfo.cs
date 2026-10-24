namespace VNForge.Core.Injection;

/// <summary>Um backup de <c>backup/&lt;data&gt;/</c> como a lista de backups o mostra: quando foi feito, quanto guarda e o que a injeção mexeu.</summary>
/// <param name="Name">Nome da pasta (<c>2026-10-22_2316</c>).</param>
/// <param name="Directory">Caminho completo da pasta do backup.</param>
/// <param name="Manifest">O manifesto lido; nulo quando ilegível (veja <paramref name="Problem"/>).</param>
/// <param name="SizeBytes">Soma dos arquivos copiados para a pasta, sem o manifesto: o que o backup ocupa em disco.</param>
/// <param name="Problem">Por que o manifesto não foi lido, em português; nulo quando foi.</param>
public sealed record BackupInfo(string Name, string Directory, BackupManifest? Manifest, long SizeBytes, string? Problem)
{
    /// <summary>Uma restauração já devolveu os arquivos deste backup.</summary>
    public bool Restored => Manifest?.RestoredAt is not null;

    /// <summary>Caminhos (relativos ao jogo) dos arquivos que existiam antes da injeção e foram copiados para o backup.</summary>
    public IReadOnlyList<string> OriginalFiles => [.. Manifest?.Files.Where(f => f.Existed).Select(f => f.Path) ?? []];

    /// <summary>Caminhos dos arquivos que a injeção criou: não há original deles no backup.</summary>
    public IReadOnlyList<string> CreatedFiles => [.. Manifest?.Files.Where(f => !f.Existed).Select(f => f.Path) ?? []];
}
