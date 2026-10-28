using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Services;

/// <summary>
/// Guarda o que o jogo desenha em volta da fala com falante de cada projeto (<see cref="SpeakerBoxMarks"/>, que vem da estratégia de acentos e do
/// <c>style.speakerDelimiter</c> do <c>vnforge.json</c>) e só relê o arquivo quando ele muda, como o <see cref="FormattingProfileCache"/>.
/// </summary>
public sealed class SpeakerMarksCache
{
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock gate = new();

    /// <summary>As marcas do projeto; <see cref="SpeakerBoxMarks.None"/> sem arquivo legível ou com uma estratégia que não as tem.</summary>
    public SpeakerBoxMarks Get(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var info = new FileInfo(Path.Combine(root, ProjectFolder.ConfigFileName));
        var stamp = info.Exists ? (info.LastWriteTimeUtc, info.Length) : (default(DateTime), -1L);
        lock (gate)
        {
            if (entries.TryGetValue(root, out var entry) && entry.Stamp == stamp)
            {
                return entry.Marks;
            }

            var marks = Load(root);
            entries[root] = new Entry(stamp, marks);
            return marks;
        }
    }

    private static SpeakerBoxMarks Load(string root)
    {
        try
        {
            var config = ProjectFolder.Open(root).LoadConfig();
            return SpeakerBoxMarks.For(config.Encoding.Strategy, config.Style.SpeakerDelimiter);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return SpeakerBoxMarks.None;
        }
    }

    private sealed record Entry((DateTime LastWrite, long Length) Stamp, SpeakerBoxMarks Marks);
}
