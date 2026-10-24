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
/// <param name="Animate">Pode redesenhar a tela. Falso com o erro padrão redirecionado (<c>vnf translate 2&gt; progresso.log</c>): a barra iria para o terminal e o log por lote sumiria do arquivo.</param>
/// <param name="Interaction">Se o Spectre pode redesenhar a tela (barras ao vivo) quando a saída é rica; os testes forçam <see cref="InteractionSupport.Yes"/>.</param>
public sealed record CliTerminal(
    bool IsInteractive,
    bool NoColorEnvironment,
    int? Width = null,
    AnsiSupport Ansi = AnsiSupport.Detect,
    ColorSystemSupport ColorSystem = ColorSystemSupport.Detect,
    InteractionSupport Interaction = InteractionSupport.Detect,
    bool Animate = true)
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
    public static CliTerminal ForWriter(TextWriter output) => ForWriter(output, Environment.GetEnvironmentVariable);

    /// <summary>Como <see cref="ForWriter(TextWriter)"/>, lendo o ambiente por <paramref name="getEnvironmentVariable"/> (os testes não mexem no ambiente do processo).</summary>
    public static CliTerminal ForWriter(TextWriter output, Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(output);

        var interactive = ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected;
        var noColor = !string.IsNullOrEmpty(getEnvironmentVariable("NO_COLOR"));
        return new CliTerminal(interactive, noColor, Animate: !Console.IsErrorRedirected);
    }
}
