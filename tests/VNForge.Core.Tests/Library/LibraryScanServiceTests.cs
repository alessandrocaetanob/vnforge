using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Vndb;
using VNForge.Vndb;
using static VNForge.Core.Tests.Library.SyntheticGames;

namespace VNForge.Core.Tests.Library;

/// <summary>A varredura da Biblioteca como a tela a faz: varre, junta, liga os projetos, casa com o VNDB e guarda a pasta com o que a pessoa escolheu.</summary>
[Trait("Risco", "R7")]
public sealed class LibraryScanServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _games = new();
    private readonly TempFolder _config = new();

    public void Dispose()
    {
        _games.Dispose();
        _config.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LibraryFile File => new(_config.Path);

    private static FakeVndbCatalog Catalog() => new(
    [
        Vn("v4", "CLANNAD", "クラナド", image: new VndbImage("https://t.vndb.org/cv/4.jpg", null, 300, 400, 0, 0)),
        Vn("v17", "Steins;Gate", "STEINS;GATE"),
    ]);

    private LibraryScanService Service(IVndbCatalog? catalog, params string[] projects) =>
        new(File, Scanner(), catalog is null ? null : () => new LibraryMatcher(catalog, new FixedTime()), () => projects, new FixedTime());

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Sem_subpastas_olha_so_a_pasta_apontada()
    {
        Collection(_games.Path);

        var outcome = await Service(null).RunAsync(new LibraryRoot(_games.Path, IncludeSubfolders: false, MatchVndb: false), cancellationToken: Ct);

        Assert.DoesNotContain(outcome.Scan.Games, g => g.Engine.Source != LibraryEngineSource.Unknown);
    }

    private sealed class Collector<T>(List<T> items, Action<T>? also = null) : IProgress<T>
    {
        public void Report(T value)
        {
            items.Add(value);
            also?.Invoke(value);
        }
    }
}
