namespace VNForge.Patching.Tests;

public class PatchRoundTripTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Patch_aplicado_sobre_copia_limpa_gera_arquivos_identicos_ao_build_e_desinstalar_devolve_os_originais()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());

        var result = PatchInstaller.Install(game.Game, package, null, Ct);

        Assert.Equal(["Game/Game.exe", "Game/extra/fonte.dat", "Game/script.ypf"], result.Written.Order(StringComparer.Ordinal));
        foreach (var relative in new[] { "Game/script.ypf", "Game/Game.exe", SyntheticGame.CreatedFile })
        {
            Assert.Equal(SyntheticGame.Sha(Path.Combine(game.Build, relative)), SyntheticGame.Sha(Path.Combine(game.Game, relative)));
        }

        var removed = PatchInstaller.Uninstall(game.Game, null, Ct);

        Assert.Empty(removed.Kept);
        SyntheticGame.AssertSameTree(game.Original, game.Game, PatchInstaller.BackupFolderName);
    }

    [Fact]
    public void O_patch_so_leva_a_diferenca_e_nunca_um_arquivo_inteiro_do_jogo()
    {
        using var game = new SyntheticGame();
        using var stream = game.BuildPackage();
        using var package = PatchPackage.Open(stream);

        var scriptEntry = package.Manifest.Files.Single(f => f.Path == "Game/script.ypf");

        Assert.True(scriptEntry.Length < 20_000, $"o delta do script tem {scriptEntry.Length} bytes");
        Assert.True(stream.Length < SyntheticGame.ScriptSize / 50, $"o pacote tem {stream.Length} bytes");
        Assert.DoesNotContain(package.Manifest.Files, f => f.Path == "Game/system.ypf");
        Assert.DoesNotContain(package.Manifest.Files, f => f.Path == "Game/leiame.txt");
        Assert.Equal(SyntheticGame.ScriptSize, scriptEntry.OriginalSize);
    }

    [Fact]
    public void Gerar_o_patch_duas_vezes_da_os_mesmos_bytes()
    {
        using var game = new SyntheticGame();

        var first = game.BuildPackage().ToArray();
        var second = game.BuildPackage().ToArray();

        Assert.True(first.AsSpan().SequenceEqual(second));
    }

    [Fact]
    public void Jogo_em_outra_versao_nao_recebe_nada_e_a_mensagem_diz_qual_arquivo()
    {
        using var game = new SyntheticGame();
        File.WriteAllBytes(Path.Combine(game.Game, "Game/Game.exe"), SyntheticGame.Random(77, 200_000));
        using var package = PatchPackage.Open(game.BuildPackage());

        var check = PatchInstaller.Check(game.Game, package);
        var error = Assert.Throws<PatchException>(() => PatchInstaller.Install(game.Game, package, null, Ct));

        Assert.Equal(PatchCheckState.WrongVersion, check.State);
        Assert.Contains("Game/Game.exe", error.Message);
        Assert.False(Directory.Exists(Path.Combine(game.Game, PatchInstaller.BackupFolderName)));
        Assert.Equal(SyntheticGame.Sha(Path.Combine(game.Original, "Game/script.ypf")), SyntheticGame.Sha(Path.Combine(game.Game, "Game/script.ypf")));
        Assert.False(File.Exists(Path.Combine(game.Game, SyntheticGame.CreatedFile)));
    }

    [Fact]
    public void Os_originais_vao_para_o_backup_antes_de_qualquer_escrita_e_conferem_byte_a_byte()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var seenBackupBeforeWrite = false;

        PatchInstaller.Install(game.Game, package, new Progress(phase =>
        {
            if (phase == PatchPhase.Apply)
            {
                var backup = Path.Combine(game.Game, PatchInstaller.BackupFolderName, "Game/script.ypf");
                seenBackupBeforeWrite = File.Exists(backup)
                    && SyntheticGame.Sha(backup) == SyntheticGame.Sha(Path.Combine(game.Original, "Game/script.ypf"))
                    && SyntheticGame.Sha(Path.Combine(game.Game, "Game/script.ypf")) == SyntheticGame.Sha(Path.Combine(game.Original, "Game/script.ypf"));
            }
        }), Ct);

        Assert.True(seenBackupBeforeWrite);
    }

    [Fact]
    public void Delta_corrompido_para_antes_de_trocar_o_arquivo_e_o_jogo_fica_como_estava()
    {
        using var game = new SyntheticGame();
        var bytes = game.BuildPackage().ToArray();
        using (var intact = PatchPackage.Open(new MemoryStream(bytes)))
        {
            var entry = intact.Manifest.Files.Single(f => f.Path == "Game/Game.exe");
            bytes[intact.PayloadStart + entry.Offset + entry.Length / 2] ^= 0xFF;
        }

        using var package = PatchPackage.Open(new MemoryStream(bytes));

        Assert.ThrowsAny<PatchException>(() => PatchInstaller.Install(game.Game, package, null, Ct));

        SyntheticGame.AssertSameTree(game.Original, game.Game, PatchInstaller.BackupFolderName);
        Assert.DoesNotContain(Directory.EnumerateFiles(game.Game, "*", SearchOption.AllDirectories), f => f.EndsWith(".vnf-tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void Instalar_de_novo_diz_que_ja_esta_instalada_e_nao_mexe_em_nada()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        PatchInstaller.Install(game.Game, package, null, Ct);

        var check = PatchInstaller.Check(game.Game, package);
        var again = PatchInstaller.Install(game.Game, package, null, Ct);

        Assert.Equal(PatchCheckState.AlreadyInstalled, check.State);
        Assert.Empty(again.Written);
    }

    [Fact]
    public void Patch_novo_instala_por_cima_do_anterior_e_desinstalar_ainda_devolve_o_jogo_original()
    {
        using var game = new SyntheticGame();
        using (var first = PatchPackage.Open(game.BuildPackage(SyntheticGame.Info("1.1.0"))))
        {
            PatchInstaller.Install(game.Game, first, null, Ct);
        }

        var script = File.ReadAllBytes(Path.Combine(game.Build, "Game/script.ypf"));
        script[500] ^= 0x33;
        File.WriteAllBytes(Path.Combine(game.Build, "Game/script.ypf"), script);
        using var second = PatchPackage.Open(game.BuildPackage(SyntheticGame.Info("1.2.0", "1.1.0")));

        Assert.Equal(PatchCheckState.Ready, PatchInstaller.Check(game.Game, second).State);
        PatchInstaller.Install(game.Game, second, null, Ct);

        Assert.Equal(SyntheticGame.Sha(Path.Combine(game.Build, "Game/script.ypf")), SyntheticGame.Sha(Path.Combine(game.Game, "Game/script.ypf")));
        PatchInstaller.Uninstall(game.Game, null, Ct);
        SyntheticGame.AssertSameTree(game.Original, game.Game, PatchInstaller.BackupFolderName);
    }

    [Fact]
    public void Arquivo_mexido_depois_da_instalacao_nao_e_sobrescrito_pela_desinstalacao()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        PatchInstaller.Install(game.Game, package, null, Ct);
        File.WriteAllBytes(Path.Combine(game.Game, "Game/Game.exe"), [1, 2, 3]);

        var result = PatchInstaller.Uninstall(game.Game, null, Ct);

        Assert.Equal(["Game/Game.exe"], result.Kept);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(game.Game, "Game/Game.exe")));
        Assert.Equal(SyntheticGame.Sha(Path.Combine(game.Original, "Game/script.ypf")), SyntheticGame.Sha(Path.Combine(game.Game, "Game/script.ypf")));
    }

    [Fact]
    public void O_plano_da_desinstalacao_diz_o_que_volta_sem_mexer_em_nada()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        PatchInstaller.Install(game.Game, package, null, Ct);
        var before = SyntheticGame.Sha(Path.Combine(game.Game, "Game/script.ypf"));

        var plan = PatchInstaller.PlanUninstall(game.Game);

        Assert.Equal(["Game/Game.exe", "Game/script.ypf"], plan.Restore.Order(StringComparer.Ordinal));
        Assert.Equal([SyntheticGame.CreatedFile], plan.Remove);
        Assert.Empty(plan.Kept);
        Assert.Empty(plan.BrokenBackups);
        Assert.Equal("1.2.0", plan.Version);
        Assert.True(plan.InstalledAt is not null && plan.InstalledAt > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Equal(before, SyntheticGame.Sha(Path.Combine(game.Game, "Game/script.ypf")));
    }

    [Fact]
    public void O_plano_da_desinstalacao_acusa_a_copia_que_sumiu_e_o_arquivo_mexido()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        PatchInstaller.Install(game.Game, package, null, Ct);
        File.Delete(Path.Combine(game.Game, PatchInstaller.BackupFolderName, "Game/script.ypf"));
        File.WriteAllBytes(Path.Combine(game.Game, "Game/Game.exe"), [1, 2, 3]);

        var plan = PatchInstaller.PlanUninstall(game.Game);

        Assert.Equal(["Game/script.ypf"], plan.BrokenBackups);
        Assert.Equal(["Game/Game.exe"], plan.Kept);
        var ex = Assert.Throws<PatchException>(() => PatchInstaller.Uninstall(game.Game, null, Ct));
        Assert.Contains("Game/script.ypf", ex.Message);
    }

    [Fact]
    public void O_plano_da_desinstalacao_sem_instalacao_e_recusado()
    {
        using var game = new SyntheticGame();

        Assert.Throws<PatchException>(() => PatchInstaller.PlanUninstall(game.Game));
    }

    [Fact]
    public void Pacote_que_nao_e_um_patch_e_recusado()
    {
        Assert.Throws<PatchException>(() => PatchPackage.Open(new MemoryStream(new byte[64])));
    }

    [Fact]
    public void A_verificacao_do_lancamento_aplica_numa_copia_limpa_e_confere_com_o_build()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());

        var report = PatchVerifier.Verify(package, game.Sources(), Path.Combine(game.Root, "verificacao"), null, Ct);

        Assert.Equal(3, report.Identical);
        Assert.Equal(2, report.RestoredToOriginal);
        Assert.False(Directory.Exists(Path.Combine(game.Root, "verificacao")));
        SyntheticGame.AssertSameTree(game.Original, game.Game);
    }

    private sealed class Progress(Action<PatchPhase> onPhase) : IProgress<PatchProgress>
    {
        public void Report(PatchProgress value) => onPhase(value.Phase);
    }
}
