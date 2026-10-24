using System.CommandLine;

namespace VNForge.Cli;

/// <summary>Confirmação antes de gravar por cima de algo: pulada com <c>--yes</c>, nunca interativa com <c>--json</c> nem sem entrada. A resposta vem de <see cref="VnfCli.Input"/>.</summary>
internal static class ConfirmationPrompt
{
    /// <returns>Verdadeiro se pode seguir; falso se o usuário recusou.</returns>
    /// <exception cref="UsageException">Sem <c>--yes</c> e sem como perguntar (<c>--json</c> ou entrada encerrada).</exception>
    public static bool Confirm(ParseResult parseResult, VnfCli cli, string question)
    {
        if (parseResult.GetValue(cli.Options.Yes))
        {
            return true;
        }

        if (parseResult.GetValue(cli.Options.Json))
        {
            throw new UsageException("Com --json não há como perguntar: confirme com --yes.");
        }

        var error = parseResult.InvocationConfiguration.Error;
        error.Write($"{question} [s/N] ");
        error.Flush();
        var answer = cli.Input.ReadLine();
        if (answer is null)
        {
            throw new UsageException("Não há como pedir a confirmação (entrada encerrada). Use --yes para seguir sem perguntar.");
        }

        return answer.Trim().ToLowerInvariant() is "s" or "sim" or "y" or "yes";
    }
}
