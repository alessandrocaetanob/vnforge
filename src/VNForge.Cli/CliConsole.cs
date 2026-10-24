using Spectre.Console;

namespace VNForge.Cli;

/// <summary>Cria o <see cref="IAnsiConsole"/> da CLI sobre o <see cref="TextWriter"/> da invocação.</summary>
public static class CliConsole
{
    /// <summary>
    /// Cor e animação só em terminal interativo, e nunca com <c>--json</c>, <c>--quiet</c>, <c>--no-color</c> ou
    /// <c>NO_COLOR</c>. Fora disso a saída é texto simples.
    /// </summary>
    public static bool IsRich(GlobalSettings settings, CliTerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(terminal);

        return terminal.IsInteractive
            && !terminal.NoColorEnvironment
            && !settings.Json
            && !settings.Quiet
            && !settings.NoColor;
    }

    public static IAnsiConsole Create(TextWriter output, GlobalSettings settings, CliTerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(output);

        var rich = IsRich(settings, terminal);
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = rich ? terminal.Ansi : AnsiSupport.No,
            ColorSystem = rich ? terminal.ColorSystem : ColorSystemSupport.NoColors,
            Interactive = rich ? InteractionSupport.Detect : InteractionSupport.No,
            Out = new AnsiConsoleOutput(output),
        });

        if (terminal.Width is { } width)
        {
            console.Profile.Width = width;
        }

        return console;
    }
}
