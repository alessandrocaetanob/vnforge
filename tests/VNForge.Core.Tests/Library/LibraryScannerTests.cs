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
    public void Reconhece_cada_jogo_com_a_engine_certa_e_ignora_o_que_nao_e_jogo()
    {
        var result = ScanCollection();

        var byFolder = result.Games.ToDictionary(g => GameLibrary.FolderName(g), g => g.Engine);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["[Key] CLANNAD v1.02"] = "kirikiri",
                ["Steins;Gate"] = "siglus",
                ["Eden Fall"] = "renpy",
                ["Tale"] = "rpgmaker",
                ["Sakura no Uta"] = "marker",
            },
            byFolder.ToDictionary(p => p.Key, p => p.Value.Id));
        Assert.False(result.Cancelled);
    }

    [Fact]
    public void Engine_registrada_vale_pelo_Detect_e_a_de_assinatura_vale_so_como_reconhecida()
    {
        var games = ScanCollection().Games;

        var registered = games.Single(g => g.Engine.Id == "marker").Engine;
        var signature = games.Single(g => g.Engine.Id == "kirikiri").Engine;
        Assert.Equal(LibraryEngineSource.Engine, registered.Source);
        Assert.True(registered.Translatable);
        Assert.Equal("Marcador", registered.Name);
        Assert.Equal("marcador reconhecido", registered.Summary);
        Assert.Equal(LibraryEngineSource.Signature, signature.Source);
        Assert.False(signature.Translatable);
    }

    [Fact]
    public void Nao_desce_dentro_de_jogo_e_pula_pastas_de_sistema()
    {
        var folders = ScanCollection().Games.Select(g => g.Folder).ToList();

        Assert.DoesNotContain(folders, f => f.Contains("Windows", StringComparison.Ordinal));
        Assert.DoesNotContain(folders, f => f.Contains("node_modules", StringComparison.Ordinal));
        Assert.DoesNotContain(folders, f => f.Contains("RECYCLE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(folders, f => f.Contains("renpy", StringComparison.Ordinal));
        Assert.Equal(5, folders.Count);
    }

    [Fact]
    public void Escolhe_o_executavel_sem_o_desinstalador_e_monta_titulo_e_id_estaveis()
    {
        var clannad = ScanCollection().Games.Single(g => g.Engine.Id == "kirikiri");

        Assert.Equal("CLANNAD.exe", clannad.Exe);
        Assert.Equal("CLANNAD", clannad.Title);
        Assert.Equal(GameLibrary.IdFor(clannad.Folder), clannad.Id);
        Assert.Equal(9, clannad.Id.Length);
        Assert.Equal(clannad.Id, SyntheticGames.Scanner().Scan(_root.Path, cancellationToken: Ct).Games.Single(g => g.Engine.Id == "kirikiri").Id);
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
    public void Pasta_com_executavel_sem_engine_so_entra_quando_pedido()
    {
        var common = ScanCollection();
        var all = SyntheticGames.Scanner().Scan(_root.Path, new LibraryScanOptions(IncludeUnknown: true), cancellationToken: Ct);

        Assert.DoesNotContain(common.Games, g => g.Engine.Id == LibraryEngine.UnknownId);
        var unknown = all.Games.Where(g => g.Engine.Id == LibraryEngine.UnknownId).ToList();
        Assert.Contains(unknown, g => GameLibrary.FolderName(g) == "Ferramenta");
        Assert.All(unknown, g => Assert.Equal(LibraryEngineSource.Unknown, g.Engine.Source));
    }

    [Fact]
    public void A_profundidade_maxima_limita_a_descida()
    {
        SyntheticGames.Collection(_root.Path);

        var shallow = SyntheticGames.Scanner().Scan(_root.Path, new LibraryScanOptions(MaxDepth: 1), cancellationToken: Ct);

        Assert.DoesNotContain(shallow.Games, g => GameLibrary.FolderName(g) == "Eden Fall");
        Assert.Contains(shallow.Games, g => GameLibrary.FolderName(g) == "Steins;Gate");
    }

    [Fact]
    public void A_raiz_pode_ser_o_proprio_jogo()
    {
        var game = Path.Combine(_root.Path, "Solo");
        SyntheticGames.File(Path.Combine(game, "Scene.pck"));

        var result = SyntheticGames.Scanner().Scan(game, cancellationToken: Ct);

        Assert.Equal("siglus", Assert.Single(result.Games).Engine.Id);
    }

    [Fact]
    public void Pasta_que_nao_existe_e_erro_esperado()
    {
        var e = Assert.Throws<LibraryException>(() => SyntheticGames.Scanner().Scan(Path.Combine(_root.Path, "nada"), cancellationToken: Ct));

        Assert.Contains("não existe", e.Message);
    }

    [Fact]
    public void Aguenta_centenas_de_jogos_e_reporta_o_progresso()
    {
        for (var i = 0; i < 300; i++)
        {
            SyntheticGames.File(Path.Combine(_root.Path, $"grupo{i % 10}", $"Jogo {i:000}", "data.xp3"));
        }

        var reports = new List<LibraryScanProgress>();
        var result = SyntheticGames.Scanner().Scan(_root.Path, progress: new Collector(reports), cancellationToken: Ct);

        Assert.Equal(300, result.Games.Count);
        Assert.Equal(300, result.Games.Select(g => g.Id).Distinct().Count());
        Assert.Equal(result.Directories, reports.Count);
        Assert.Equal(300, reports[^1].Games);
        Assert.True(reports.Select(r => r.Directories).SequenceEqual(Enumerable.Range(1, reports.Count)));
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
    public void Varrer_nao_cria_nem_muda_arquivo_na_pasta()
    {
        SyntheticGames.Collection(_root.Path);
        var before = Snapshot();

        SyntheticGames.Scanner(new GameWindowTitleReader()).Scan(_root.Path, new LibraryScanOptions(IncludeUnknown: true), cancellationToken: Ct);

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Varre_Program_Files_onde_os_instaladores_gravam_e_pula_Windows_ProgramData_e_a_lixeira()
    {
        SyntheticGames.File(Path.Combine(_root.Path, "Program Files", "Marca", "Jogo A", "data.xp3"));
        SyntheticGames.File(Path.Combine(_root.Path, "Program Files (x86)", "Jogo B", "Scene.pck"));
        SyntheticGames.File(Path.Combine(_root.Path, "Windows", "Jogo C", "data.xp3"));
        SyntheticGames.File(Path.Combine(_root.Path, "ProgramData", "Jogo D", "data.xp3"));
        SyntheticGames.File(Path.Combine(_root.Path, "$Recycle.Bin", "Jogo E", "data.xp3"));
        SyntheticGames.File(Path.Combine(_root.Path, "System Volume Information", "Jogo F", "data.xp3"));

        var names = SyntheticGames.Scanner().Scan(_root.Path, cancellationToken: Ct).Games.Select(GameLibrary.FolderName).Order(StringComparer.Ordinal);

        Assert.Equal(["Jogo A", "Jogo B"], names);
        Assert.False(LibraryScanner.IsSystemFolder("Program Files"));
        Assert.False(LibraryScanner.IsSystemFolder("Program Files (x86)"));
        Assert.True(LibraryScanner.IsSystemFolder("ProgramData"));
    }

    [Fact]
    public void Nao_segue_link_simbolico_de_pasta_nem_gira_em_laco()
    {
        var outside = Path.Combine(_root.Path, "fora", "Jogo Fora");
        SyntheticGames.File(Path.Combine(outside, "data.xp3"));
        var scanned = Path.Combine(_root.Path, "colecao");
        SyntheticGames.File(Path.Combine(scanned, "Jogo Dentro", "data.xp3"));
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(scanned, "atalho"), Path.Combine(_root.Path, "fora"));
            Directory.CreateSymbolicLink(Path.Combine(scanned, "Jogo Dentro", "laco"), scanned);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip($"Sem privilégio para criar link simbólico neste ambiente: {e.Message}");
        }

        var result = SyntheticGames.Scanner().Scan(scanned, cancellationToken: Ct);

        Assert.Equal(["Jogo Dentro"], result.Games.Select(GameLibrary.FolderName));
    }

    [Fact]
    public void Nao_segue_junction_do_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Junction só existe no Windows.");
        }

        var target = Path.Combine(_root.Path, "fora", "Jogo Fora");
        SyntheticGames.File(Path.Combine(target, "data.xp3"));
        var scanned = Path.Combine(_root.Path, "colecao");
        SyntheticGames.File(Path.Combine(scanned, "Jogo Dentro", "data.xp3"));
        var junction = Path.Combine(scanned, "juncao");
        var started = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{Path.Combine(_root.Path, "fora")}\"")
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using (var process = System.Diagnostics.Process.Start(started)!)
        {
            process.WaitForExit();
            if (process.ExitCode != 0 || !Directory.Exists(junction))
            {
                Assert.Skip("Não foi possível criar a junction neste ambiente.");
            }
        }

        try
        {
            var result = SyntheticGames.Scanner().Scan(scanned, cancellationToken: Ct);

            Assert.Equal(["Jogo Dentro"], result.Games.Select(GameLibrary.FolderName));
        }
        finally
        {
            // Só a junction (não recursivo): a limpeza recursiva da pasta temporária não atravessa uma junction.
            Directory.Delete(junction, recursive: false);
        }
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
