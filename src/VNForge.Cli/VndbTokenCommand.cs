using System.CommandLine;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf vndb token set|clear|check</c> (o grupo <c>token</c> de <see cref="VndbCommand"/>): o token pessoal da conta do VNDB, que deixa <c>vnf library sync</c> ler e gravar a lista. Fica no cofre do Windows (DPAPI)
/// ou na variável de ambiente <c>VNDB_TOKEN</c> (que tem prioridade), nunca em arquivo do projeto ou da biblioteca. O token só entra pela entrada padrão e nunca sai
/// em mensagem, log ou <c>--json</c>.
/// </summary>
internal static class VndbTokenCommand
{
    /// <summary>O grupo <c>token</c>, que entra em <c>vnf vndb</c>.</summary>
    public static Command Create(VnfCli cli)
    {
        var token = new Command("token", "Guarda, apaga ou confere o token da sua conta do VNDB.");
        token.Subcommands.Add(CreateSet(cli));
        token.Subcommands.Add(CreateClear(cli));
        token.Subcommands.Add(CreateCheck(cli));
        return token;
    }

    private static Command CreateSet(VnfCli cli)
    {
        var command = new Command("set", "Guarda no cofre do Windows o token lido da entrada padrão, depois de conferi-lo no VNDB.");
        command.SetAction((parseResult, token) => SetAsync(cli, parseResult, token));
        return command;
    }

    private static Command CreateClear(VnfCli cli)
    {
        var command = new Command("clear", "Apaga o token do cofre do Windows.");
        command.SetAction(parseResult => Clear(cli, parseResult));
        return command;
    }

    private static Command CreateCheck(VnfCli cli)
    {
        var command = new Command("check", "Confere no VNDB o token em uso e mostra a conta e as permissões.");
        command.SetAction((parseResult, token) => CheckAsync(cli, parseResult, token));
        return command;
    }

    private static async Task<int> SetAsync(VnfCli cli, ParseResult parseResult, CancellationToken cancellationToken)
    {
        var json = parseResult.GetValue(cli.Options.Json);
        if (!json)
        {
            await parseResult.InvocationConfiguration.Error.WriteAsync("Token do VNDB: ");
            await parseResult.InvocationConfiguration.Error.FlushAsync(cancellationToken);
        }

        var token = (await cli.Input.ReadLineAsync(cancellationToken))?.Trim();
        if (string.IsNullOrEmpty(token) || token.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            throw new UsageException("O token do VNDB veio vazio ou com espaços. Envie o token na entrada padrão, em uma linha. Gere um em vndb.org/u/tokens.");
        }

        var account = await cli.CreateVndbUserList().GetAuthInfoAsync(token, cancellationToken).ConfigureAwait(false);
        cli.VndbTokenStore.Save(token);

        var inUse = cli.ResolveVndbToken().Source;
        var output = parseResult.InvocationConfiguration.Output;
        if (json)
        {
            await output.WriteLineAsync(JsonEnvelope.Result(new { saved = true, account = AccountData(account), source = SourceName(inUse) }));
            return ErrorHandler.OkExitCode;
        }

        await output.WriteLineAsync($"Token guardado no cofre do Windows (DPAPI). Conta: {account.Username} ({account.UserId}).");
        WritePermissions(output, account);
        if (inUse == VndbTokenSource.Environment)
        {
            await output.WriteLineAsync($"A variável de ambiente {VndbTokenResolver.EnvironmentVariable} está definida e tem prioridade sobre o cofre.");
        }

        return ErrorHandler.OkExitCode;
    }

    private static int Clear(VnfCli cli, ParseResult parseResult)
    {
        var had = cli.VndbTokenStore.Delete();
        var inUse = cli.ResolveVndbToken().Source;
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { cleared = had, source = SourceName(inUse) }));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine(had ? "Token apagado do cofre do Windows." : "Não havia token no cofre do Windows.");
        if (inUse == VndbTokenSource.Environment)
        {
            output.WriteLine($"A variável de ambiente {VndbTokenResolver.EnvironmentVariable} continua definida e segue valendo.");
        }

        return ErrorHandler.OkExitCode;
    }

    private static async Task<int> CheckAsync(VnfCli cli, ParseResult parseResult, CancellationToken cancellationToken)
    {
        var resolved = cli.ResolveVndbToken();
        if (resolved.Token is null)
        {
            throw new UsageException($"Não há token do VNDB. Defina {VndbTokenResolver.EnvironmentVariable} ou rode vnf vndb token set.{(resolved.Notice is null ? string.Empty : " " + resolved.Notice)}");
        }

        var account = await cli.CreateVndbUserList().GetAuthInfoAsync(resolved.Token, cancellationToken).ConfigureAwait(false);
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            await output.WriteLineAsync(JsonEnvelope.Result(new { valid = true, source = SourceName(resolved.Source), account = AccountData(account) }));
            return ErrorHandler.OkExitCode;
        }

        await output.WriteLineAsync($"Token válido ({(resolved.Source == VndbTokenSource.Environment ? "variável de ambiente " + VndbTokenResolver.EnvironmentVariable : "cofre do Windows (DPAPI)")}). Conta: {account.Username} ({account.UserId}).");
        WritePermissions(output, account);
        return ErrorHandler.OkExitCode;
    }

    private static void WritePermissions(TextWriter output, VndbAuthInfo account)
    {
        output.WriteLine($"Permissões: {(account.Permissions.Count == 0 ? "nenhuma" : string.Join(", ", account.Permissions))}.");
        if (!account.Permissions.Contains("listread"))
        {
            output.WriteLine("Falta a permissão listread: sem ela a sincronização não funciona. Gere outro token em vndb.org/u/tokens.");
        }
        else if (!account.Permissions.Contains("listwrite"))
        {
            output.WriteLine("Falta a permissão listwrite: o VNForge só consegue ler a lista (vnf library sync --pull).");
        }
    }

    private static object AccountData(VndbAuthInfo account) => new
    {
        userId = account.UserId,
        username = account.Username,
        permissions = account.Permissions,
        canRead = account.Permissions.Contains("listread"),
        canWrite = account.Permissions.Contains("listwrite"),
    };

    private static string SourceName(VndbTokenSource source) => source.ToString().ToLowerInvariant();
}
