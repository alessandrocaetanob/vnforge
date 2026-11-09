using VNForge.Core.Engines;

namespace VNForge.Core.Tests.Images;

/// <summary>
/// Formato de pacote falso para os testes do inventário: o conteúdo de cada pacote vem de um dicionário indexado pelo nome do arquivo
/// (<c>.pak</c> em disco, vazio), e um arquivo que não está nele é um pacote inválido.
/// </summary>
internal sealed class FakeArchiveFormat(IReadOnlyDictionary<string, IReadOnlyDictionary<string, byte[]>> archives) : IArchiveFormat
{
    public string Id => "fake";

    public IReadOnlyList<string> FileExtensions { get; } = [".pak"];

    public int Opened { get; private set; }

    public bool CanOpen(Stream stream) => true;

    public IArchive Open(Stream stream)
    {
        var name = stream is FileStream file ? Path.GetFileName(file.Name) : string.Empty;
        if (!archives.TryGetValue(name, out var contents))
        {
            throw new InvalidFileException(name, "pacote de teste inválido.");
        }

        Opened++;
        stream.Dispose();
        return new FakeArchive(contents);
    }

    /// <summary>Cria em <paramref name="directory"/> os arquivos <c>.pak</c> (vazios) que o formato falso reconhece, nos caminhos relativos dados.</summary>
    public static void Touch(string directory, params string[] relativePaths)
    {
        foreach (var relative in relativePaths)
        {
            var path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [0]);
        }
    }

    private sealed class FakeArchive(IReadOnlyDictionary<string, byte[]> contents) : IArchive
    {
        public IReadOnlyList<ArchiveEntry> Entries { get; } = [.. contents.Select(e => new ArchiveEntry(e.Key, e.Value.Length, false))];

        public Stream OpenEntry(ArchiveEntry entry) => new MemoryStream(contents[entry.Path], writable: false);

        public void WriteTo(Stream output, IReadOnlyDictionary<string, byte[]> replacements) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
