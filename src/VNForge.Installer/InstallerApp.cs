using VNForge.Patching;

namespace VNForge.Installer;

/// <summary>
/// O instalador de console: confere a versão do jogo, guarda os originais, aplica o patch e desinstala. Os textos e a ordem seguem o quadro #540
/// (pasta, conferência, instalação, concluído, versão errada, já instalada); a janela de 720×480 do quadro é a referência visual, esta é a mesma sequência em texto.
/// </summary>
public static class InstallerApp
{
    public const int Ok = 0;
    public const int Failed = 2;
    public const int WrongVersion = 3;
    public const int Cancelled = 4;

    /// <summary>O aviso que a licença Apache-2.0 do VCDiff pede a quem distribui o instalador (ver THIRD-PARTY-NOTICES.md).</summary>
    public const string ThirdPartyNotice =
        "Este instalador usa a biblioteca VCDiff (https://github.com/SnowflakePowered/vcdiff), derivada de open-vcdiff (Google) e do xdelta3, sob a licença Apache 2.0.\n"
        + "O texto da licença está em https://www.apache.org/licenses/LICENSE-2.0 e na pasta licenses/ do pacote do VNForge.";

    /// <param name="package">O patch embutido no executável; nulo num instalador vazio.</param>
    /// <param name="currentDirectory">Onde o jogador abriu o instalador; é a primeira pasta tentada.</param>
    /// <param name="executableDirectory">Pasta do instalador; é a segunda tentativa, depois a pasta acima dela.</param>
    public static int Run(string[] args, TextReader input, TextWriter output, PatchPackage? package, string currentDirectory, string? executableDirectory = null)
    {
        if (args.Contains("--ajuda") || args.Contains("-h") || args.Contains("-?"))
        {
            output.WriteLine("Uso: instalador [--pasta <pasta do jogo>] [--desinstalar] [--silencioso]");
            output.WriteLine("  --pasta         pasta onde o jogo está instalado (sem isso, o instalador procura e pergunta)");
            output.WriteLine("  --desinstalar   volta ao jogo original");
            output.WriteLine("  --silencioso    não faz perguntas");
            return Ok;
        }

        if (args.Contains("--licencas"))
        {
            output.WriteLine(ThirdPartyNotice);
            return Ok;
        }

        if (package is null)
        {
            output.WriteLine("Este instalador está vazio: não achei o patch dentro dele. Baixe o instalador de novo.");
            return Failed;
        }

        var silent = args.Contains("--silencioso");
        var manifest = package.Manifest;
        output.WriteLine($"{manifest.GameName} · Tradução para português do Brasil");
        output.WriteLine($"Versão {manifest.Version}");
        if (!string.IsNullOrWhiteSpace(manifest.Credits))
        {
            output.WriteLine(manifest.Credits);
        }

        output.WriteLine();
        try
        {
            var folder = ChooseFolder(args, input, output, package, silent, currentDirectory, executableDirectory);
            if (folder is null)
            {
                return Cancelled;
            }

            return args.Contains("--desinstalar")
                ? Uninstall(folder, input, output, silent)
                : InstallOrOffer(folder, input, output, package, silent);
        }
        catch (PatchException ex)
        {
            output.WriteLine(ex.Message);
            return Failed;
        }
    }

    private static int InstallOrOffer(string folder, TextReader input, TextWriter output, PatchPackage package, bool silent)
    {
        var manifest = package.Manifest;
        output.WriteLine("[1/4] Conferir a versão do jogo");
        var check = PatchInstaller.Check(folder, package);
        switch (check.State)
        {
            case PatchCheckState.WrongVersion:
                output.WriteLine(PatchInstaller.DescribeMismatch(check));
                output.WriteLine("Procure uma pasta com a versão do jogo para a qual a tradução foi feita (o leia-me diz qual é).");
                return WrongVersion;

            case PatchCheckState.AlreadyInstalled:
                output.WriteLine($"A tradução {manifest.Version} já está instalada.");
                output.WriteLine("Voltar ao jogo original apaga a tradução do jogo e não mexe nos seus saves.");
                return !silent && Ask(input, output, "Voltar ao jogo original?", defaultYes: false) ? Uninstall(folder, input, output, silent: true) : Ok;
        }

        output.WriteLine($"Jogo conferido: {check.Files.Count} arquivo(s) para alterar.");
        if (!silent && !Ask(input, output, "Antes de mexer no jogo, o instalador guarda uma cópia dos arquivos originais. Instalar?", defaultYes: true))
        {
            output.WriteLine("Nada foi alterado.");
            return Cancelled;
        }

        var result = PatchInstaller.Install(folder, package, new PhaseWriter(output));
        output.WriteLine();
        output.WriteLine("Tradução instalada. Abra o jogo normalmente.");
        output.WriteLine($"{result.Written.Count} arquivo(s) alterado(s). Os originais estão guardados em {result.BackupDirectory}.");
        output.WriteLine("Para voltar ao jogo original, execute este instalador de novo e escolha Voltar ao jogo original.");
        if (!silent && !string.IsNullOrWhiteSpace(manifest.ReadMe) && Ask(input, output, "Ler o leia-me?", defaultYes: false))
        {
            output.WriteLine();
            output.WriteLine(manifest.ReadMe);
        }

        return Ok;
    }

    private static int Uninstall(string folder, TextReader input, TextWriter output, bool silent)
    {
        if (PatchInstaller.InstalledVersion(folder) is not { } version)
        {
            output.WriteLine("Não achei uma tradução instalada por este instalador nesta pasta.");
            return Failed;
        }

        if (!silent && !Ask(input, output, $"Voltar ao jogo original (tira a tradução {version})?", defaultYes: false))
        {
            output.WriteLine("Nada foi alterado.");
            return Cancelled;
        }

        var result = PatchInstaller.Uninstall(folder, new PhaseWriter(output));
        output.WriteLine($"{result.Restored.Count + result.Removed.Count} arquivo(s) voltaram ao original, byte a byte.");
        foreach (var kept in result.Kept)
        {
            output.WriteLine($"Deixei {kept} como está: ele mudou depois da instalação.");
        }

        return Ok;
    }

    private static string? ChooseFolder(string[] args, TextReader input, TextWriter output, PatchPackage package, bool silent, string currentDirectory, string? executableDirectory)
    {
        var index = Array.IndexOf(args, "--pasta");
        if (index >= 0)
        {
            return index + 1 < args.Length ? Path.GetFullPath(args[index + 1], currentDirectory) : throw new PatchException("--pasta precisa de um caminho.");
        }

        var guess = new[] { currentDirectory, executableDirectory, executableDirectory is null ? null : Path.GetDirectoryName(executableDirectory) }
            .Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d))
            .FirstOrDefault(d => PatchInstaller.Check(d!, package).State != PatchCheckState.WrongVersion);
        if (guess is not null && (silent || Ask(input, output, $"Pasta do jogo: {guess}\nUsar esta pasta?", defaultYes: true)))
        {
            return guess;
        }

        if (silent)
        {
            throw new PatchException("Não achei a pasta do jogo. Use --pasta <pasta do jogo>.");
        }

        output.Write("Escolha a pasta onde o jogo está instalado: ");
        var typed = input.ReadLine()?.Trim().Trim('"');
        return string.IsNullOrEmpty(typed) ? null : Path.GetFullPath(typed, currentDirectory);
    }

    private static bool Ask(TextReader input, TextWriter output, string question, bool defaultYes)
    {
        output.Write($"{question} [{(defaultYes ? "S/n" : "s/N")}] ");
        var answer = input.ReadLine()?.Trim();
        return string.IsNullOrEmpty(answer) ? defaultYes : answer.StartsWith('s') || answer.StartsWith('S') || answer.StartsWith('y') || answer.StartsWith('Y');
    }

    private sealed class PhaseWriter(TextWriter output) : IProgress<PatchProgress>
    {
        private PatchPhase? _last;

        public void Report(PatchProgress value)
        {
            if (_last == value.Phase || value.Phase == PatchPhase.Check)
            {
                return;
            }

            _last = value.Phase;
            output.WriteLine(value.Phase switch
            {
                PatchPhase.Backup => "[2/4] Guardar a cópia dos originais",
                PatchPhase.Apply => "[3/4] Aplicar a tradução",
                PatchPhase.Verify => "[4/4] Conferir o resultado",
                PatchPhase.Restore => "Devolver os originais",
                _ => string.Empty,
            });
        }
    }
}
