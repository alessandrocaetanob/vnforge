using System.CommandLine;
using System.Globalization;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary><c>vnf init &lt;pasta&gt; --game &lt;dir&gt;</c>: cria o projeto, detecta a engine e grava engine, versão e chave no <c>vnforge.json</c>.</summary>
internal static class InitCommand
{
    private const string RemapWarning =
        "A estratégia 'remap' ainda não tem encoder no vnf build (#66): o projeto é criado, mas o build recusa até ela entrar. Use fold, cp1252 ou sjis para construir.";

    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    public static Command Create(VnfCli cli)
    {
        var folder = new Argument<string>("pasta") { Description = "Pasta do novo projeto (criada se não existir)." };
        var game = new Option<string>("--game")
        {
            Description = "Pasta do jogo a traduzir.",
            HelpName = "pasta",
            Required = true,
        };
        var name = new Option<string?>("--name")
        {
            Description = "Nome do projeto (padrão: o nome da pasta).",
            HelpName = "nome",
        };
        var exe = new Option<string?>("--exe")
        {
            Description = "Executável do jogo, relativo à pasta do jogo (padrão: o único .exe da pasta).",
            HelpName = "arquivo",
        };

        var source = new Option<string?>("--source")
        {
            Description = "Idioma do jogo (padrão: ja).",
            HelpName = "idioma",
        };
        var target = new Option<string?>("--target")
        {
            Description = "Idioma da tradução (padrão: pt-BR).",
            HelpName = "idioma",
        };
        var encoding = new Option<string?>("--encoding")
        {
            Description = $"Estratégia de acentuação ({string.Join(", ", ProjectChoices.EncodingStrategies)}; padrão: {ProjectChoices.DefaultEncoding}).",
            HelpName = "estratégia",
        };
        var model = new Option<string?>("--model")
        {
            Description = $"Modelo de IA ({string.Join(", ", ProjectChoices.Models)}; padrão: {ProjectChoices.DefaultAi.Model}).",
            HelpName = "modelo",
        };

        var command = new Command("init", "Cria o projeto de tradução, detecta a engine e grava versão e chave no vnforge.json.");
        command.Arguments.Add(folder);
        command.Options.Add(game);
        command.Options.Add(name);
        command.Options.Add(exe);
        command.Options.Add(source);
        command.Options.Add(target);
        command.Options.Add(encoding);
        command.Options.Add(model);
        command.SetAction(parseResult => Run(
            cli,
            parseResult,
            parseResult.GetRequiredValue(folder),
            parseResult.GetRequiredValue(game),
            new ProjectInitOptions(
                parseResult.GetValue(name),
                parseResult.GetValue(exe),
                parseResult.GetValue(source),
                parseResult.GetValue(target),
                parseResult.GetValue(encoding),
                parseResult.GetValue(model) is { } chosen ? ProjectChoices.DefaultAi with { Model = chosen } : null)));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult, string folderArgument, string gameArgument, ProjectInitOptions options)
    {
        var result = ProjectInitializer.Initialize(
            Path.GetFullPath(folderArgument, cli.CurrentDirectory),
            Path.GetFullPath(gameArgument, cli.CurrentDirectory),
            KnownEngines.All,
            options);

        var output = parseResult.InvocationConfiguration.Output;
        var details = result.Detection.Details;
        var warnings = Warnings(details.GetValueOrDefault(ProjectInitializer.WarningsDetail), result.Config.Encoding.Strategy);
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                folder = result.Folder.Root,
                config = result.Folder.ConfigPath,
                engine = result.Engine.Id,
                version = result.Config.Engine.Version,
                scriptKey = result.Config.Engine.ScriptKey,
                summary = result.Detection.Summary,
                warnings,
            }));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Engine detectada: {result.Engine.DisplayName}{Versions(details)}");
        if (details.TryGetValue("scriptsArchive", out var archive))
        {
            output.WriteLine($"Arquivo de scripts: {archive}{ScriptCount(details)}");
        }

        if (details.TryGetValue(ProjectInitializer.ScriptKeyDetail, out var key))
        {
            output.WriteLine($"Chave XOR dos scripts: {key}{KeyOrigin(details)}");
        }

        if (warnings is not null)
        {
            output.WriteLine($"Aviso: {warnings}");
        }

        output.WriteLine($"Projeto criado em {folderArgument.TrimEnd('/', '\\')}{Path.DirectorySeparatorChar}");
        return ErrorHandler.OkExitCode;
    }

    private static string? Warnings(string? detected, string encodingStrategy)
    {
        if (encodingStrategy != "remap")
        {
            return detected;
        }

        return string.IsNullOrEmpty(detected) ? RemapWarning : $"{detected} {RemapWarning}";
    }

    private static string Versions(IReadOnlyDictionary<string, string> details)
    {
        var parts = new List<string>();
        if (details.TryGetValue(ProjectInitializer.VersionDetail, out var ypf))
        {
            parts.Add($"YPF v{ypf}");
        }

        if (details.TryGetValue("ystbVersion", out var ystb))
        {
            parts.Add($"YSTB v{ystb}");
        }

        return parts.Count == 0 ? string.Empty : $" ({string.Join(", ", parts)})";
    }

    private static string ScriptCount(IReadOnlyDictionary<string, string> details)
    {
        if (!details.TryGetValue("scripts", out var text) || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            return string.Empty;
        }

        return $" ({count.ToString("N0", PortugueseBrazil)} {(count == 1 ? "script" : "scripts")})";
    }

    private static string KeyOrigin(IReadOnlyDictionary<string, string> details)
    {
        if (!details.TryGetValue("scriptKeyOrigin", out var origin))
        {
            return string.Empty;
        }

        return origin.StartsWith("chave padrão", StringComparison.Ordinal) ? " (padrão)" : $" ({origin})";
    }
}
