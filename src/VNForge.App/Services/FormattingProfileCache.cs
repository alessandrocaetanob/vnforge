using VNForge.Core;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;

namespace VNForge.App.Services;

/// <summary>
/// Guarda o <see cref="FormattingProfile"/> de cada projeto e só o lê de novo quando o <c>formatting.json</c> muda (data de gravação ou tamanho).
/// Trocar de linha na grade custa um <c>stat</c>, não uma leitura e um parse do arquivo.
/// </summary>
public sealed class FormattingProfileCache
{
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock gate = new();

    /// <summary>O perfil atual do projeto; <see cref="FormattingProfile.Default"/> sem arquivo, nulo quando o projeto ou o arquivo não abrem.</summary>
    public FormattingProfile? Get(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var info = new FileInfo(Path.Combine(root, ProjectFolder.FormattingFileName));
        var stamp = info.Exists ? (info.LastWriteTimeUtc, info.Length) : (default(DateTime), -1L);
        lock (gate)
        {
            if (entries.TryGetValue(root, out var entry) && entry.Stamp == stamp)
            {
                return entry.Profile;
            }

            var profile = Load(root);
            entries[root] = new Entry(stamp, profile);
            return profile;
        }
    }

    private static FormattingProfile? Load(string root)
    {
        try
        {
            return ProjectFolder.Open(root).LoadFormatting();
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record Entry((DateTime LastWrite, long Length) Stamp, FormattingProfile? Profile);
}
