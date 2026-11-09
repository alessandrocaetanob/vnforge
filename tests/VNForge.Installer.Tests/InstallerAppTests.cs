using System.Diagnostics;
using VNForge.Patching;
using VNForge.Patching.Tests;

namespace VNForge.Installer.Tests;

/// <summary>O modo de console do instalador: o que a automação e os testes de ponta a ponta usam.</summary>
public class InstallerAppTests
{
    private static InstallerArguments Parse(params string[] args) => InstallerArguments.Parse(args);

    [Fact]
    public void Instala_e_desinstala_pela_linha_de_comando_em_modo_silencioso()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var output = new StringWriter();

        var install = InstallerApp.Run(Parse("--pasta", game.Game, "--silencioso"), output, package, game.Root);
        var uninstall = InstallerApp.Run(Parse("--pasta", game.Game, "--desinstalar", "--silencioso"), output, package, game.Root);

        Assert.Equal(InstallerApp.Ok, install);
        Assert.Equal(InstallerApp.Ok, uninstall);
        Assert.Contains("Tradução instalada", output.ToString());
        Assert.Contains("voltaram ao original", output.ToString());
        SyntheticGame.AssertSameTree(game.Original, game.Game, PatchInstaller.BackupFolderName);
    }

    [Fact]
    public void Versao_errada_sai_com_3_diz_o_arquivo_e_nao_mexe_no_jogo()
    {
        using var game = new SyntheticGame();
        File.WriteAllBytes(Path.Combine(game.Game, "Game/script.ypf"), [9, 9, 9]);
        using var package = PatchPackage.Open(game.BuildPackage());
        var output = new StringWriter();

        var code = InstallerApp.Run(Parse("--pasta", game.Game, "--silencioso"), output, package, game.Root);

        Assert.Equal(InstallerApp.WrongVersion, code);
        Assert.Contains("Game/script.ypf", output.ToString());
        Assert.Equal([9, 9, 9], File.ReadAllBytes(Path.Combine(game.Game, "Game/script.ypf")));
        Assert.False(Directory.Exists(Path.Combine(game.Game, PatchInstaller.BackupFolderName)));
    }

    [Fact]
    public void Sem_pasta_o_modo_silencioso_procura_na_pasta_atual()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var output = new StringWriter();

        var code = InstallerApp.Run(Parse("--silencioso"), output, package, game.Game);

        Assert.Equal(InstallerApp.Ok, code);
        Assert.Contains("Pasta do jogo:", output.ToString());
        Assert.Equal(PatchCheckState.AlreadyInstalled, PatchInstaller.Check(game.Game, package).State);
    }

    [Fact]
    public void Sem_pasta_e_sem_jogo_por_perto_manda_usar_a_opcao_pasta()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var output = new StringWriter();

        var code = InstallerApp.Run(Parse("--silencioso"), output, package, game.Root);

        Assert.Equal(InstallerApp.Failed, code);
        Assert.Contains("--pasta", output.ToString());
        SyntheticGame.AssertSameTree(game.Original, game.Game);
    }

    [Fact]
    public void Instalar_de_novo_em_silencioso_diz_que_ja_esta_instalada_e_nao_mexe_em_nada()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        InstallerApp.Run(Parse("--pasta", game.Game, "--silencioso"), new StringWriter(), package, game.Root);
        var output = new StringWriter();

        var code = InstallerApp.Run(Parse("--pasta", game.Game, "--silencioso"), output, package, game.Root);

        Assert.Equal(InstallerApp.Ok, code);
        Assert.Contains("já está instalada", output.ToString());
        Assert.Equal(PatchCheckState.AlreadyInstalled, PatchInstaller.Check(game.Game, package).State);
    }

    [Fact]
    public void Desinstalar_uma_pasta_sem_instalacao_falha_sem_mexer()
    {
        using var game = new SyntheticGame();
        using var package = PatchPackage.Open(game.BuildPackage());
        var output = new StringWriter();

        var code = InstallerApp.Run(Parse("--pasta", game.Game, "--desinstalar", "--silencioso"), output, package, game.Root);

        Assert.Equal(InstallerApp.Failed, code);
        Assert.Contains("Não achei uma tradução instalada", output.ToString());
        SyntheticGame.AssertSameTree(game.Original, game.Game);
    }

    [Fact]
    public void Pasta_sem_caminho_e_erro_mesmo_sem_silencioso()
    {
        var arguments = Parse("--pasta");
        var output = new StringWriter();

        var code = InstallerApp.Run(arguments, output, null, Path.GetTempPath());

        Assert.True(arguments.UsesConsole);
        Assert.Equal(InstallerApp.Failed, code);
        Assert.Contains("--pasta precisa de um caminho", output.ToString());
    }

    [Fact]
    public void Licencas_mostra_o_aviso_do_VCDiff_mesmo_sem_patch()
    {
        var output = new StringWriter();

        var code = InstallerApp.Run(Parse("--licencas"), output, null, Path.GetTempPath());

        Assert.Equal(InstallerApp.Ok, code);
        Assert.Contains("VCDiff", output.ToString());
        Assert.Contains("Apache License", output.ToString());
        Assert.Contains("Newtonsoft.Json", output.ToString());
        Assert.Contains("Microsoft.IO.RecyclableMemoryStream", output.ToString());
        Assert.Contains("SharpCompress", output.ToString());
    }

    [Theory]
    [InlineData("--ajuda")]
    [InlineData("-h")]
    [InlineData("-?")]
    public void Ajuda_diz_que_sem_silencioso_abre_a_janela_e_lista_os_codigos_de_saida(string flag)
    {
        var output = new StringWriter();

        var code = InstallerApp.Run(Parse(flag), output, null, Path.GetTempPath());

        Assert.Equal(InstallerApp.Ok, code);
        Assert.Contains("abre a janela", output.ToString());
        Assert.Contains("--silencioso", output.ToString());
        Assert.Contains("4 cancelado", output.ToString());
    }

    [Fact]
    public void Instalador_vazio_avisa_e_sai_com_erro()
    {
        var output = new StringWriter();

        var code = InstallerApp.Run(Parse("--silencioso"), output, null, Path.GetTempPath());

        Assert.Equal(InstallerApp.Failed, code);
        Assert.Contains("vazio", output.ToString());
    }

    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--pasta", "D:\\Jogo" }, false)]
    [InlineData(new[] { "--desinstalar" }, false)]
    [InlineData(new[] { "--silencioso" }, true)]
    [InlineData(new[] { "--ajuda" }, true)]
    [InlineData(new[] { "--licencas" }, true)]
    public void So_as_opcoes_de_console_deixam_de_abrir_a_janela(string[] args, bool console)
    {
        Assert.Equal(console, InstallerArguments.Parse(args).UsesConsole);
    }

    [Fact]
    public void O_executavel_do_instalador_com_o_patch_dentro_instala_e_desinstala_de_verdade()
    {
        using var game = new SyntheticGame();
        var stub = Path.Combine(AppContext.BaseDirectory, "vnf-instalador" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        Assert.True(File.Exists(stub), $"o build deveria ter copiado {stub}");
        string installer;
        // VNF_INSTALLER_PUBLISHED aponta para o resultado de `dotnet publish -p:PublishSingleFile=true` (o que vai para o jogador); sem ele, vale o build de teste.
        if (Environment.GetEnvironmentVariable("VNF_INSTALLER_PUBLISHED") is { Length: > 0 } published)
        {
            installer = Path.Combine(game.Root, "instalador", Path.GetFileName(stub));
            Directory.CreateDirectory(Path.GetDirectoryName(installer)!);
            using var package = game.BuildPackage();
            File.Copy(published, installer);
            var bundled = installer + ".tmp";
            InstallerBundle.Create(installer, package, bundled);
            File.Move(bundled, installer, overwrite: true);
        }
        else
        {
            // O apphost abre a vnf-instalador.dll que está ao lado dele, qualquer que seja o nome do .exe: o pacote vai para um nome só deste teste, no próprio
            // diretório do build, onde estão a dll, o deps.json e o Avalonia.
            installer = Path.Combine(AppContext.BaseDirectory, "vnf-instalador-e2e-" + Guid.NewGuid().ToString("N") + Path.GetExtension(stub));
            using var package = game.BuildPackage();
            InstallerBundle.Create(stub, package, installer);
        }

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(installer, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            var install = Run(installer, game.Game, "--pasta", game.Game, "--silencioso");
            Assert.Equal(0, install.ExitCode);
            Assert.Contains("Tradução instalada", install.Output);
            Assert.Equal(SyntheticGame.Sha(Path.Combine(game.Build, "Game/script.ypf")), SyntheticGame.Sha(Path.Combine(game.Game, "Game/script.ypf")));

            var uninstall = Run(installer, game.Game, "--pasta", game.Game, "--desinstalar", "--silencioso");
            Assert.Equal(0, uninstall.ExitCode);
            SyntheticGame.AssertSameTree(game.Original, game.Game, PatchInstaller.BackupFolderName);

            var help = Run(installer, game.Game, "--ajuda");
            Assert.Equal(0, help.ExitCode);
            Assert.Contains("--silencioso", help.Output);

            if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("VNF_INSTALLER_PUBLISHED") is { Length: > 0 })
            {
                AssertWindowOpens(installer, game);
            }
        }
        finally
        {
            if (installer.StartsWith(AppContext.BaseDirectory, StringComparison.Ordinal))
            {
                File.Delete(installer);
            }
        }
    }

    /// <summary>Sem opção de console o instalador abre a janela: o processo segue vivo e a janela leva o nome do jogo do patch (o que só aparece se o XAML, os bindings e o patch embutido sobreviveram ao trimming).</summary>
    private static void AssertWindowOpens(string installer, SyntheticGame game)
    {
        var info = new ProcessStartInfo(installer) { WorkingDirectory = game.Game };
        using var process = Process.Start(info)!;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            var title = string.Empty;
            while (DateTime.UtcNow < deadline && !process.HasExited && !title.Contains("Jogo Sintético", StringComparison.Ordinal))
            {
                Thread.Sleep(250);
                process.Refresh();
                title = process.MainWindowTitle;
            }

            Assert.False(process.HasExited, $"o instalador fechou sozinho com o código {(process.HasExited ? process.ExitCode : 0)}");
            Assert.Equal("Tradução PT-BR · Jogo Sintético", title);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
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
