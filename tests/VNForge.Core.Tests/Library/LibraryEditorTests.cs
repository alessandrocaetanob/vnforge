using System.Diagnostics;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Vndb;
using static VNForge.Core.Tests.Library.SyntheticGames;

namespace VNForge.Core.Tests.Library;

/// <summary>As edições do jogo da Biblioteca (confirmar, procurar de novo) sob a trava do <c>library.json</c>.</summary>
public sealed class LibraryEditorTests : IDisposable
{
    private readonly TempFolder _games = new();
    private readonly TempFolder _config = new();

    public void Dispose()
    {
        _games.Dispose();
        _config.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LibraryFile File => new(_config.Path);

    private LibraryEditor Editor() => new(File, () => new LibraryMatcher(new FakeVndbCatalog([Vn("v4", "CLANNAD", "クラナド")])));

    private async Task<string> ScannedGameIdAsync()
    {
        Collection(_games.Path);
        await new LibraryScanService(File, Scanner(), null, () => []).RunAsync(new LibraryRoot(_games.Path, MatchVndb: false), cancellationToken: Ct);
        return File.Load().Games.First(g => g.Engine.Source != LibraryEngineSource.Unknown).Id;
    }

    private FileStream HoldLock() => new(File.Path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    [Fact]
    public async Task Cancelar_chega_a_espera_da_trava_ao_confirmar()
    {
        var id = await ScannedGameIdAsync();
        using var held = HoldLock();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Editor().ConfirmAsync(id, "v4", cts.Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"A espera ignorou o cancelamento: {watch.Elapsed}.");
    }

    [Fact]
    public async Task Cancelar_chega_a_espera_da_trava_ao_procurar_de_novo()
    {
        var id = await ScannedGameIdAsync();
        using var held = HoldLock();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Editor().SearchAgainAsync(id, cts.Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"A espera ignorou o cancelamento: {watch.Elapsed}.");
    }
}
