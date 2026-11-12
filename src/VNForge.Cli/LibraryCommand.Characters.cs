using System.CommandLine;
using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Vndb;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf library characters list|show|sync</c> e <c>vnf library character</c> (d11): os personagens dos jogos da biblioteca como uma área só. <c>sync</c> traz do VNDB as fichas
/// e as edições dos jogos que você tem, <c>list</c> busca e filtra por traço e <c>show</c> mostra um personagem com o "Aparece em". Descrição e traços em português são os da
/// ficha (<c>vnf library sheet translate</c>); spoiler e conteúdo adulto saem escondidos pela privacidade da biblioteca. Os dados ficam em <c>sheets/</c> e <c>library-editions.json</c>.
/// </summary>
internal static partial class LibraryCommand
{
    private static readonly Option<bool> CharactersSpoilersOption = new("--show-spoilers")
    {
        Description = "Mostra os spoilers (jogos, traços, trechos da descrição e o gênero real) só nesta chamada, mesmo com a privacidade escondendo.",
    };

    private static readonly Option<bool> CharactersAdultOption = new("--show-adult")
    {
        Description = "Mostra retratos e traços de conteúdo adulto só nesta chamada, mesmo com a privacidade escondendo.",
    };

    private static readonly Option<string[]> CharactersTraitOption = new("--trait")
    {
        Description = "Só os personagens com este traço: o id (i34) ou um pedaço do nome, em português ou no original. Repita para mais de um.",
        HelpName = "traço",
    };

    private static readonly Option<bool> CharactersAnyOption = new("--any")
    {
        Description = "Com mais de um --trait, aceita o personagem que tem qualquer um (OU). Sem isso, ele precisa ter todos (E).",
    };

    private static readonly Option<string?> CharactersGameOption = new("--appears-in")
    {
        Description = "Só os personagens que aparecem neste jogo da biblioteca (id, nome da pasta ou pedaço do título).",
        HelpName = "jogo",
    };

    private static readonly Option<bool> CharactersAgainOption = new("--again")
    {
        Description = "Busca de novo no VNDB os jogos que já têm ficha e refaz o casamento das edições (as traduções ficam).",
    };

    private static Command CreateCharacters(VnfCli cli)
    {
        var command = new Command("characters", "Os personagens dos seus jogos: busca, filtro por traço e em que jogos cada um aparece.");
        command.Subcommands.Add(CreateCharactersList(cli));
        command.Subcommands.Add(CreateCharactersShow(cli, "show", "Mostra um personagem: descrição e traços em português e os jogos da biblioteca em que ele aparece."));
        command.Subcommands.Add(CreateCharactersSync(cli));
        return command;
    }

    private static Command CreateCharactersList(VnfCli cli)
    {
        var search = new Argument<string?>("busca")
        {
            Description = "Parte do nome, do nome original, de um apelido ou do título de um jogo do personagem.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var command = new Command("list", "Lista os personagens da biblioteca, com busca por nome e filtro por traço.");
        command.Arguments.Add(search);
        command.Options.Add(CharactersTraitOption);
        command.Options.Add(CharactersAnyOption);
        command.Options.Add(CharactersGameOption);
        command.Options.Add(CharactersSpoilersOption);
        command.Options.Add(CharactersAdultOption);
        command.SetAction(parseResult => CharactersList(cli, parseResult, parseResult.GetValue(search)));
        return command;
    }

    private static Command CreateCharactersShow(VnfCli cli, string name, string description)
    {
        var id = new Argument<string>("personagem") { Description = "O id do personagem no VNDB (c123). Veja os ids com vnf library characters list." };
        var command = new Command(name, description);
        command.Arguments.Add(id);
        command.Options.Add(CharactersSpoilersOption);
        command.Options.Add(CharactersAdultOption);
        command.SetAction(parseResult => CharactersShow(cli, parseResult, parseResult.GetRequiredValue(id)));
        return command;
    }

    private static Command CreateCharacter(VnfCli cli) =>
        CreateCharactersShow(cli, "character", "Mostra um personagem (o mesmo que vnf library characters show).");

    private static Command CreateCharactersSync(VnfCli cli)
    {
        var command = new Command("sync", "Traz do VNDB os personagens e as edições dos jogos da biblioteca; não repete o que já veio.");
        command.Options.Add(CharactersAgainOption);
        command.SetAction((parseResult, token) => CharactersSyncAsync(cli, parseResult, token));
        return command;
    }

    private static (GameLibrary Library, LibraryPrivacy Privacy, SheetStore Store, GameEditions Editions) CharactersContext(VnfCli cli, ParseResult parseResult)
    {
        var library = FileOf(cli).Load();
        var privacy = new LibraryPrivacy(
            library.Privacy.ShowSpoilers || parseResult.GetValue(CharactersSpoilersOption),
            parseResult.GetValue(CharactersAdultOption) ? AdultMode.Show : library.Privacy.Adult);
        return (library, privacy, SheetStoreOf(cli), new GameEditionsFile(cli.UserConfigDirectory).Load());
    }

    private static int CharactersList(VnfCli cli, ParseResult parseResult, string? text)
    {
        var (library, privacy, store, editions) = CharactersContext(cli, parseResult);
        var gameId = parseResult.GetValue(CharactersGameOption) is { } reference ? library.Find(reference).Id : null;
        var traits = parseResult.GetValue(CharactersTraitOption) ?? [];
        var filter = new LibraryCharacterFilter(text, traits, parseResult.GetValue(CharactersAnyOption), gameId);
        var list = LibraryCharacters.List(library, store.LoadAllCharacters(), store.LoadTerms(), editions, privacy, filter);
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { characters = list.Characters.Select(WithoutHiddenImage).ToList(), hidden = list.Hidden }));
            return ErrorHandler.OkExitCode;
        }

        var filtered = !string.IsNullOrWhiteSpace(text) || traits.Length > 0 || gameId is not null;
        if (list.Characters.Count == 0)
        {
            output.WriteLine(filtered
                ? "Nenhum personagem da biblioteca combina com a busca."
                : "Nenhum personagem na biblioteca ainda. Traga-os do VNDB com vnf library characters sync.");
        }
        else
        {
            output.WriteLine($"{CliFormat.Count(list.Characters.Count, "personagem", "personagens")}{(filtered ? " na busca" : " na biblioteca")}:");
            foreach (var character in list.Characters)
            {
                var games = string.Join(", ", character.AppearsIn.Select(a => $"{a.GameTitle} ({a.Role})"));
                output.WriteLine($"  {character.Id}  {character.Name}{(character.Original is { } original ? $" ({original})" : string.Empty)} · {games}"
                    + (character.HiddenAppearances > 0 ? $" (mais {CliFormat.Count(character.HiddenAppearances, "jogo escondido", "jogos escondidos")})" : string.Empty));
            }
        }

        if (list.Hidden > 0)
        {
            output.WriteLine($"{CliFormat.Count(list.Hidden, "personagem escondido", "personagens escondidos")} por spoiler; --show-spoilers libera só esta chamada.");
        }

        output.WriteLine(VndbCredit.ShortText);
        return ErrorHandler.OkExitCode;
    }

    private static int CharactersShow(VnfCli cli, ParseResult parseResult, string id)
    {
        var (library, privacy, store, editions) = CharactersContext(cli, parseResult);
        var lookup = LibraryCharacters.Find(library, store.LoadAllCharacters(), store.LoadTerms(), editions, privacy, id.Trim());
        var card = lookup.Card ?? throw new LibraryException(lookup.HiddenBySpoiler
            ? $"O personagem {id.Trim()} só aparece em jogos escondidos por spoiler. Use --show-spoilers para ver."
            : $"O personagem {id.Trim()} não está em nenhum jogo da biblioteca. Veja os personagens com vnf library characters list e traga os que faltam com vnf library characters sync.");
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { character = WithoutHiddenImage(card) }));
            return ErrorHandler.OkExitCode;
        }

        WriteCharacter(output, card, privacy);
        return ErrorHandler.OkExitCode;
    }

    /// <summary>No Desfocar o Core guarda o endereço do retrato adulto para a tela borrá-lo; a CLI não borra, então a imagem escondida sai sem endereço.</summary>
    private static LibraryCharacterCard WithoutHiddenImage(LibraryCharacterCard card) => card.ImageHidden ? card with { Image = null } : card;

    private static void WriteCharacter(TextWriter output, LibraryCharacterCard card, LibraryPrivacy privacy)
    {
        output.WriteLine($"{card.Name}{(card.Original is { } original ? $" ({original})" : string.Empty)} · {card.Id} · {card.Gender}{(card.RealGender is { } real ? $" (real: {real})" : string.Empty)}");
        if (card.Aliases.Count > 0)
        {
            output.WriteLine("  Também chamado de: " + string.Join(", ", card.Aliases));
        }

        if (card.ImageHidden)
        {
            output.WriteLine("  Retrato: escondido (conteúdo adulto; use --show-adult)");
        }
        else if (card.Image is { } image)
        {
            output.WriteLine($"  Retrato: {image.Url}");
        }

        if (card.Description is { } description)
        {
            output.WriteLine($"  Descrição ({SheetState(description)}):");
            WriteIndented(output, description.Text, "    ");
            if (description.HasHiddenSpoilers)
            {
                output.WriteLine("    (trechos de spoiler escondidos; use --show-spoilers)");
            }
        }

        output.WriteLine("  Traços: " + (card.Traits.Count == 0 ? "nenhum à vista" : string.Join(", ", card.Traits.Select(t => t.Name)))
            + (card.HiddenTraits > 0 ? $" ({CliFormat.Count(card.HiddenTraits, "escondido", "escondidos")} por spoiler ou conteúdo adulto)" : string.Empty));
        output.WriteLine("  Aparece em:");
        foreach (var appearance in card.AppearsIn)
        {
            output.WriteLine($"    {appearance.GameTitle} ({appearance.GameId}) · {appearance.Role}"
                + (appearance.Editions.Count > 0 ? $" · edição {string.Join(", ", appearance.Editions)}" : string.Empty));
        }

        if (card.HiddenAppearances > 0)
        {
            output.WriteLine($"    (mais {CliFormat.Count(card.HiddenAppearances, "jogo escondido", "jogos escondidos")} por spoiler; use --show-spoilers)");
        }

        if (!privacy.ShowSpoilers || !privacy.ShowAdult)
        {
            output.WriteLine("  Spoiler e conteúdo adulto escondidos por padrão; --show-spoilers e --show-adult liberam só esta chamada.");
        }

        output.WriteLine("  " + VndbCredit.ShortText);
    }

    private static async Task<int> CharactersSyncAsync(VnfCli cli, ParseResult parseResult, CancellationToken token)
    {
        var library = FileOf(cli).Load();
        var json = parseResult.GetValue(cli.Options.Json);
        var error = parseResult.InvocationConfiguration.Error;
        var progress = json ? null : new SyncProgress<LibraryCharacterSyncProgress>(p => error.WriteLine($"{p.Done}/{p.Total} jogos"));
        var result = await LibraryCharacterSync.SyncAsync(
            cli.CreateVndbCatalog(), library, SheetStoreOf(cli), new GameEditionsFile(cli.UserConfigDirectory), parseResult.GetValue(CharactersAgainOption), cli.Clock, progress, token).ConfigureAwait(false);
        var output = parseResult.InvocationConfiguration.Output;
        var exit = result.Failures.Count > 0 ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode;
        if (json)
        {
            await output.WriteLineAsync(JsonEnvelope.Result(
                new
                {
                    games = result.Games,
                    fetched = result.Fetched,
                    cached = result.Cached,
                    characters = result.Characters,
                    editionsMatched = result.EditionsMatched,
                    editionsIncomplete = result.EditionsIncomplete,
                    failed = result.Failures.Select(f => new { visualNovelId = f.VisualNovelId, error = f.Message }),
                },
                exit)).ConfigureAwait(false);
            return exit;
        }

        if (result.Games == 0)
        {
            await output.WriteLineAsync("Nenhum jogo da biblioteca tem o jogo do VNDB confirmado. Use vnf library match.").ConfigureAwait(false);
            return exit;
        }

        await output.WriteLineAsync($"{CliFormat.Count(result.Games, "jogo do VNDB", "jogos do VNDB")}: {result.Fetched} buscados, {result.Cached} já estavam guardados; "
            + $"{CliFormat.Count(result.Characters, "personagem trazido", "personagens trazidos")} e {CliFormat.Count(result.EditionsMatched, "edição casada", "edições casadas")} com a pasta.").ConfigureAwait(false);
        foreach (var failure in result.Failures)
        {
            await output.WriteLineAsync($"Ficou de fora ({failure.VisualNovelId}): {failure.Message}").ConfigureAwait(false);
        }

        if (result.Failures.Count > 0)
        {
            await output.WriteLineAsync("Rode o comando de novo para tentar outra vez; o que já veio não volta a ser pedido.").ConfigureAwait(false);
        }

        if (result.EditionsIncomplete > 0)
        {
            await output.WriteLineAsync($"Aviso: o VNDB tem mais edições do que o limite trazido em {CliFormat.Count(result.EditionsIncomplete, "pasta", "pastas")}; a edição pode ter ficado de fora e só o papel depende dela (o spoiler não). vnf library characters sync --again refaz.").ConfigureAwait(false);
        }

        await output.WriteLineAsync("Traduza as descrições e os traços de cada jogo com vnf library sheet translate; depois veja com vnf library characters list.").ConfigureAwait(false);
        await output.WriteLineAsync(VndbCredit.ShortText).ConfigureAwait(false);
        return exit;
    }
}
