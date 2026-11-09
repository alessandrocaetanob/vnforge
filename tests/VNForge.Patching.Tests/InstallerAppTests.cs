using System.Diagnostics;
using VNForge.Installer;

namespace VNForge.Patching.Tests;

public class InstallerAppTests
{
    [Fact]
    public void Instala_e_desinstala_pela_linha_de_comando_em_modo_silencioso()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var output = new StringWriter();

        var install = InstallerApp.Run(["--pasta", game.Game, "--silencioso"], TextReader.Null, output, package, game.Root);
        var uninstall = InstallerApp.Run(["--pasta", game.Game, "--desinstalar", "--silencioso"], TextReader.Null, output, package, game.Root);

        Assert.Equal(InstallerApp.Ok, install);
        Assert.Equal(InstallerApp.Ok, uninstall);
        Assert.Contains("Tradução instalada", output.ToString());
        SyntheticGame.AssertSameTree(game.Original, game.Game, PatchInstaller.BackupFolderName);
    }

    [Fact]
    public void Versao_errada_sai_com_3_diz_o_arquivo_e_nao_mexe_no_jogo()
    {
        using var game = new SyntheticGame();
        File.WriteAllBytes(Path.Combine(game.Game, "Game/script.ypf"), [9, 9, 9]);
        using var package = PatchPackage.Open(game.BuildPackage());
        var output = new StringWriter();

        var code = InstallerApp.Run(["--pasta", game.Game, "--silencioso"], TextReader.Null, output, package, game.Root);

        Assert.Equal(InstallerApp.WrongVersion, code);
        Assert.Contains("Game/script.ypf", output.ToString());
        Assert.Equal([9, 9, 9], File.ReadAllBytes(Path.Combine(game.Game, "Game/script.ypf")));
        Assert.False(Directory.Exists(Path.Combine(game.Game, PatchInstaller.BackupFolderName)));
    }

    [Fact]
    public void Sem_pasta_o_instalador_procura_na_pasta_atual_e_pergunta_antes_de_instalar()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var output = new StringWriter();

        var code = InstallerApp.Run([], new StringReader("s\ns\nn\n"), output, package, game.Game);

        Assert.Equal(InstallerApp.Ok, code);
        Assert.Contains("Pasta do jogo:", output.ToString());
        Assert.Equal(PatchCheckState.AlreadyInstalled, PatchInstaller.Check(game.Game, package).State);
    }

    [Fact]
    public void Recusar_a_confirmacao_nao_altera_nada()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());

        var code = InstallerApp.Run(["--pasta", game.Game], new StringReader("n\n"), new StringWriter(), package, game.Root);

        Assert.Equal(InstallerApp.Cancelled, code);
        SyntheticGame.AssertSameTree(game.Original, game.Game);
    }

    [Fact]
    public void Licencas_mostra_o_aviso_do_VCDiff_mesmo_sem_patch()
    {
        var output = new StringWriter();

        var code = InstallerApp.Run(["--licencas"], TextReader.Null, output, null, Path.GetTempPath());

        Assert.Equal(InstallerApp.Ok, code);
        Assert.Contains("VCDiff", output.ToString());
        Assert.Contains("Apache License", output.ToString());
        Assert.Contains("Newtonsoft.Json", output.ToString());
        Assert.Contains("Microsoft.IO.RecyclableMemoryStream", output.ToString());
        Assert.Contains("SharpCompress", output.ToString());
    }

    [Fact]
    public void Instalador_vazio_avisa_e_sai_com_erro()
    {
        var output = new StringWriter();

        var code = InstallerApp.Run([], TextReader.Null, output, null, Path.GetTempPath());

        Assert.Equal(InstallerApp.Failed, code);
        Assert.Contains("vazio", output.ToString());
    }

    [Fact]
    public void O_executavel_do_instalador_com_o_patch_dentro_instala_e_desinstala_de_verdade()
    {
        using var game = new SyntheticGame();
        var stub = Path.Combine(AppContext.BaseDirectory, "vnf-instalador" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        Assert.True(File.Exists(stub), $"o build deveria ter copiado {stub}");
        var folder = Path.Combine(game.Root, "instalador");
        Directory.CreateDirectory(folder);
        // VNF_INSTALLER_PUBLISHED aponta para o resultado de `dotnet publish -p:PublishSingleFile=true` (o que vai para o jogador); sem ele, vale o build de teste.
        if (Environment.GetEnvironmentVariable("VNF_INSTALLER_PUBLISHED") is { Length: > 0 } published)
        {
            File.Copy(published, Path.Combine(folder, Path.GetFileName(stub)));
        }
        else
        {
            foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "*").Where(f => Path.GetFileName(f) is "VNForge.Patching.dll" or "VCDiff.dll" or "SharpCompress.dll" or "Newtonsoft.Json.dll" or "Microsoft.IO.RecyclableMemoryStream.dll" || Path.GetFileName(f).StartsWith("vnf-instalador.", StringComparison.Ordinal)))
            {
                File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
            }
        }

        var installer = Path.Combine(folder, Path.GetFileName(stub));
        using (var package = game.BuildPackage())
        {
            InstallerBundle.Create(installer, package, Path.Combine(folder, "tmp.bin"));
        }

        File.Move(Path.Combine(folder, "tmp.bin"), installer, overwrite: true);

        var install = Run(installer, game.Game, "--pasta", game.Game, "--silencioso");
        Assert.Equal(0, install.ExitCode);
        Assert.Contains("Tradução instalada", install.Output);
        Assert.Equal(SyntheticGame.Sha(Path.Combine(game.Build, "Game/script.ypf")), SyntheticGame.Sha(Path.Combine(game.Game, "Game/script.ypf")));

        var uninstall = Run(installer, game.Game, "--pasta", game.Game, "--desinstalar", "--silencioso");
        Assert.Equal(0, uninstall.ExitCode);
        SyntheticGame.AssertSameTree(game.Original, game.Game, PatchInstaller.BackupFolderName);
    }

    private static (int ExitCode, string Output) Run(string executable, string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
