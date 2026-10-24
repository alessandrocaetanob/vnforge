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

    /// <summary>Console sobre um terminal montado à mão (<see cref="TestTerminals"/>).</summary>
    public static ConsoleHarness For(CliTerminal terminal) => new(terminal);

    public string Render(Spectre.Console.Rendering.IRenderable renderable)
    {
        Console.Write(renderable);
        return Output;
    }
}

/// <summary>Terminais montados à mão para os testes de saída rica.</summary>
internal static class TestTerminals
{
    /// <summary>
    /// Terminal interativo sem ANSI nem cor: a CLI entra no modo rico (barra, painel, tabela), mas a saída sai em texto puro
    /// e os testes comparam o que o usuário lê.
    /// </summary>
    public static CliTerminal InteractivePlain(int width = 100) =>
        new(IsInteractive: true, NoColorEnvironment: false, width, Spectre.Console.AnsiSupport.No, Spectre.Console.ColorSystemSupport.NoColors);

    /// <summary>
    /// Terminal interativo com ANSI de 24 bits e redesenho liberado: a única forma de a barra ao vivo do Spectre aparecer no
    /// <see cref="StringWriter"/> dos testes. Os testes procuram trechos de texto, que o ANSI não parte.
    /// </summary>
    public static CliTerminal InteractiveAnsi(int width = 100) =>
        CliTerminal.TrueColor(width) with { Interaction = Spectre.Console.InteractionSupport.Yes };
}
