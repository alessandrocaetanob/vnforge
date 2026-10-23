using System.CommandLine;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary><c>vnf run</c>: abre o executável do jogo registrado no <c>vnforge.json</c>.</summary>
internal static class RunCommand
{
    public static Command Create(VnfCli cli)
    {
        var command = new Command("run", "Abre o jogo registrado no vnforge.json, para testar depois do inject.");
        command.SetAction(parseResult => Run(cli, parseResult));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var executable = new GameLauncher(cli.GameProcessStarter).Launch(project);
        var output = parseResult.InvocationConfiguration.Output;

        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { executable }));
        }
        else
        {
            output.WriteLine($"Jogo aberto: {executable}");
        }

        return ErrorHandler.OkExitCode;
    }
}
