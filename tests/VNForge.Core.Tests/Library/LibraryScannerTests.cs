using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

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
    public void Titulo_da_janela_vem_do_leitor_e_do_System_json_do_RPG_Maker()
    {
        var withReader = ScanCollection(titles: new SyntheticGames.FixedTitles("Janela do Jogo"));
        var rpg = ScanCollection(titles: new GameWindowTitleReader()).Games.Single(g => g.Engine.Id == "rpgmaker");

        Assert.All(withReader.Games, g => Assert.Equal("Janela do Jogo", g.WindowTitle));
        Assert.Equal("Moonlit Tale", rpg.WindowTitle);
    }

    [Fact]
    public void Pasta_que_nao_existe_e_erro_esperado()
    {
        var e = Assert.Throws<LibraryException>(() => SyntheticGames.Scanner().Scan(Path.Combine(_root.Path, "nada"), cancellationToken: Ct));

        Assert.Contains("não existe", e.Message);
    }

    [Fact]
    public void Cancelar_para_no_meio_e_devolve_o_que_achou()
    {
        for (var i = 0; i < 20; i++)
        {
            SyntheticGames.File(Path.Combine(_root.Path, $"Jogo {i:00}", "data.xp3"));
        }

        using var source = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var result = SyntheticGames.Scanner().Scan(
            _root.Path,
            progress: new Collector([], r =>
            {
                if (r.Games == 3)
                {
                    source.Cancel();
                }
            }),
            cancellationToken: source.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(3, result.Games.Count);
    }

    [Fact]
    public void Pasta_sem_permissao_de_leitura_e_pulada_sem_derrubar_a_varredura()
    {
        var open = Path.Combine(_root.Path, "Aberta", "Jogo Aberto");
        var denied = Path.Combine(_root.Path, "Negada");
        SyntheticGames.File(Path.Combine(open, "data.xp3"));
        SyntheticGames.File(Path.Combine(denied, "Jogo Escondido", "data.xp3"));
        using var deny = DeniedAccess.Create(denied);
        if (deny is null)
        {
            Assert.Skip("Não foi possível negar a leitura da pasta neste ambiente (processo privilegiado?).");
        }

        var result = SyntheticGames.Scanner().Scan(_root.Path, cancellationToken: Ct);

        Assert.Equal(["Jogo Aberto"], result.Games.Select(GameLibrary.FolderName));
        Assert.False(result.Cancelled);
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
