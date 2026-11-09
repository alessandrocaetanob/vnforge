using System.CommandLine;
using System.Globalization;
using Spectre.Console;
using VNForge.Core.Jmdict;
using VNForge.Core.Logging;
using VNForge.Core.Output;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf jmdict install|status|lookup</c>: o dicionário offline do inspetor (#538), o JMdict do EDRDG. O arquivo mora na pasta do usuário e só é baixado quando você pede;
/// rodar <c>install</c> de novo atualiza (a licença do EDRDG pede atualização regular). A consulta não usa a rede.
/// </summary>
internal static class JmdictCommand
{
    public static Command Create(VnfCli cli)
    {
        var command = new Command("jmdict", "Dicionário japonês offline (JMdict, do EDRDG): baixar, ver a situação e consultar.");
        command.Subcommands.Add(CreateInstall(cli));
        command.Subcommands.Add(CreateStatus(cli));
        command.Subcommands.Add(CreateLookup(cli));
        return command;
    }

    private static JmdictStore Store(VnfCli cli) => new(Path.Combine(cli.UserConfigDirectory, "dictionary"));

    private static Command CreateInstall(VnfCli cli)
    {
        var command = new Command("install", $"Baixa o JMdict do EDRDG (cerca de {JmdictInstaller.ApproximateSize}) e monta o dicionário offline. Rode de novo para atualizar.");
        command.SetAction((parseResult, cancellationToken) => InstallAsync(cli, parseResult, cancellationToken));
        return command;
    }

    private static Command CreateStatus(VnfCli cli)
    {
        var command = new Command("status", "Mostra se o dicionário está instalado, de quando é e se já pede atualização.");
        command.SetAction(parseResult => Status(cli, parseResult));
        return command;
    }

    private static Command CreateLookup(VnfCli cli)
    {
        var text = new Argument<string>("texto") { Description = "Uma palavra ou uma frase em japonês; cada palavra achada vira uma linha." };
        var command = new Command("lookup", "Consulta palavras japonesas no dicionário offline (forma conjugada incluída).");
        command.Arguments.Add(text);
        command.SetAction(parseResult => Lookup(cli, parseResult, parseResult.GetRequiredValue(text)));
        return command;
    }

    private static async Task<int> InstallAsync(VnfCli cli, ParseResult parseResult, CancellationToken cancellationToken)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var json = parseResult.GetValue(cli.Options.Json);
        var store = Store(cli);
        if (!json)
        {
            await output.WriteLineAsync($"Baixando o JMdict (cerca de {JmdictInstaller.ApproximateSize}) de {JmdictInstaller.SourceUri.Host}.");
        }

        using var http = cli.HttpClientFactory();
        await new JmdictInstaller(http, store).InstallAsync(null, cancellationToken);
        var index = store.Open()!;
        if (json)
        {
            await output.WriteLineAsync(JsonEnvelope.Result(new { path = store.IndexPath, entries = index.EntryCount, created = index.Created }));
            return ErrorHandler.OkExitCode;
        }

        await output.WriteLineAsync($"Dicionário instalado: {index.EntryCount.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} entradas, JMdict de {index.Created}.");
        await output.WriteLineAsync(JmdictAttribution);
        return ErrorHandler.OkExitCode;
    }

    private const string JmdictAttribution = "JMdict © Electronic Dictionary Research and Development Group (EDRDG), licença CC BY-SA: " + JmdictSource.LicenseUrl;

    private static int Status(VnfCli cli, ParseResult parseResult)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var store = Store(cli);
        var now = TimeProvider.System.GetUtcNow();
        var index = store.IsInstalled ? store.Open() : null;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                installed = index is not null,
                path = store.IndexPath,
                entries = index?.EntryCount,
                created = index?.Created,
                installedAt = store.InstalledAt,
                stale = store.IsStale(now),
            }));
            return ErrorHandler.OkExitCode;
        }

        if (index is null)
        {
            output.WriteLine("O dicionário não está instalado. Rode 'vnf jmdict install'.");
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Dicionário instalado em {store.IndexPath}: {index.EntryCount.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} entradas, JMdict de {index.Created}.");
        output.WriteLine(store.IsStale(now) ? "Passou de 30 dias: rode 'vnf jmdict install' para atualizar." : "Em dia.");
        return ErrorHandler.OkExitCode;
    }

    private static int Lookup(VnfCli cli, ParseResult parseResult, string text)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var index = Store(cli).Open() ?? throw new UsageException("O dicionário não está instalado. Rode 'vnf jmdict install' primeiro.");
        var words = new JmdictLookup(index).Scan(text);
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                words = words.Select(w => new
                {
                    text = text.Substring(w.Start, w.Length),
                    word = w.Word,
                    inflection = w.Inflection,
                    entries = w.Entries.Select(e => new { id = e.Id, kanji = e.Kanji, readings = e.Readings, partsOfSpeech = e.PartsOfSpeech, senses = e.Senses.Select(s => s.GlossText) }),
                }),
            }));
            return ErrorHandler.OkExitCode;
        }

        if (words.Count == 0)
        {
            cli.Ansi.MarkupLine("Nenhuma palavra do texto está no dicionário.");
            return ErrorHandler.OkExitCode;
        }

        var table = CliViews.NewTable(CliViews.Column("Texto"), CliViews.Column("Palavra"), CliViews.Column("Leitura"), CliViews.Column("Classe"), CliViews.Column("Sentidos"));
        foreach (var word in words)
        {
            var entry = word.Main;
            var inflection = word.IsInflected ? $" [dim]({CliViews.Escape(string.Join(", ", word.Inflection))})[/]" : string.Empty;
            table.AddRow(
                new Markup(CliViews.Escape(text.Substring(word.Start, word.Length))),
                new Markup(CliViews.Escape(word.Word) + inflection),
                new Markup(CliViews.Escape(string.Join(" · ", entry.Readings))),
                new Markup(CliViews.Escape(string.Join(", ", entry.PartsOfSpeech))),
                new Markup(CliViews.Escape(string.Join(" | ", entry.Senses.Take(3).Select(s => s.GlossText)))));
        }

        cli.Ansi.Write(table);
        return ErrorHandler.OkExitCode;
    }
}
