using System.CommandLine;
using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Logging;
using VNForge.Core.Output;

namespace VNForge.Cli;

/// <summary><c>vnf library export</c>: o catálogo das VNs da biblioteca numa página HTML local (<see cref="LibraryCatalog"/>). Só lê a biblioteca, o tempo de jogo e os projetos ligados.</summary>
internal static partial class LibraryCommand
{
    private const string DefaultCatalogFile = "catalogo.html";

    private static readonly Option<string?> CatalogOutputOption = new("--output")
    {
        Description = $"Arquivo HTML de destino (padrão: {DefaultCatalogFile} na pasta atual).",
        HelpName = "arquivo",
    };

    private static readonly Option<bool> CatalogForceOption = new("--force")
    {
        Description = "Troca o arquivo de destino se ele já existir.",
    };

    private static readonly Option<bool> CatalogSpoilersOption = new("--show-spoilers")
    {
        Description = "Põe spoilers (tags, sinopse e personagens) no catálogo, mesmo com a privacidade escondendo.",
    };

    private static readonly Option<bool> CatalogAdultOption = new("--show-adult")
    {
        Description = "Põe capas, retratos e tags de conteúdo adulto no catálogo, mesmo com a privacidade escondendo.",
    };

    private static readonly Option<bool> CatalogNotesOption = new("--with-notes")
    {
        Description = "Põe no catálogo as notas de texto que você escreveu em cada jogo (vnf library status --notes). Sem esta opção, só a sua nota de 1 a 10 sai.",
    };

    private static Command CreateExport(VnfCli cli)
    {
        var command = new Command("export", "Exporta as VNs da biblioteca como página HTML, com o que você jogou, a sua nota e o que traduziu.");
        command.Options.Add(CatalogOutputOption);
        command.Options.Add(CatalogForceOption);
        command.Options.Add(CatalogSpoilersOption);
        command.Options.Add(CatalogAdultOption);
        command.Options.Add(CatalogNotesOption);
        command.SetAction(parseResult => Export(cli, parseResult));
        return command;
    }

    private static int Export(VnfCli cli, ParseResult parseResult)
    {
        var path = Path.GetFullPath(parseResult.GetValue(CatalogOutputOption) ?? DefaultCatalogFile, cli.CurrentDirectory);
        var library = FileOf(cli).Load();
        if (LibraryCatalog.GameHolding(library, path) is { } holder)
        {
            throw new UsageException($"'{path}' fica dentro da pasta do jogo '{holder.Title}' ({holder.Folder}). O VNForge não grava na pasta de um jogo fora do backup; use --output com um destino fora das pastas dos jogos.");
        }

        if (File.Exists(path) && !parseResult.GetValue(CatalogForceOption))
        {
            throw new UsageException($"'{path}' já existe; use --force para trocá-lo ou --output para outro arquivo.");
        }

        var playFile = new PlayLogFile(cli.UserConfigDirectory, cli.Clock);
        var played = playFile.Load();
        WriteWarnings(parseResult, playFile, played);
        var privacy = library.Privacy with
        {
            ShowSpoilers = library.Privacy.ShowSpoilers || parseResult.GetValue(CatalogSpoilersOption),
            ShowAdult = library.Privacy.ShowAdult || parseResult.GetValue(CatalogAdultOption),
        };
        var options = new LibraryCatalogOptions(privacy, parseResult.GetValue(CatalogNotesOption));

        var sheets = new SheetCatalogSource(SheetStoreOf(cli));
        var result = LibraryCatalog.Render(library, played, cli.Clock.GetLocalNow(), options, sheets);
        if (result.Games == 0)
        {
            throw new LibraryException("Nenhum jogo da biblioteca tem jogo do VNDB confirmado, então não há o que exportar. Rode vnf library match para casar os jogos.");
        }

        LibraryCatalog.Write(path, result.Html);

        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { path, games = result.Games, skipped = result.Skipped, privacy, includesNotes = options.IncludeNotes, staleSheetCharacters = sheets.StaleCharacters }));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Catálogo com {CliFormat.Count(result.Games, "VN", "VNs")} gravado em {path}");
        if (result.Skipped > 0)
        {
            output.WriteLine($"{CliFormat.Count(result.Skipped, "jogo ficou", "jogos ficaram")} de fora por não ter jogo do VNDB confirmado (vnf library match).");
        }

        if (sheets.StaleCharacters > 0 && !privacy.ShowSpoilers)
        {
            output.WriteLine($"{CliFormat.Count(sheets.StaleCharacters, "personagem ficou", "personagens ficaram")} fora do catálogo: a ficha é antiga e o nível de spoiler deles é desconhecido. Busque de novo com vnf library sheet fetch <jogo>.");
        }

        output.WriteLine($"Spoilers: {(privacy.ShowSpoilers ? "visíveis" : "escondidos")}. Conteúdo adulto: {(privacy.ShowAdult ? "visível" : "escondido")}.");
        return ErrorHandler.OkExitCode;
    }
}
