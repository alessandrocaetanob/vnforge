using System.CommandLine;
using VNForge.AI.Claude;
using VNForge.AI.Settings;
using VNForge.Core.Logging;
using VNForge.Core.Output;

namespace VNForge.Cli;

/// <summary><c>vnf config ai</c>: modelo e chave de API do usuário, gravados fora de qualquer projeto.</summary>
internal static class ConfigCommand
{
    public static Command Create(VnfCli cli)
    {
        var command = new Command("config", "Configurações do usuário, guardadas fora do projeto.");
        command.Subcommands.Add(CreateAi(cli));
        return command;
    }

    private static Command CreateAi(VnfCli cli)
    {
        var model = new Option<string?>("--model")
        {
            Description = $"Modelo Claude padrão: {string.Join(", ", ClaudeModels.All)}.",
            HelpName = "id",
        };
        var setKey = new Option<bool>("--set-key")
        {
            Description = "Lê a chave de API da entrada padrão (uma linha) e a guarda no cofre do Windows (DPAPI).",
        };
        var clearKey = new Option<bool>("--clear-key") { Description = "Apaga a chave guardada no cofre do Windows." };

        var command = new Command("ai", "Mostra ou grava o modelo e a chave de API da IA (a chave nunca vai para o projeto).");
        command.Options.Add(model);
        command.Options.Add(setKey);
        command.Options.Add(clearKey);
        command.SetAction(parseResult => Run(cli, parseResult, model, setKey, clearKey));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult, Option<string?> modelOption, Option<bool> setKeyOption, Option<bool> clearKeyOption)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var json = parseResult.GetValue(cli.Options.Json);
        var modelId = parseResult.GetValue(modelOption);
        var setKey = parseResult.GetValue(setKeyOption);
        var clearKey = parseResult.GetValue(clearKeyOption);

        if (setKey && clearKey)
        {
            throw new UsageException("As opções --set-key e --clear-key não podem ser usadas juntas.");
        }

        if (modelId is not null && !ClaudeModels.All.Contains(modelId))
        {
            throw new UsageException($"Modelo desconhecido: '{modelId}'. Use um de: {string.Join(", ", ClaudeModels.All)}.");
        }

        var settingsFile = new AiUserSettingsFile(cli.UserConfigDirectory);
        var store = cli.ApiKeyStore;
        var messages = new List<string>();

        if (modelId is not null)
        {
            settingsFile.Save(new AiUserSettings(modelId));
            messages.Add($"Modelo gravado: {modelId}.");
        }

        if (setKey)
        {
            if (!json)
            {
                parseResult.InvocationConfiguration.Error.Write("Chave de API: ");
                parseResult.InvocationConfiguration.Error.Flush();
            }

            var key = cli.Input.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(key) || key.Any(char.IsWhiteSpace))
            {
                throw new UsageException("A chave de API veio vazia ou com espaços. Envie a chave na entrada padrão, em uma linha.");
            }

            store.Save(key);
            messages.Add("Chave guardada no cofre do Windows (DPAPI).");
        }

        if (clearKey)
        {
            messages.Add(store.Delete() ? "Chave apagada do cofre do Windows." : "Não havia chave no cofre do Windows.");
        }

        var current = settingsFile.Load();
        var resolved = ApiKeyResolver.Resolve(store);
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                model = current.Model,
                keySource = resolved.Source.ToString().ToLowerInvariant(),
                keyConfigured = resolved.Key is not null,
            }));
            return ErrorHandler.OkExitCode;
        }

        foreach (var message in messages)
        {
            output.WriteLine(message);
        }

        output.WriteLine($"Modelo: {current.Model}");
        output.WriteLine($"Chave: {Describe(resolved.Source)}");
        return ErrorHandler.OkExitCode;
    }

    private static string Describe(ApiKeySource source) => source switch
    {
        ApiKeySource.Environment => $"variável de ambiente {ApiKeyResolver.EnvironmentVariable}",
        ApiKeySource.Vault => "cofre do Windows (DPAPI)",
        _ => $"não configurada (defina {ApiKeyResolver.EnvironmentVariable} ou rode 'vnf config ai --set-key')",
    };
}
