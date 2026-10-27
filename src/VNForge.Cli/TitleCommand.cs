using System.CommandLine;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli;

/// <summary><c>vnf title show|set|clear</c>: o título da janela do jogo, lido pelo <c>vnf extract</c> e gravado pelo <c>vnf build</c>.</summary>
internal static class TitleCommand
{
    public static Command Create(VnfCli cli)
    {
        var command = new Command("title", "Mostra e traduz o título da janela do jogo (game.title do vnforge.json).");
        command.Subcommands.Add(CreateShow(cli));
        command.Subcommands.Add(CreateSet(cli));
        command.Subcommands.Add(CreateClear(cli));
        return command;
    }

    private static Command CreateShow(VnfCli cli)
    {
        var command = new Command("show", "Mostra o título original, lido do jogo, e a tradução.");
        command.SetAction(parseResult => Show(cli, parseResult));
        return command;
    }

    private static Command CreateSet(VnfCli cli)
    {
        var translation = new Argument<string>("traducao") { Description = "Título traduzido; o build o grava no jogo com a codificação do projeto." };
        var command = new Command("set", "Define a tradução do título da janela do jogo.");
        command.Arguments.Add(translation);
        command.SetAction(parseResult => Set(cli, parseResult, parseResult.GetRequiredValue(translation)));
        return command;
    }

    private static Command CreateClear(VnfCli cli)
    {
        var command = new Command("clear", "Apaga a tradução; o build deixa o título original no jogo.");
        command.SetAction(parseResult => Clear(cli, parseResult));
        return command;
    }

    private static int Show(VnfCli cli, ParseResult parseResult)
    {
        var project = Open(cli, parseResult);
        var config = project.LoadConfig();
        var check = Check(config);
        if (parseResult.GetValue(cli.Options.Json))
        {
            parseResult.InvocationConfiguration.Output.WriteLine(JsonEnvelope.Result(Data(config, check)));
            return ErrorHandler.OkExitCode;
        }

        Write(parseResult.InvocationConfiguration.Output, config, check);
        return ErrorHandler.OkExitCode;
    }

    private static int Set(VnfCli cli, ParseResult parseResult, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new UsageException("A tradução do título não pode ser vazia; use vnf title clear para apagá-la.");
        }

        var project = Open(cli, parseResult);
        var config = project.LoadConfig();
        var current = config.Game.Title
            ?? throw new UsageException("O título original ainda não foi lido do jogo. Rode vnf extract antes de traduzir o título.");

        config = config with { Game = config.Game with { Title = current with { Translation = text.Trim() } } };
        project.SaveConfig(config);

        var check = Check(config);
        if (parseResult.GetValue(cli.Options.Json))
        {
            parseResult.InvocationConfiguration.Output.WriteLine(JsonEnvelope.Result(Data(config, check)));
            return ErrorHandler.OkExitCode;
        }

        var output = parseResult.InvocationConfiguration.Output;
        output.WriteLine($"Título atualizado: {current.Original} → {text.Trim()}");
        if (check.Error is { } error)
        {
            output.WriteLine($"Aviso: {error} O build recusa o título enquanto isso não mudar.");
        }

        return ErrorHandler.OkExitCode;
    }

    private static int Clear(VnfCli cli, ParseResult parseResult)
    {
        var project = Open(cli, parseResult);
        var config = project.LoadConfig();
        var had = !string.IsNullOrWhiteSpace(config.Game.Title?.Translation);
        if (config.Game.Title is { } current && had)
        {
            config = config with { Game = config.Game with { Title = current with { Translation = null } } };
            project.SaveConfig(config);
        }

        if (parseResult.GetValue(cli.Options.Json))
        {
            parseResult.InvocationConfiguration.Output.WriteLine(JsonEnvelope.Result(Data(config, Check(config))));
            return ErrorHandler.OkExitCode;
        }

        parseResult.InvocationConfiguration.Output.WriteLine(had ? "Tradução do título apagada; o build deixa o título original no jogo." : "O título não tinha tradução.");
        return ErrorHandler.OkExitCode;
    }

    /// <summary>A mesma conferência do build e da Conferência (<see cref="GameTitleText.Resolve"/>) com a estratégia do projeto.</summary>
    private static GameTitleName Check(ProjectConfig config) =>
        GameTitleText.Resolve(config.Game.Title?.Translation, TextEncoderRegistry.Default.Create(config.Encoding.Strategy));

    private static object Data(ProjectConfig config, GameTitleName check) => new
    {
        original = config.Game.Title?.Original,
        translation = GameTitleText.Normalize(config.Game.Title?.Translation),
        encoding = config.Encoding.Strategy,
        writable = check.IsWritable,
        error = check.Error,
    };

    private static void Write(TextWriter output, ProjectConfig config, GameTitleName check)
    {
        if (config.Game.Title is not { } title)
        {
            output.WriteLine("O título original ainda não foi lido do jogo. Rode vnf extract.");
            return;
        }

        output.WriteLine($"Original: {title.Original}");
        output.WriteLine($"Tradução: {GameTitleText.Normalize(title.Translation) ?? "(sem tradução; o jogo fica com o título original)"}");
        output.WriteLine($"Codificação: {config.Encoding.Strategy}");
        if (check.Error is { } error)
        {
            output.WriteLine($"Aviso: {error}");
        }
    }

    private static ProjectFolder Open(VnfCli cli, ParseResult parseResult) =>
        ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
}
