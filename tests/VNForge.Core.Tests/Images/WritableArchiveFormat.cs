using System.Text.Json;
using VNForge.Core.Engines;

namespace VNForge.Core.Tests.Images;

/// <summary>Pacote de teste que sabe se regravar: o arquivo é um JSON com o nome e os bytes de cada entrada, na ordem.</summary>
internal sealed class WritableArchiveFormat : IArchiveFormat
{
    public string Id => "fake";

    public IReadOnlyList<string> FileExtensions { get; } = [".pak"];

    public bool CanOpen(Stream stream) => true;

    public IArchive Open(Stream stream)
    {
        using (stream)
        {
            var content = JsonSerializer.Deserialize<List<KeyValuePair<string, byte[]>>>(stream)!;
            return new Archive(content);
        }
    }

    public static void Write(string path, IReadOnlyDictionary<string, byte[]> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(entries.ToList()));
    }

    public static Dictionary<string, byte[]> Read(string path) =>
        JsonSerializer.Deserialize<List<KeyValuePair<string, byte[]>>>(File.ReadAllBytes(path))!.ToDictionary(e => e.Key, e => e.Value);

    private sealed class Archive(List<KeyValuePair<string, byte[]>> content) : IArchive
    {
        public IReadOnlyList<ArchiveEntry> Entries { get; } = [.. content.Select(e => new ArchiveEntry(e.Key, e.Value.Length, false))];

        public Stream OpenEntry(ArchiveEntry entry) => new MemoryStream(content.First(e => e.Key == entry.Path).Value);

        public void WriteTo(Stream output, IReadOnlyDictionary<string, byte[]> replacements)
        {
            var result = content.Select(e => replacements.TryGetValue(e.Key, out var replacement) ? KeyValuePair.Create(e.Key, replacement) : e).ToList();
            output.Write(JsonSerializer.SerializeToUtf8Bytes(result));
        }

        public void Dispose()
        {
        }
    }
}
