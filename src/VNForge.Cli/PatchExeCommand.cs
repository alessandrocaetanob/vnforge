using System.CommandLine;
using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Core.Injection;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf patch-exe</c> (experimental): aplica no executável do jogo o patch da engine, que no YU-RIS troca o nome da fonte das falas.
/// Passa pelo mesmo fluxo da injeção: backup do original, hash conferido e <c>vnf restore</c> para desfazer.
/// </summary>
internal static class PatchExeCommand
{
    public static Command Create(VnfCli cli)
    {
        var command = new Command("patch-exe", "Experimental: troca o nome da fonte no executável do jogo, com backup (desfaça com vnf restore).");
        command.SetAction(parseResult => Run(cli, parseResult));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var config = project.LoadConfig();
        var engine = KnownEngines.All.FirstOrDefault(e => string.Equals(e.Id, config.Engine.Id, StringComparison.Ordinal))
            ?? throw new VNForgeException($"A engine '{config.Engine.Id}' do {ProjectFolder.ConfigFileName} não é conhecida por esta versão do vnf.");
        if (engine is not IGamePatchProvider provider)
        {
            throw new VNForgeException($"A engine {engine.DisplayName} não tem patch de executável.");
        }

        var output = parseResult.InvocationConfiguration.Output;
        var json = parseResult.GetValue(cli.Options.Json);
        var plans = new List<PatchPlan>();
        try
        {
            foreach (var patch in provider.CreatePatches(config.Game.Exe))
            {
                plans.Add(GameInjector.PlanPatch(project, patch));
            }

            return plans.Count == 0
                ? throw new VNForgeException($"A engine {engine.DisplayName} não tem patch para o executável '{config.Game.Exe}'.")
                : Apply(cli, parseResult, project, plans, output, json);
        }
        finally
        {
            plans.ForEach(GameInjector.DiscardPatch);
        }
    }

    private static int Apply(VnfCli cli, ParseResult parseResult, ProjectFolder project, List<PatchPlan> plans, TextWriter output, bool json)
    {
        var pending = plans.Where(p => p.Injection.Files.Count > 0).ToList();
        if (!json)
        {
            foreach (var plan in pending)
            {
                Describe(cli, plan, output);
            }
        }

        if (pending.Count > 0 && !ConfirmationPrompt.Confirm(parseResult, cli, $"Alterar {pending.Sum(p => p.Injection.Files.Count)} arquivo(s) na pasta do jogo?"))
        {
            output.WriteLine("Operação cancelada. Nada foi gravado.");
            return ErrorHandler.CancelledExitCode;
        }

        using var projectLock = ProjectLock.Acquire(project);
        var injector = new GameInjector();
        var results = plans.Select(plan => (plan.Patch, Result: injector.InjectPatch(plan))).ToList();
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                gameDirectory = plans[0].Injection.GameDirectory,
                patches = results.Select(r => new
                {
                    id = r.Patch.Id,
                    backupDirectory = r.Result.BackupDirectory,
                    written = r.Result.Written,
                    unchanged = r.Result.UnchangedCount,
                }),
            }));
            return ErrorHandler.OkExitCode;
        }

        foreach (var (patch, result) in results)
        {
            if (result.BackupDirectory is null)
            {
                output.WriteLine($"O jogo já está com o patch '{patch.Id}'. Nada a fazer.");
                continue;
            }

            output.WriteLine($"Backup: {result.BackupDirectory}");
            output.WriteLine($"Patch '{patch.Id}' aplicado: {string.Join(", ", result.Written)} (hashes conferidos). Desfaça com vnf restore.");
        }

        return ErrorHandler.OkExitCode;
    }

    private static void Describe(VnfCli cli, PatchPlan plan, TextWriter output)
    {
        output.WriteLine($"Patch '{plan.Patch.Id}': {plan.Patch.Description}");
        if (cli.RichOutput)
        {
            cli.Ansi.Write(RunSummaries.InjectTable(plan.Injection));
            return;
        }

        output.WriteLine($"Jogo: {plan.Injection.GameDirectory}");
        foreach (var file in plan.Injection.Files)
        {
            output.WriteLine($"  {file.RelativePath} ({(file.ReplacesExisting ? "substitui o original" : "arquivo novo")})");
        }
    }
}
