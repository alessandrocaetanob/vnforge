using Spectre.Console;
using VNForge.Cli;

namespace VNForge.Cli.Tests;

/// <summary>
/// Console do Spectre sobre um <see cref="StringWriter"/>, sem ANSI e com largura fixa, para os testes dos helpers de
/// saída compararem texto puro. <see cref="Rich"/> liga cores de 24 bits para os testes que conferem a paleta.
/// </summary>
internal sealed class ConsoleHarness
{
    private readonly StringWriter _writer = new();

    private ConsoleHarness(CliTerminal terminal)
    {
        Console = CliConsole.Create(_writer, new GlobalSettings(null, false, false, false, false), terminal);
    }

    public IAnsiConsole Console { get; }

    /// <summary>Tudo que já foi escrito no console.</summary>
    public string Output => _writer.ToString();

    /// <summary>Linhas escritas, sem a quebra final.</summary>
    public string[] Lines => Output.TrimEnd('\r', '\n').Split(["\r\n", "\n"], StringSplitOptions.None);

    /// <summary>Console de texto simples (o que sai redirecionado, com <c>--json</c>, <c>--quiet</c> ou <c>NO_COLOR</c>).</summary>
    public static ConsoleHarness Plain(int? width = 80) => new(CliTerminal.Plain(width));

    /// <summary>Console como o de um terminal interativo, com ANSI de 24 bits.</summary>
    public static ConsoleHarness Rich(int width = 80) => new(CliTerminal.TrueColor(width));

    public string Render(Spectre.Console.Rendering.IRenderable renderable)
    {
        Console.Write(renderable);
        return Output;
    }
}
