using System.CommandLine;
using VNForge.Core;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli;

/// <summary><c>vnf lines keep|unkeep</c>: marca ou desmarca linhas como "Não traduzir" (<see cref="TranslationStatus.Keep"/>), que o build copia como estão.</summary>
internal static class LinesCommand
{
    public static Command Create(VnfCli cli)
    {
        var command = new Command("lines", "Marca linhas que ficam como no original (Não traduzir).");
        command.Subcommands.Add(CreateChange(cli, "keep", true, "Marca linhas como Não traduzir: o build copia o original e elas contam como prontas."));
        command.Subcommands.Add(CreateChange(cli, "unkeep", false, "Tira linhas de Não traduzir; elas voltam a ficar sem tradução."));
        return command;
    }

    private static Command CreateChange(VnfCli cli, string name, bool keep, string description)
    {
        var targets = new Argument<string[]>("alvos")
        {
            Description = "Ids de linha (yst00012:000034) e chaves de script (yst00012, todas as linhas dele), separados por vírgula ou espaço.",
            Arity = ArgumentArity.OneOrMore,
        };
        var command = new Command(name, description);
        command.Arguments.Add(targets);
        command.SetAction(parseResult => Change(cli, parseResult, name, keep, parseResult.GetRequiredValue(targets)));
        return command;
    }

    private static int Change(VnfCli cli, ParseResult parseResult, string action, bool keep, string[] tokens)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var byScript = Resolve(project.LoadAllText(), tokens);

        var editor = new LineEditor(project, TimeProvider.System);
        var changed = new List<LineChange>();
        var requested = 0;
        foreach (var (key, ids) in byScript)
        {
            requested += ids.Count;
            changed.AddRange(editor.SetKeepMany(key, ids, keep));
        }

        var unchanged = requested - changed.Count;
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                action,
                changed = changed.Count,
                unchanged,
                lines = changed.Select(c => c.Saved.Id),
                scripts = byScript.Keys,
            }));
            return ErrorHandler.OkExitCode;
        }

        var verb = keep ? "marcadas como Não traduzir" : "devolvidas a sem tradução";
        var rest = unchanged > 0 ? $" ({CliFormat.Count(unchanged, "linha já estava", "linhas já estavam")} no estado pedido)" : string.Empty;
        output.WriteLine($"{CliFormat.Count(changed.Count, "linha", "linhas")} {verb}{rest}.");
        return ErrorHandler.OkExitCode;
    }

    // Cada alvo é um id de linha ou uma chave de script; um desconhecido aborta antes de gravar qualquer coisa.
    private static SortedDictionary<string, List<string>> Resolve(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts, IEnumerable<string> tokens)
    {
        var scriptOfId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, units) in texts)
        {
            foreach (var unit in units)
            {
                scriptOfId[unit.Id] = key;
            }
        }

        var byScript = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var unknown = new List<string>();
        foreach (var target in tokens.SelectMany(t => t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            if (texts.TryGetValue(target, out var units))
            {
                AddAll(byScript, target, units.Select(u => u.Id));
            }
            else if (scriptOfId.TryGetValue(target, out var key))
            {
                AddAll(byScript, key, [target]);
            }
            else
            {
                unknown.Add(target);
            }
        }

        return unknown.Count == 0
            ? byScript
            : throw new VNForgeException($"Não existe linha nem script com {(unknown.Count == 1 ? "o alvo" : "os alvos")} '{string.Join("', '", unknown)}' nos text/*.json do projeto.");
    }

    private static void AddAll(SortedDictionary<string, List<string>> byScript, string key, IEnumerable<string> ids)
    {
        if (!byScript.TryGetValue(key, out var list))
        {
            byScript[key] = list = [];
        }

        foreach (var id in ids)
        {
            if (!list.Contains(id, StringComparer.Ordinal))
            {
                list.Add(id);
            }
        }
    }
}
