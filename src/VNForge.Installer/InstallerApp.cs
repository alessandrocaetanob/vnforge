using VNForge.Patching;

namespace VNForge.Installer;

/// <summary>
/// O modo de console do instalador, para automação e testes de ponta a ponta: <c>--silencioso</c> instala ou desinstala sem perguntar,
/// <c>--ajuda</c> e <c>--licencas</c> só escrevem. O jogador comum não passa por aqui: sem essas opções o instalador abre as janelas
/// (<see cref="ViewModels.InstallerViewModel"/>). A ordem e os textos das etapas seguem as das janelas.
/// </summary>
public static class InstallerApp
{
    public const int Ok = 0;
    public const int Failed = 2;
    public const int WrongVersion = 3;
    public const int Cancelled = 4;

    public const string HelpText =
        """
        Uso: instalador [--pasta <pasta do jogo>] [--desinstalar] [--silencioso] [--licencas]
          Sem --silencioso, o instalador abre a janela; --pasta já deixa a pasta escolhida.
          --pasta         pasta onde o jogo está instalado
          --silencioso    instala (ou, com --desinstalar, volta ao jogo original) sem janela e sem perguntas, e escreve o andamento
          --desinstalar   volta ao jogo original (só com --silencioso)
          --licencas      mostra as licenças das bibliotecas que o instalador usa
        Códigos de saída: 0 feito, 2 falhou, 3 o jogo não é da versão esperada, 4 cancelado.
        """;

    /// <param name="package">O patch embutido no executável; nulo num instalador vazio.</param>
    /// <param name="currentDirectory">Onde o jogador abriu o instalador; é a primeira pasta tentada.</param>
    /// <param name="executableDirectory">Pasta do instalador; é a segunda tentativa, depois a pasta acima dela.</param>
    public static int Run(InstallerArguments arguments, TextWriter output, PatchPackage? package, string currentDirectory, string? executableDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Help)
        {
            output.WriteLine(HelpText);
            return Ok;
        }

        if (arguments.Licenses)
        {
            output.WriteLine(ThirdPartyLicenses.Text);
            return Ok;
        }

        if (arguments.Error is not null)
        {
            output.WriteLine(arguments.Error);
            return Failed;
        }

        if (package is null)
        {
            output.WriteLine(EmptyInstallerText);
            return Failed;
        }

        var engine = new PatchEngine(package);
        var manifest = engine.Manifest;
        output.WriteLine($"{manifest.GameName} · Tradução para português do Brasil");
        output.WriteLine($"Versão {manifest.Version}");
        if (!string.IsNullOrWhiteSpace(manifest.Credits))
        {
            output.WriteLine(manifest.Credits);
        }

        output.WriteLine();
        try
        {
            var folder = ChooseFolder(arguments, output, engine, currentDirectory, executableDirectory);
            return arguments.Uninstall ? Uninstall(engine, folder, output) : Install(engine, folder, output);
        }
        catch (PatchException ex)
        {
            output.WriteLine(ex.Message);
            return Failed;
        }
    }

    /// <summary>O texto de quem abre um instalador que não tem patch dentro.</summary>
    public const string EmptyInstallerText = "Este instalador está vazio: não achei o patch dentro dele. Baixe o instalador de novo.";

    private static int Install(PatchEngine engine, string folder, TextWriter output)
    {
        var manifest = engine.Manifest;
        output.WriteLine("[1/4] Conferir a versão do jogo");
        var check = engine.Check(folder);
        switch (check.State)
        {
            case PatchCheckState.WrongVersion:
                output.WriteLine(PatchInstaller.DescribeMismatch(check));
                output.WriteLine("Procure uma pasta com a versão do jogo para a qual a tradução foi feita (o leia-me diz qual é).");
                return WrongVersion;

            case PatchCheckState.AlreadyInstalled:
                output.WriteLine($"A tradução {manifest.Version} já está instalada.");
                output.WriteLine("Para voltar ao jogo original, use --desinstalar --silencioso.");
                return Ok;
        }

        output.WriteLine($"Jogo conferido: {check.Files.Count} arquivo(s) para alterar.");
        var result = engine.Install(folder, new PhaseWriter(output), CancellationToken.None);
        output.WriteLine();
        output.WriteLine("Tradução instalada. Abra o jogo normalmente.");
        output.WriteLine($"{result.Written.Count} arquivo(s) alterado(s). Os originais estão guardados em {result.BackupDirectory}.");
        output.WriteLine("Para voltar ao jogo original, execute este instalador de novo e escolha Voltar ao jogo original.");
        return Ok;
    }

    private static int Uninstall(PatchEngine engine, string folder, TextWriter output)
    {
        if (PatchInstaller.InstalledVersion(folder) is null)
        {
            output.WriteLine("Não achei uma tradução instalada por este instalador nesta pasta.");
            return Failed;
        }

        var result = engine.Uninstall(folder, new PhaseWriter(output), CancellationToken.None);
        output.WriteLine($"{result.Restored.Count + result.Removed.Count} arquivo(s) voltaram ao original, byte a byte.");
        foreach (var kept in result.Kept)
        {
            output.WriteLine($"Deixei {kept} como está: ele mudou depois da instalação.");
        }

        return Ok;
    }

    private static string ChooseFolder(InstallerArguments arguments, TextWriter output, IInstallerEngine engine, string currentDirectory, string? executableDirectory)
    {
        if (arguments.Folder is not null)
        {
            return Path.GetFullPath(arguments.Folder, currentDirectory);
        }

        if (GameFolderLocator.Find(engine, GameFolderLocator.Candidates(currentDirectory, executableDirectory)) is { } found)
        {
            output.WriteLine($"Pasta do jogo: {found.Folder}");
            return found.Folder;
        }

        throw new PatchException("Não achei a pasta do jogo. Use --pasta <pasta do jogo>.");
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
