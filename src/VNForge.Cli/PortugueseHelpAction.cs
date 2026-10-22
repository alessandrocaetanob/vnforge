using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;

namespace VNForge.Cli;

/// <summary>
/// Ajuda do <c>vnf</c> em português. O <c>HelpAction</c> do System.CommandLine 2.0 não deixa trocar os
/// títulos fixos ("Description:", "[options]"), por isso a ajuda é montada aqui a partir da árvore de comandos.
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

        var subcommands = command.Subcommands.Where(c => !c.Hidden).Select(c => (c.Name, c.Description)).ToList();
        Section(text, "Comandos:", subcommands);

        var options = chain
            .SelectMany(c => c.Options.Where(o => c == command || o.Recursive))
            .Where(o => !o.Hidden)
            .Select(o => (OptionLabel(o), o.Description))
            .ToList();
        Section(text, "Opções:", options);

        return text.ToString();
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

    private static void Section(StringBuilder text, string title, List<(string Label, string? Description)> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var width = rows.Max(r => r.Label.Length);
        text.AppendLine(title);
        foreach (var (label, description) in rows)
        {
            text.AppendLine($"  {label.PadRight(width)}  {description}".TrimEnd());
        }

        text.AppendLine();
    }
}
