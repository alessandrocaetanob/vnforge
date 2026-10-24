using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;

namespace VNForge.Cli;

/// <summary>
/// Ajuda do <c>vnf</c> em português. O <c>HelpAction</c> do System.CommandLine 2.0 não deixa trocar os
/// títulos fixos ("Description:", "[options]"), por isso a ajuda é montada aqui a partir da árvore de comandos.
/// A raiz agrupa os comandos nas seções de <see cref="VnfCommands.Sections"/> e cada comando mostra os exemplos de <see cref="CommandExamples"/>.
/// </summary>
public sealed class PortugueseHelpAction : SynchronousCommandLineAction
{
    public override bool ClearsParseErrors => true;

    public override int Invoke(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        parseResult.InvocationConfiguration.Output.Write(Render(parseResult.CommandResult.Command));
        return 0;
    }

    public static string Render(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var chain = new List<Command>();
        for (Command? current = command; current is not null; current = current.Parents.OfType<Command>().FirstOrDefault())
        {
            chain.Insert(0, current);
        }

        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(command.Description))
        {
            text.AppendLine("Descrição:").AppendLine($"  {command.Description}").AppendLine();
        }

        text.AppendLine("Uso:").AppendLine($"  {Usage(command, chain)}").AppendLine();

        var arguments = command.Arguments.Where(a => !a.Hidden).Select(a => (ArgumentLabel(a), a.Description)).ToList();
        Section(text, "Argumentos:", arguments);

        var subcommands = command.Subcommands.Where(c => !c.Hidden).ToList();
        if (command is RootCommand)
        {
            RootSections(text, subcommands);
        }
        else
        {
            Section(text, "Comandos:", subcommands.Select(c => (c.Name, c.Description)).ToList());
        }

        var options = chain
            .SelectMany(c => c.Options.Where(o => c == command || o.Recursive))
            .Where(o => !o.Hidden)
            .Select(o => (OptionLabel(o), o.Description))
            .ToList();
        Section(text, "Opções:", options);

        var examples = CommandExamples.For(string.Join(' ', chain.Skip(1).Select(c => c.Name)));
        if (examples.Count > 0)
        {
            text.AppendLine("Exemplos:");
            foreach (var example in examples)
            {
                text.AppendLine($"  {example}");
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    /// <summary>Caminho do comando como se digita: <c>vnf</c> para a raiz, <c>vnf ypf info</c> para um subcomando.</summary>
    public static string CommandPath(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var names = new List<string>();
        for (Command? current = command; current is not null; current = current.Parents.OfType<Command>().FirstOrDefault())
        {
            names.Insert(0, current is RootCommand ? "vnf" : current.Name);
        }

        return string.Join(' ', names);
    }

    private static void RootSections(StringBuilder text, List<Command> subcommands)
    {
        var placed = new HashSet<Command>();
        var sections = new List<(string Title, List<(string Label, string? Description)> Rows)>();
        foreach (var section in VnfCommands.Sections)
        {
            var rows = new List<(string Label, string? Description)>();
            foreach (var name in section.Commands)
            {
                if (subcommands.FirstOrDefault(c => c.Name == name) is { } found && placed.Add(found))
                {
                    rows.Add((found.Name, found.Description));
                }
            }

            sections.Add(($"{section.Title}:", rows));
        }

        sections.Add(("Outros comandos:", [.. subcommands.Where(c => !placed.Contains(c)).Select(c => (c.Name, c.Description))]));

        var width = sections.SelectMany(s => s.Rows).Select(r => r.Label.Length).DefaultIfEmpty(0).Max();
        foreach (var (title, rows) in sections)
        {
            Section(text, title, rows, width);
        }
    }

    private static string Usage(Command command, List<Command> chain)
    {
        var usage = new StringBuilder(string.Join(' ', chain.Select(c => c is RootCommand ? "vnf" : c.Name)));
        foreach (var argument in command.Arguments.Where(a => !a.Hidden))
        {
            usage.Append(' ').Append(ArgumentLabel(argument));
        }

        if (command.Subcommands.Any(c => !c.Hidden))
        {
            usage.Append(" [comando]");
        }

        usage.Append(" [opções]");
        return usage.ToString();
    }

    private static string ArgumentLabel(Argument argument) => $"<{argument.HelpName ?? argument.Name}>";

    private static string OptionLabel(Option option)
    {
        var names = option.Aliases.Prepend(option.Name)
            .Distinct()
            .Where(n => !n.StartsWith('/'))
            .OrderBy(n => n.StartsWith("--", StringComparison.Ordinal))
            .ThenBy(n => n, StringComparer.Ordinal);
        var label = string.Join(", ", names);
        return option.ValueType == typeof(bool) || option is System.CommandLine.Help.HelpOption ? label : $"{label} <{option.HelpName ?? "valor"}>";
    }

    private static void Section(StringBuilder text, string title, List<(string Label, string? Description)> rows, int? labelWidth = null)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var width = labelWidth ?? rows.Max(r => r.Label.Length);
        text.AppendLine(title);
        foreach (var (label, description) in rows)
        {
            text.AppendLine($"  {label.PadRight(width)}  {description}".TrimEnd());
        }

        text.AppendLine();
    }
}
