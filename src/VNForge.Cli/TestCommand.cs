using System.CommandLine;
using System.Globalization;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Qa;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf test</c>: confere a tradução, reconstrói o <c>build/</c>, guarda a cópia de segurança, injeta no jogo e abre o executável, tudo com
/// <see cref="GameTester"/>, o mesmo serviço da tela "Testar no jogo". Desfaz com <c>vnf restore</c>.
/// </summary>
internal static class TestCommand
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly Option<string?> OnlyScriptOption = new("--only-script")
    {
        Description = "Testa só esta cena (a chave do script, como yst00012); as outras ficam como no jogo original.",
        HelpName = "chave",
    };

    private static readonly Option<bool> AccentTestOption = new("--accent-test")
    {
        Description = "Injeta só uma frase fixa com ç, ã, é e ô na primeira fala do primeiro script, sem olhar a tradução do projeto.",
    };

    private static readonly Option<bool> AllowWarningsOption = new("--allow-warnings")
    {
        Description = "Testa mesmo com avisos da Conferência; erros continuam travando o teste e saem com 2.",
    };

    private static readonly Option<bool> NoOpenOption = new("--no-open")
    {
        Description = "Prepara e injeta, mas não abre o jogo no fim.",
    };

    public static Command Create(VnfCli cli)
    {
        var command = new Command("test", "Confere, guarda a cópia de segurança, injeta e abre o jogo (desfaça com vnf restore).");
        command.Options.Add(OnlyScriptOption);
        command.Options.Add(AccentTestOption);
        command.Options.Add(AllowWarningsOption);
        command.Options.Add(NoOpenOption);
        command.SetAction((parseResult, token) => Task.FromResult(Run(cli, parseResult, token)));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult, CancellationToken token)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var output = parseResult.InvocationConfiguration.Output;
        var json = parseResult.GetValue(cli.Options.Json);
        var options = new GameTestOptions(
            parseResult.GetValue(OnlyScriptOption),
            parseResult.GetValue(AccentTestOption),
            parseResult.GetValue(AllowWarningsOption),
            OpenGame: !parseResult.GetValue(NoOpenOption));

        if (!ConfirmationPrompt.Confirm(parseResult, cli, $"Gravar {WhatGoesToTheGame(options)} na pasta do jogo, depois de guardar os originais?"))
        {
            output.WriteLine("Operação cancelada. Nada foi gravado.");
            return ErrorHandler.CancelledExitCode;
        }

        var tester = new GameTester(KnownEngines.All, cli.GameProcessStarter, cli.Qa);
        var steps = new InlineProgress<GameTestStep>(step =>
        {
            if (!json)
            {
                output.WriteLine($"[{(int)step + 1}/5] {step.Label()}");
            }
        });

        GameTestResult result;
        try
        {
            result = tester.Run(project, options, steps, cancellationToken: token);
        }
        catch (QaBlockedException blocked)
        {
            return BuildCommand.RunQaBlocked(json, blocked, output);
        }

        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                kind = result.Kind == GameTestKind.AccentTest ? "accent-test" : "translation",
                gameDirectory = result.Injection.GameDirectory,
                backupDirectory = result.Injection.BackupDirectory,
                written = result.Injection.Written,
                unchanged = result.Injection.UnchangedCount,
                executable = result.Executable,
                steps = result.Steps.Select(s => new { step = s.Step.ToString().ToLowerInvariant(), milliseconds = (long)s.Elapsed.TotalMilliseconds }),
            }));
            return ErrorHandler.OkExitCode;
        }

        WriteSummary(output, result);
        return ErrorHandler.OkExitCode;
    }

    private static string WhatGoesToTheGame(GameTestOptions options)
    {
        if (options.AccentTest)
        {
            return "a frase dos acentos";
        }

        return options.OnlyScript is { } scene ? $"a cena {scene}" : "a tradução";
    }

    private static void WriteSummary(TextWriter output, GameTestResult result)
    {
        var injection = result.Injection;
        output.WriteLine(injection.BackupDirectory is null
            ? $"O jogo já estava igual ao build: nada foi copiado ({injection.UnchangedCount.ToString("N0", PortugueseBrazil)} arquivo(s) iguais)."
            : $"Backup: {injection.BackupDirectory}\nInjetado: {injection.Written.Count.ToString("N0", PortugueseBrazil)} arquivo(s) em {injection.GameDirectory} (hashes conferidos)");
        if (result.Executable is not null)
        {
            output.WriteLine($"Jogo aberto: {result.Executable}");
        }

        output.WriteLine("Tempos: " + string.Join(" · ", result.Steps.Select(s => $"{s.Step.Label().ToLowerInvariant()} {s.Elapsed.TotalSeconds.ToString("0.0", PortugueseBrazil)} s")));
        output.WriteLine("Para voltar ao jogo original: vnf restore");
    }
}
