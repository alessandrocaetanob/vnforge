namespace VNForge.Vndb.Tests;

/// <summary>Uma pasta temporária que some no fim do teste. Ela só é criada quando o código a cria: o caminho nasce livre.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vnforge-vndb-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
