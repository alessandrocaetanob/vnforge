using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

[Trait("Risco", "R7")]
public sealed class LibraryScannerTests : IDisposable
{
    private readonly TempFolder _root = new();

    public void Dispose() => _root.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LibraryScanResult ScanCollection(LibraryScanOptions? options = null, IGameWindowTitleReader? titles = null)
    {
        SyntheticGames.Collection(_root.Path);
        return SyntheticGames.Scanner(titles).Scan(_root.Path, options, cancellationToken: Ct);
    }

    [Fact]
    public void Pasta_que_nao_existe_e_erro_esperado()
    {
        var e = Assert.Throws<LibraryException>(() => SyntheticGames.Scanner().Scan(Path.Combine(_root.Path, "nada"), cancellationToken: Ct));

        Assert.Contains("não existe", e.Message);
    }

    private string[] Snapshot() =>
        [.. Directory.EnumerateFileSystemEntries(_root.Path, "*", SearchOption.AllDirectories)
            .Select(p => p + "|" + (File.Exists(p) ? new FileInfo(p).Length : -1) + "|" + File.GetLastWriteTimeUtc(p).Ticks)
            .Order(StringComparer.Ordinal)];

    private sealed class Collector(List<LibraryScanProgress> reports, Action<LibraryScanProgress>? also = null) : IProgress<LibraryScanProgress>
    {
        public void Report(LibraryScanProgress value)
        {
            reports.Add(value);
            also?.Invoke(value);
        }
    }
}
