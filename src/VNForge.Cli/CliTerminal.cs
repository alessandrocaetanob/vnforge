using Spectre.Console;

namespace VNForge.Cli;

/// <summary>
/// O que a CLI sabe do terminal onde roda: se a saída é interativa, se <c>NO_COLOR</c> está definida e como o
/// Spectre deve tratar ANSI e cor. Os testes montam um terminal à mão em vez de depender do processo.
/// </summary>
/// <param name="IsInteractive">A saída vai para um terminal de verdade (não foi redirecionada para arquivo ou pipe).</param>
/// <param name="NoColorEnvironment"><c>NO_COLOR</c> definida e não vazia (https://no-color.org).</param>
/// <param name="Width">Largura fixa em colunas; <c>null</c> deixa o Spectre medir o terminal.</param>
/// <param name="Ansi">Suporte a ANSI quando a saída é rica.</param>
/// <param name="ColorSystem">Sistema de cores quando a saída é rica.</param>
public sealed record CliTerminal(
    bool IsInteractive,
    bool NoColorEnvironment,
    int? Width = null,
    AnsiSupport Ansi = AnsiSupport.Detect,
    ColorSystemSupport ColorSystem = ColorSystemSupport.Detect)
{
    /// <summary>Saída redirecionada: texto simples. É o que os testes usam por padrão.</summary>
    public static CliTerminal Plain(int? width = null) => new(IsInteractive: false, NoColorEnvironment: false, width);

    /// <summary>Terminal interativo com ANSI de 24 bits, para os testes conferirem as cores exatas.</summary>
    public static CliTerminal TrueColor(int? width = null) =>
        new(IsInteractive: true, NoColorEnvironment: false, width, AnsiSupport.Yes, ColorSystemSupport.TrueColor);

    /// <summary>
    /// Terminal de <paramref name="output"/>. Só o <see cref="Console.Out"/> do processo pode ser interativo: um
    /// <see cref="StringWriter"/> ou um arquivo nunca recebe cor, mesmo com a CLI rodando num terminal.
    /// </summary>
    public static CliTerminal ForWriter(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var interactive = ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected;
        var noColor = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        return new CliTerminal(interactive, noColor);
    }
}
