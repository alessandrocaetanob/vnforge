using System.Text.RegularExpressions;

namespace VNForge.Cli;

/// <summary>
/// Traduz as mensagens de erro de parse do System.CommandLine 2.0.13, que saem em inglês para
/// opção obrigatória e valor inválido. Reconhece também a forma já localizada, para que o texto final
/// seja sempre o mesmo. O que não for reconhecido mantém o texto original depois de um prefixo em português.
/// </summary>
public static partial class ParseErrorMessages
{
    private static readonly (Regex Pattern, Func<Match, string> Format)[] Rules =
    [
        (RequiredOption(), m => $"A opção '{m.Groups[1].Value}' é obrigatória."),
        (
            InvalidOptionValue(),
            m => $"O valor '{m.Groups[1].Value}' não é válido para a opção '{m.Groups[2].Value}' (esperado: {TypeName(m.Groups[3].Value)})."),
        (
            InvalidArgumentValue(),
            m => $"O valor '{m.Groups[1].Value}' não é válido (esperado: {TypeName(m.Groups[2].Value)})."),
        (Unrecognized(), m => $"'{m.Groups[1].Value}' não é um comando, opção ou argumento conhecido."),
        (MissingCommandArgument(), m => $"Falta um argumento obrigatório do comando '{m.Groups[1].Value}'."),
        (MissingOptionValue(), m => $"A opção '{m.Groups[1].Value}' precisa de um valor."),
        (MissingCommand(), _ => "Informe um comando."),
    ];

    public static string Translate(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        foreach (var (pattern, format) in Rules)
        {
            var match = pattern.Match(message);
            if (match.Success)
            {
                return format(match);
            }
        }

        return $"Uso inválido: {message}";
    }

    private static string TypeName(string typeName) => typeName switch
    {
        "System.Int32" or "System.Int64" or "System.Int16" or "System.UInt32" or "System.UInt64" => "número inteiro",
        "System.Double" or "System.Single" or "System.Decimal" => "número",
        "System.Boolean" => "verdadeiro ou falso",
        _ => typeName[(typeName.LastIndexOf('.') + 1)..],
    };

    [GeneratedRegex(@"^Option '(.+)' is required\.$")]
    private static partial Regex RequiredOption();

    [GeneratedRegex(@"^Cannot parse argument '(.*)' for option '(.+)' as expected type '(.+)'\.$")]
    private static partial Regex InvalidOptionValue();

    [GeneratedRegex(@"^Cannot parse argument '(.*)' as expected type '(.+)'\.$")]
    private static partial Regex InvalidArgumentValue();

    [GeneratedRegex(@"^(?:Unrecognized command or argument|Comando ou argumento) '(.+)'(?: não reconhecido)?\.$")]
    private static partial Regex Unrecognized();

    [GeneratedRegex(@"^(?:Required argument missing for command|Argumento obrigatório ausente para o comando): '(.+)'\.$")]
    private static partial Regex MissingCommandArgument();

    [GeneratedRegex(@"^(?:Required argument missing for option|Argumento obrigatório ausente para a opção): '(.+)'\.$")]
    private static partial Regex MissingOptionValue();

    [GeneratedRegex(@"^(?:Required command was not provided|O comando necessário não foi fornecido)\.$")]
    private static partial Regex MissingCommand();
}
