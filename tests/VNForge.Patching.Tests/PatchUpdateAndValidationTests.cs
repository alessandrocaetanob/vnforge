namespace VNForge.Patching.Tests;

public class PatchUpdateAndValidationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Instala a 1.1.0 e deixa o build pronto para uma 1.2.0 que muda o Game.exe e o script (nesta ordem de aplicação).</summary>
    private static void InstallFirstVersionAndChangeBuild(SyntheticGame game)
    {
        using (var first = PatchPackage.Open(game.BuildPackage(SyntheticGame.Info("1.1.0"))))
        {
            PatchInstaller.Install(game.Game, first, null, Ct);
        }

        var script = File.ReadAllBytes(Path.Combine(game.Build, "Game/script.ypf"));
        script[700] ^= 0x33;
        File.WriteAllBytes(Path.Combine(game.Build, "Game/script.ypf"), script);
        var exe = File.ReadAllBytes(Path.Combine(game.Build, "Game/Game.exe"));
        exe[900] ^= 0x44;
        File.WriteAllBytes(Path.Combine(game.Build, "Game/Game.exe"), exe);
    }

    private static PatchPackage SecondVersionWithBrokenScriptDelta(SyntheticGame game)
    {
        var bytes = game.BuildPackage(SyntheticGame.Info("1.2.0", "1.1.0")).ToArray();
        using (var intact = PatchPackage.Open(new MemoryStream(bytes)))
        {
            var entry = intact.Manifest.Files.Single(f => f.Path == "Game/script.ypf");
            bytes[intact.PayloadStart + entry.Offset + entry.Length / 2] ^= 0xFF;
        }

        return PatchPackage.Open(new MemoryStream(bytes));
    }

    [Fact]
    public void Atualizacao_que_falha_no_segundo_arquivo_volta_para_a_versao_anterior_e_nao_para_os_originais()
    {
        using var game = new SyntheticGame();
        InstallFirstVersionAndChangeBuild(game);
        var installedExe = SyntheticGame.Sha(Path.Combine(game.Game, "Game/Game.exe"));
        var installedScript = SyntheticGame.Sha(Path.Combine(game.Game, "Game/script.ypf"));
        var installedCreated = SyntheticGame.Sha(Path.Combine(game.Game, SyntheticGame.CreatedFile));
        using var second = SecondVersionWithBrokenScriptDelta(game);

        var error = Assert.Throws<PatchException>(() => PatchInstaller.Install(game.Game, second, null, Ct));

        Assert.Contains("estado de antes desta instalação", error.Message);
        Assert.DoesNotContain("ao que eram", error.Message);
        Assert.Equal(installedExe, SyntheticGame.Sha(Path.Combine(game.Game, "Game/Game.exe")));
        Assert.Equal(installedScript, SyntheticGame.Sha(Path.Combine(game.Game, "Game/script.ypf")));
        Assert.Equal(installedCreated, SyntheticGame.Sha(Path.Combine(game.Game, SyntheticGame.CreatedFile)));
        Assert.Equal("1.1.0", PatchInstaller.InstalledVersion(game.Game));
        Assert.False(Directory.Exists(Path.Combine(game.Game, PatchInstaller.BackupFolderName, ".anterior")));

        PatchInstaller.Uninstall(game.Game, null, Ct);
        SyntheticGame.AssertSameTree(game.Original, game.Game, PatchInstaller.BackupFolderName);
    }

    [Fact]
    public void Primeira_instalacao_que_falha_deixa_o_manifesto_sem_instalacao_e_o_jogo_original()
    {
        using var game = new SyntheticGame();
        var bytes = game.BuildPackage().ToArray();
        using (var intact = PatchPackage.Open(new MemoryStream(bytes)))
        {
            var entry = intact.Manifest.Files.Single(f => f.Path == "Game/script.ypf");
            bytes[intact.PayloadStart + entry.Offset + entry.Length / 2] ^= 0xFF;
        }

        using var package = PatchPackage.Open(new MemoryStream(bytes));

        Assert.Throws<PatchException>(() => PatchInstaller.Install(game.Game, package, null, Ct));

        Assert.Null(PatchInstaller.InstalledVersion(game.Game));
        SyntheticGame.AssertSameTree(game.Original, game.Game, PatchInstaller.BackupFolderName);
    }

    [Fact]
    public void Rollback_que_nao_consegue_devolver_um_arquivo_diz_qual()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var game = new SyntheticGame();
        InstallFirstVersionAndChangeBuild(game);
        using var second = SecondVersionWithBrokenScriptDelta(game);
        FileStream? lockedExe = null;
        try
        {
            var hook = new Hook(p =>
            {
                if (p.Phase == PatchPhase.Apply && p.Path == "Game/script.ypf")
                {
                    lockedExe = new FileStream(Path.Combine(game.Game, "Game/Game.exe"), FileMode.Open, FileAccess.Read, FileShare.None);
                }
            });

            var error = Assert.Throws<PatchException>(() => PatchInstaller.Install(game.Game, second, hook, Ct));

            Assert.Contains("Não consegui devolver", error.Message);
            Assert.Contains("Game/Game.exe", error.Message);
        }
        finally
        {
            lockedExe?.Dispose();
        }
    }

    public static TheoryData<string, string[]> InvalidPaths() => new()
    {
        { "pasta reservada", ["_originais/manifest.json"] },
        { "pasta reservada em outra caixa", ["_ORIGINAIS/x.bin"] },
        { "temporário do instalador", ["Game/a.ypf.vnf-tmp"] },
        { "duplicata exata", ["Game/a.bin", "Game/a.bin"] },
        { "duplicata só na caixa", ["Game/a.bin", "game/A.BIN"] },
        { "barra invertida", ["Game\\a.bin"] },
        { "segmento vazio", ["Game//a.bin"] },
        { "sai da pasta", ["../fora.bin"] },
        { "absoluto", ["/etc/x"] },
    };

    [Theory]
    [MemberData(nameof(InvalidPaths))]
    public void Manifesto_com_caminho_invalido_e_recusado_com_PatchException(string motivo, string[] paths)
    {
        Assert.NotNull(motivo);

        var package = ManualPackage(paths.Select((p, i) => Entry(p, offset: 0, length: 4)).ToList(), payloadBytes: 8);

        Assert.Throws<PatchException>(() => PatchPackage.Open(package));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(6, 4)]
    [InlineData(100, 1)]
    public void Delta_que_passa_do_fim_do_pacote_e_recusado(long offset, long length)
    {
        var package = ManualPackage([Entry("Game/a.bin", offset, length)], payloadBytes: 8);

        Assert.Throws<PatchException>(() => PatchPackage.Open(package));
    }

    [Fact]
    public void Manifesto_valido_com_o_delta_dentro_do_pacote_abre()
    {
        using var package = PatchPackage.Open(ManualPackage([Entry("Game/a.bin", 0, 8)], payloadBytes: 8));

        Assert.Single(package.Manifest.Files);
    }

    [Fact]
    [Trait("Category", "Lento")]
    public void Patch_de_um_arquivo_de_64_MB_nao_carrega_o_delta_na_memoria()
    {
        using var game = new SyntheticGame();
        const int size = 64 * 1024 * 1024;
        var original = Path.Combine(game.Root, "grande-original.ypf");
        var result = Path.Combine(game.Root, "grande-build.ypf");
        var data = SyntheticGame.Random(11, size);
        File.WriteAllBytes(original, data);
        for (var i = 0; i < 6 * 1024 * 1024; i++)
        {
            data[20_000_000 + i] ^= 0x5A;
        }

        File.WriteAllBytes(result, data);
        using var package = new MemoryStream();

        var before = GC.GetAllocatedBytesForCurrentThread();
        PatchBuilder.Build(SyntheticGame.Info(), [new PatchSource("Game/grande.ypf", original, result)], package, cancellationToken: Ct);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // O delta tem ~6 MB (a região trocada é aleatória). O VCDiff lê o original inteiro (64 MB); o resto não pode somar outro arquivo inteiro por delta.
        Assert.True(allocated < 3L * size, $"alocou {allocated / 1048576} MB para um arquivo de {size / 1048576} MB");
        using var opened = PatchPackage.Open(new MemoryStream(package.ToArray()));
        Assert.True(opened.Manifest.Files[0].Length < 8 * 1024 * 1024);
    }

    private static PatchFileEntry Entry(string path, long offset, long length) =>
        new(path, new string('a', 64), 10, new string('b', 64), 10, offset, length);

    private static MemoryStream ManualPackage(List<PatchFileEntry> files, int payloadBytes)
    {
        var manifest = new PatchManifest("Jogo", "1.0.0", null, null, null, files);
        var stream = new MemoryStream();
        PatchPackage.Write(stream, manifest, new MemoryStream(new byte[payloadBytes]));
        stream.Position = 0;
        return stream;
    }

    private sealed class Hook(Action<PatchProgress> onReport) : IProgress<PatchProgress>
    {
        public void Report(PatchProgress value) => onReport(value);
    }
}
