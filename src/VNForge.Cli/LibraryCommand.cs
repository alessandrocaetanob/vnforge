using System.CommandLine;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;
using VNForge.Core.Library;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf library scan|match|list|link|privacy|status|sync|play|option|emulator|tag</c>: as VNs do usuário num lugar só (<c>library.json</c>, na pasta de configuração do usuário). <c>scan</c> reconhece
/// os jogos de uma pasta ou unidade, <c>match</c> casa cada um com o VNDB, <c>list</c> mostra a biblioteca respeitando o que a privacidade esconde e filtra por tag,
/// <c>play</c> abre o jogo e conta o tempo (<c>playtime.json</c>, ao lado). Os subcomandos de <c>play</c> ficam em <c>LibraryCommand.Play.cs</c>.
/// <c>tag</c> cuida das tags do usuário (<c>library-tags.json</c>, ao lado).
/// </summary>
internal static partial class LibraryCommand
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly string[] States = ["confirmed", "pending", "unmatched"];

    private static readonly Option<int?> DepthOption = new("--depth")
    {
        Description = $"Quantas pastas abaixo da raiz percorrer (de 0 a 32; padrão {new LibraryScanOptions().MaxDepth}).",
        HelpName = "n",
    };

    private static readonly Option<bool> UnknownOption = new("--unknown")
    {
        Description = "Também lista pastas com executável e engine desconhecida (por padrão só entram as engines reconhecidas).",
    };

    private static readonly Option<bool> AutoOption = new("--auto")
    {
        Description = "Confirma sozinho o que não deixa dúvida (pontuação de 90% ou mais e sem empate). O resto fica com candidatos.",
    };

    private static readonly Option<string[]> ConfirmOption = new("--confirm")
    {
        Description = "Confirma o jogo do VNDB para um jogo da biblioteca: --confirm <jogo> <vndb-id>.",
        HelpName = "jogo vndb-id",
        Arity = new ArgumentArity(2, 2),
        AllowMultipleArgumentsPerToken = true,
    };

    private static readonly Option<string?> ResetOption = new("--reset")
    {
        Description = "Desfaz a confirmação de um jogo (id, nome da pasta ou pedaço do título).",
        HelpName = "jogo",
    };

    private static readonly Option<string?> OnlyOption = new("--only")
    {
        Description = "Casa só este jogo, mesmo que já tenha sido buscado.",
        HelpName = "jogo",
    };

    private static readonly Option<bool> AgainOption = new("--again")
    {
        Description = "Busca de novo os jogos que já foram buscados e ficaram sem confirmação.",
    };

    private static readonly Option<string?> EngineOption = new("--engine")
    {
        Description = "Só os jogos desta engine (kirikiri, siglus, renpy, yuris...).",
        HelpName = "engine",
    };

    private static readonly Option<string?> StateOption = new("--state")
    {
        Description = $"Só os jogos neste estado do casamento: {string.Join(", ", States)}.",
        HelpName = "estado",
    };

    private static readonly Option<bool> ShowSpoilersOption = new("--show-spoilers")
    {
        Description = "Mostra spoilers (tags e trechos da sinopse) só nesta chamada, mesmo com a privacidade escondendo.",
    };

    private static readonly Option<bool> ShowAdultOption = new("--show-adult")
    {
        Description = "Mostra capa e tags de conteúdo adulto só nesta chamada, mesmo com a privacidade escondendo.",
    };

    private static readonly Option<bool> RemoveOption = new("--remove")
    {
        Description = "Tira a ligação com o projeto de tradução (o projeto não é tocado).",
    };

    private static readonly Option<string?> SpoilersOption = new("--spoilers")
    {
        Description = "Mostrar spoilers por padrão: sim ou nao.",
        HelpName = "sim|nao",
    };

    private static readonly Option<string?> AdultOption = new("--adult")
    {
        Description = "Mostrar capa e tags de conteúdo adulto por padrão: sim ou nao.",
        HelpName = "sim|nao",
    };

    private static readonly Option<string[]> TagOption = new("--tag")
    {
        Description = "Só os jogos com esta tag, sua ou do VNDB (repita para mais de uma). minha:nome e vndb:nome-ou-id limitam a quem a tag é.",
        HelpName = "tag",
    };

    private static readonly Option<string[]> NotOption = new("--not")
    {
        Description = "Tira os jogos com esta tag, sua ou do VNDB (repita para mais de uma).",
        HelpName = "tag",
    };

    private static readonly Option<bool> AnyOption = new("--any")
    {
        Description = "Com mais de uma --tag, aceita o jogo que tem qualquer uma (OU). Sem isso, ele precisa ter todas (E).",
    };

    private static readonly Option<bool> SaveFilterOption = new("--save")
    {
        Description = "Guarda esta combinação de --tag, --any e --not como o filtro salvo. Sem tags, apaga o filtro salvo.",
    };

    private static readonly Option<bool> SavedFilterOption = new("--saved")
    {
        Description = "Usa o filtro por tag salvo com --save.",
    };

    private static readonly Option<string?> ColorOption = new("--color")
    {
        Description = "Cor da tag em rrggbb (por exemplo ff8800). Com #, ponha entre aspas: no shell o # abre um comentário.",
        HelpName = "cor",
    };

    private static readonly Option<bool> MergeOption = new("--merge")
    {
        Description = "Se a tag de destino já existe, junta os jogos das duas nela em vez de recusar.",
    };

    public static Command Create(VnfCli cli)
    {
        var command = new Command("library", "As suas VNs num lugar só: reconhece os jogos de uma pasta e casa cada um com o VNDB.");
        command.Subcommands.Add(CreateScan(cli));
        command.Subcommands.Add(CreateMatch(cli));
        command.Subcommands.Add(CreateList(cli));
        command.Subcommands.Add(CreateLink(cli));
        command.Subcommands.Add(CreatePrivacy(cli));
        command.Subcommands.Add(CreateTag(cli));
        command.Subcommands.Add(LibraryListCommands.CreateStatus(cli));
        command.Subcommands.Add(LibraryListCommands.CreateSync(cli));
        command.Subcommands.Add(CreatePlay(cli));
        command.Subcommands.Add(CreateOption(cli));
        command.Subcommands.Add(CreateEmulator(cli));
        return command;
    }

    private static Command CreateTag(VnfCli cli)
    {
        static Argument<string> NewGame() => new("jogo") { Description = "Jogo da biblioteca (id, nome da pasta ou pedaço do título)." };
        static Argument<string[]> NewNames() => new("tag") { Description = "Uma ou mais tags. Caixa e espaços repetidos não contam: Pra  Traduzir é pra traduzir.", Arity = ArgumentArity.OneOrMore };
        var command = new Command("tag", "Tags suas nos jogos. Ficam só neste computador e não vão para o VNDB.");

        var addGame = NewGame();
        var addNames = NewNames();
        var add = new Command("add", "Põe tags num jogo, criando as que ainda não existem.");
        add.Arguments.Add(addGame);
        add.Arguments.Add(addNames);
        add.Options.Add(ColorOption);
        add.SetAction(parseResult => TagAdd(cli, parseResult, parseResult.GetRequiredValue(addGame), parseResult.GetRequiredValue(addNames)));
        command.Subcommands.Add(add);

        var removeGame = NewGame();
        var removeNames = NewNames();
        var remove = new Command("remove", "Tira tags de um jogo. As tags continuam na lista.");
        remove.Arguments.Add(removeGame);
        remove.Arguments.Add(removeNames);
        remove.SetAction(parseResult => TagRemove(cli, parseResult, parseResult.GetRequiredValue(removeGame), parseResult.GetRequiredValue(removeNames)));
        command.Subcommands.Add(remove);

        var listGame = new Argument<string?>("jogo")
        {
            Description = "Mostra só as tags deste jogo. Sem ele, lista todas as tags com a cor e quantos jogos usam.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var list = new Command("list", "Lista as suas tags, ou as de um jogo, e mostra o filtro salvo.");
        list.Arguments.Add(listGame);
        list.SetAction(parseResult => TagList(cli, parseResult, parseResult.GetValue(listGame)));
        command.Subcommands.Add(list);

        var from = new Argument<string>("de") { Description = "A tag que muda de nome." };
        var to = new Argument<string>("para") { Description = "O novo nome." };
        var rename = new Command("rename", "Renomeia uma tag em todos os jogos que a usam.");
        rename.Arguments.Add(from);
        rename.Arguments.Add(to);
        rename.Options.Add(MergeOption);
        rename.SetAction(parseResult => TagRename(cli, parseResult, parseResult.GetRequiredValue(from), parseResult.GetRequiredValue(to)));
        command.Subcommands.Add(rename);

        var target = new Argument<string>("tag") { Description = "A tag a apagar." };
        var delete = new Command("delete", "Apaga uma tag e a tira de todos os jogos que a usam.");
        delete.Arguments.Add(target);
        delete.SetAction(parseResult => TagDelete(cli, parseResult, parseResult.GetRequiredValue(target)));
        command.Subcommands.Add(delete);
        return command;
    }

    private static Command CreateScan(VnfCli cli)
    {
        var folder = new Argument<string>("pasta") { Description = "Pasta ou unidade onde estão os jogos (D:\\Jogos, por exemplo)." };
        var command = new Command("scan", "Varre uma pasta ou unidade, reconhece a engine de cada jogo e grava a biblioteca.");
        command.Arguments.Add(folder);
        command.Options.Add(DepthOption);
        command.Options.Add(UnknownOption);
        command.SetAction((parseResult, token) => Task.FromResult(Scan(cli, parseResult, parseResult.GetRequiredValue(folder), token)));
        return command;
    }

    private static Command CreateMatch(VnfCli cli)
    {
        var command = new Command("match", "Casa os jogos da biblioteca com o VNDB pelo título da janela, pelo executável e pela pasta.");
        command.Options.Add(AutoOption);
        command.Options.Add(ConfirmOption);
        command.Options.Add(ResetOption);
        command.Options.Add(OnlyOption);
        command.Options.Add(AgainOption);
        command.SetAction((parseResult, token) => MatchAsync(cli, parseResult, token));
        return command;
    }

    private static Command CreateList(VnfCli cli)
    {
        var game = new Argument<string?>("jogo")
        {
            Description = "Mostra a ficha deste jogo (id, nome da pasta ou pedaço do título). Sem ele, lista a biblioteca.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var command = new Command("list", "Lista os jogos da biblioteca, ou mostra a ficha de um, sem spoiler nem conteúdo adulto por padrão.");
        command.Arguments.Add(game);
        command.Options.Add(EngineOption);
        command.Options.Add(StateOption);
        command.Options.Add(TagOption);
        command.Options.Add(AnyOption);
        command.Options.Add(NotOption);
        command.Options.Add(SaveFilterOption);
        command.Options.Add(SavedFilterOption);
        command.Options.Add(ShowSpoilersOption);
        command.Options.Add(ShowAdultOption);
        command.Options.Add(RecentOption);
        command.SetAction(parseResult => List(cli, parseResult, parseResult.GetValue(game)));
        return command;
    }

    private static Command CreateLink(VnfCli cli)
    {
        var game = new Argument<string>("jogo") { Description = "Jogo da biblioteca (id, nome da pasta ou pedaço do título)." };
        var command = new Command("link", "Liga um projeto de tradução (--project) a um jogo da biblioteca, ou desliga com --remove.");
        command.Arguments.Add(game);
        command.Options.Add(RemoveOption);
        command.SetAction(parseResult => Link(cli, parseResult, parseResult.GetRequiredValue(game)));
        return command;
    }

    private static Command CreatePrivacy(VnfCli cli)
    {
        var command = new Command("privacy", "Mostra ou muda o que a biblioteca esconde por padrão (spoiler e conteúdo adulto).");
        command.Options.Add(SpoilersOption);
        command.Options.Add(AdultOption);
        command.SetAction(parseResult => Privacy(cli, parseResult));
        return command;
    }

    private static LibraryFile FileOf(VnfCli cli) => new(cli.UserConfigDirectory);

    private static UserTagsFile TagsFileOf(VnfCli cli) => new(cli.UserConfigDirectory);

    private static int Scan(VnfCli cli, ParseResult parseResult, string folderArgument, CancellationToken token)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var error = parseResult.InvocationConfiguration.Error;
        var json = parseResult.GetValue(cli.Options.Json);
        var depth = parseResult.GetValue(DepthOption) ?? new LibraryScanOptions().MaxDepth;
        if (depth is < 0 or > 32)
        {
            throw new UsageException("--depth precisa estar entre 0 e 32.");
        }

        var root = Path.GetFullPath(folderArgument, cli.CurrentDirectory);
        var file = FileOf(cli);
        var scanner = new LibraryScanner(new EngineRecognizer(KnownEngines.All), new GameWindowTitleReader());
        var result = scanner.Scan(
            root,
            new LibraryScanOptions(depth, parseResult.GetValue(UnknownOption)),
            json ? null : new ScanLines(error),
            token);

        var recent = new RecentProjectsFile(cli.UserConfigDirectory).Load().Select(r => r.Path).ToList();
        var (library, merge, linked) = file.Update(current =>
        {
            var merged = current.Merge(result.Games, root);
            return (current, merged, LibraryProjectLinker.LinkAll(current, recent));
        });

        IReadOnlyList<LibraryGame> missing = result.Cancelled ? [] : merge.Missing;
        var known = result.Games.Count - merge.Added.Count - merge.Updated.Count;
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                root,
                directories = result.Directories,
                cancelled = result.Cancelled,
                found = result.Games.Count,
                added = merge.Added.Select(g => GameData(g, library.Privacy)),
                updated = merge.Updated.Select(g => GameData(g, library.Privacy)),
                unchanged = known,
                missing = missing.Select(g => GameData(g, library.Privacy)),
                linkedProjects = linked.Select(l => new { project = l.ProjectPath, game = l.Game.Id }),
                total = library.Games.Count,
                file = file.Path,
            }));
        }
        else
        {
            WriteScan(cli, output, root, result, merge, missing, linked, known, library.Games.Count, file.Path);
        }

        token.ThrowIfCancellationRequested();
        return ErrorHandler.OkExitCode;
    }

    private static void WriteScan(
        VnfCli cli,
        TextWriter output,
        string root,
        LibraryScanResult result,
        LibraryMergeResult merge,
        IReadOnlyList<LibraryGame> missing,
        IReadOnlyList<LibraryProjectLink> linked,
        int known,
        int total,
        string path)
    {
        if (result.Cancelled)
        {
            output.WriteLine("Varredura cancelada. O que já foi achado ficou na biblioteca; rode de novo para continuar.");
        }

        output.WriteLine($"{CliFormat.Count(result.Directories, "pasta varrida", "pastas varridas")} em {root}: {CliFormat.Count(result.Games.Count, "jogo reconhecido", "jogos reconhecidos")} "
            + $"({CliFormat.Count(merge.Added.Count, "novo", "novos")}, {CliFormat.Count(merge.Updated.Count, "atualizado", "atualizados")}, {CliFormat.Count(known, "já conhecido", "já conhecidos")}).");
        var byEngine = result.Games.GroupBy(g => g.Engine.Name).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
        if (byEngine.Count > 0)
        {
            output.WriteLine("Por engine: " + string.Join(", ", byEngine.Select(g => $"{g.Key} {CliFormat.Number(g.Count())}")) + ".");
        }

        if (merge.Added.Count > 0)
        {
            var table = CliViews.NewTable(CliViews.Column("Id"), CliViews.Column("Engine"), CliViews.Column("Título"), CliViews.Column("Pasta"));
            foreach (var game in merge.Added)
            {
                table.AddRow(new Markup(game.Id), new Markup(CliViews.Escape(game.Engine.Name)), new Markup(CliViews.Escape(game.Title)), new Markup(CliViews.Escape(game.Folder)));
            }

            cli.Ansi.Write(table);
        }

        if (linked.Count > 0)
        {
            output.WriteLine($"{CliFormat.Count(linked.Count, "projeto de tradução ligado", "projetos de tradução ligados")} aos jogos.");
        }

        if (missing.Count > 0)
        {
            output.WriteLine($"{CliFormat.Count(missing.Count, "jogo não foi achado", "jogos não foram achados")} nesta pasta e {(missing.Count == 1 ? "continua" : "continuam")} na biblioteca: {string.Join(", ", missing.Take(10).Select(g => g.Title))}{(missing.Count > 10 ? ", ..." : string.Empty)}.");
        }

        output.WriteLine($"A biblioteca tem {CliFormat.Count(total, "jogo", "jogos")} ({path}).");
        if (merge.Added.Count > 0)
        {
            output.WriteLine("Para casar com o VNDB: vnf library match");
        }
    }

    private static async Task<int> MatchAsync(VnfCli cli, ParseResult parseResult, CancellationToken token)
    {
        var confirm = parseResult.GetValue(ConfirmOption);
        var reset = parseResult.GetValue(ResetOption);
        var only = parseResult.GetValue(OnlyOption);
        var auto = parseResult.GetValue(AutoOption);
        var again = parseResult.GetValue(AgainOption);
        var modes = (confirm is { Length: > 0 } ? 1 : 0) + (reset is not null ? 1 : 0);
        if (modes > 1 || (modes == 1 && (auto || again || only is not null)))
        {
            throw new UsageException("Use só uma coisa por vez: --confirm, --reset, ou o casamento em lote (--auto, --only, --again).");
        }

        var file = FileOf(cli);
        var library = file.Load();
        if (reset is not null)
        {
            return Reset(cli, parseResult, file, reset);
        }

        if (confirm is { Length: > 0 })
        {
            return await ConfirmAsync(cli, parseResult, file, library, confirm[0], confirm[1], token).ConfigureAwait(false);
        }

        return await BatchAsync(cli, parseResult, file, library, auto, again, only, token).ConfigureAwait(false);
    }

    private static int Reset(VnfCli cli, ParseResult parseResult, LibraryFile file, string reference)
    {
        var (game, undone, privacy) = file.Update(current =>
        {
            var found = current.Find(reference);
            if (!found.IsConfirmed)
            {
                throw new UsageException($"'{found.Title}' não tem jogo do VNDB confirmado.");
            }

            var unconfirmed = LibraryMatcher.Unconfirm(found);
            current.Replace(unconfirmed);
            return (found, unconfirmed, current.Privacy);
        });
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { reset = GameData(undone, privacy) }));
        }
        else
        {
            output.WriteLine($"{undone.Title}: a confirmação de {game.VndbId} foi desfeita.");
        }

        return ErrorHandler.OkExitCode;
    }

    private static async Task<int> ConfirmAsync(VnfCli cli, ParseResult parseResult, LibraryFile file, GameLibrary library, string reference, string vndbId, CancellationToken token)
    {
        var game = library.Find(reference);
        var matcher = new LibraryMatcher(cli.CreateVndbCatalog());
        LibraryGame confirmed;
        try
        {
            confirmed = await matcher.ConfirmAsync(game, vndbId.Trim(), token).ConfigureAwait(false);
        }
        catch (ArgumentException e)
        {
            throw new UsageException(e.Message);
        }

        library = file.Update(current =>
        {
            current.Replace(confirmed);
            return current;
        });
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { confirmed = GameData(confirmed, library.Privacy) }));
        }
        else
        {
            output.WriteLine($"{GameLibrary.FolderName(confirmed)}: confirmado como {confirmed.VndbId} {confirmed.Title}.");
        }

        return ErrorHandler.OkExitCode;
    }

    private static async Task<int> BatchAsync(VnfCli cli, ParseResult parseResult, LibraryFile file, GameLibrary library, bool auto, bool again, string? onlyReference, CancellationToken token)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var error = parseResult.InvocationConfiguration.Error;
        var json = parseResult.GetValue(cli.Options.Json);
        var only = onlyReference is null ? null : library.Find(onlyReference).Id;
        var options = new LibraryMatchOptions { AutoConfirm = auto };

        var waiting = library.Games.Count(g => !g.IsConfirmed && (only is not null ? g.Id == only : again || g.SearchedAt is null));
        LibraryMatchRun run;
        if (waiting == 0)
        {
            run = new LibraryMatchRun([], [], [], [], false, null);
        }
        else
        {
            var matcher = new LibraryMatcher(cli.CreateVndbCatalog());
            run = await matcher.MatchAllAsync(library, options, again, only, json ? null : new MatchLines(error), game => KeepMatch(file, game), token).ConfigureAwait(false);
        }

        var exitCode = run.Failures.Count > 0 || run.AbortReason is not null ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode;
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(
                new
                {
                    searched = waiting,
                    auto,
                    confirmed = run.Confirmed.Select(g => GameData(g, library.Privacy)),
                    pending = run.Pending.Select(g => GameData(g, library.Privacy)),
                    notFound = run.NotFound.Select(g => GameData(g, library.Privacy)),
                    failures = run.Failures.Select(f => new { id = f.Game.Id, title = f.Game.Title, error = f.Message }),
                    cancelled = run.Cancelled,
                    abortReason = run.AbortReason,
                },
                exitCode));
        }
        else
        {
            WriteMatch(output, library, run, waiting, auto);
        }

        token.ThrowIfCancellationRequested();
        return exitCode;
    }

    /// <summary>Grava o resultado de um jogo sobre a biblioteca como está agora no disco, sem refazer o que outro processo mudou nem desfazer uma confirmação feita no meio.</summary>
    private static void KeepMatch(LibraryFile file, LibraryGame game) =>
        file.Update(current =>
        {
            if (current.Games.FirstOrDefault(g => g.Id == game.Id) is { IsConfirmed: false })
            {
                current.Replace(game);
            }
        });

    private static void WriteMatch(TextWriter output, GameLibrary library, LibraryMatchRun run, int waiting, bool auto)
    {
        if (waiting == 0)
        {
            output.WriteLine(library.Games.Count == 0
                ? "A biblioteca está vazia. Rode vnf library scan <pasta> primeiro."
                : "Nada para casar: todos os jogos já foram buscados ou confirmados. Use --again para buscar de novo os que ficaram sem confirmação.");
            return;
        }

        if (run.Cancelled)
        {
            output.WriteLine("Casamento cancelado. O que já foi buscado ficou na biblioteca; rode de novo para continuar.");
        }

        output.WriteLine($"{CliFormat.Count(waiting, "jogo buscado", "jogos buscados")} no VNDB: {run.Confirmed.Count} confirmados, {run.Pending.Count} com candidatos, {run.NotFound.Count} sem resultado, {run.Failures.Count} com falha.");
        foreach (var game in run.Confirmed)
        {
            output.WriteLine($"  Confirmado: {GameLibrary.FolderName(game)} -> {game.VndbId} {game.Title} ({TitleSimilarity.Percent(game.MatchScore ?? 0)})");
        }

        foreach (var game in run.Pending)
        {
            output.WriteLine($"  Escolha um: {GameLibrary.FolderName(game)} ({game.Id})");
            foreach (var candidate in game.Candidates)
            {
                output.WriteLine("    " + Describe(candidate));
            }
        }

        foreach (var game in run.NotFound)
        {
            output.WriteLine($"  Sem resultado: {GameLibrary.FolderName(game)} ({game.Id}). Confirme a mão com --confirm {game.Id} <vndb-id>.");
        }

        foreach (var failure in run.Failures.Take(10))
        {
            output.WriteLine($"  Falhou: {GameLibrary.FolderName(failure.Game)}: {failure.Message}");
        }

        if (run.AbortReason is not null)
        {
            output.WriteLine(run.AbortReason);
        }

        if (run.Pending.Count > 0)
        {
            output.WriteLine("Para confirmar um candidato: vnf library match --confirm <jogo> <vndb-id>");
        }
        else if (run.Confirmed.Count == 0 && !auto && run.NotFound.Count < waiting)
        {
            output.WriteLine("Para confirmar sozinho o que não deixa dúvida: vnf library match --auto --again");
        }

        output.WriteLine(VndbCredit.ShortText);
    }

    private static string Describe(LibraryCandidate candidate)
    {
        var original = candidate.OriginalTitle is { Length: > 0 } text && text != candidate.Title ? $" ({text})" : string.Empty;
        var year = candidate.Released is { Length: >= 4 } released ? $", {released[..4]}" : string.Empty;
        var evidence = candidate.Evidence.Count > 0 ? $"  [{string.Join("+", candidate.Evidence)}]" : string.Empty;
        return $"{candidate.VndbId}  {candidate.Title}{original}{year}  {TitleSimilarity.Percent(candidate.Score)}{evidence}";
    }

    private static int List(VnfCli cli, ParseResult parseResult, string? reference)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var json = parseResult.GetValue(cli.Options.Json);
        var library = FileOf(cli).Load();
        var playFile = new PlayLogFile(cli.UserConfigDirectory, cli.Clock);
        var played = playFile.Load();
        WriteWarnings(parseResult, playFile, played);
        var privacy = library.Privacy with
        {
            ShowSpoilers = library.Privacy.ShowSpoilers || parseResult.GetValue(ShowSpoilersOption),
            ShowAdult = library.Privacy.ShowAdult || parseResult.GetValue(ShowAdultOption),
        };

        var tagsFile = TagsFileOf(cli);
        var tags = tagsFile.Load();
        if (reference is not null)
        {
            if (HasTagFilterOptions(parseResult))
            {
                throw new UsageException("--tag, --any, --not, --save e --saved valem para a lista; com um jogo, mostre as tags dele com vnf library tag list <jogo>.");
            }

            var game = library.Find(reference);
            if (json)
            {
                output.WriteLine(JsonEnvelope.Result(new { privacy, game = GameData(game, privacy, played.StatsOf(game.Id), tags.TagsOf(game.Id)) }));
            }
            else
            {
                WriteSheet(output, game, privacy, played.StatsOf(game.Id), tags.TagsOf(game.Id));
            }

            return ErrorHandler.OkExitCode;
        }

        var filter = TagFilterOf(parseResult, tags);
        var ordered = parseResult.GetValue(RecentOption) ? played.ByLastPlayed(library.Games) : library.Sorted();
        var games = filter.Apply(Filter(ordered, parseResult.GetValue(EngineOption), parseResult.GetValue(StateOption)), tags, privacy);
        var saveFilter = parseResult.GetValue(SaveFilterOption);
        if (saveFilter)
        {
            tagsFile.Update(t => t.SavedFilter = filter);
        }

        var shownFilter = filter.Masked(library.Games, tags, privacy, out _);
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                privacy,
                total = library.Games.Count,
                shown = games.Count,
                filter = FilterData(shownFilter),
                filterSaved = saveFilter,
                games = games.Select(g => GameData(g, privacy, played.StatsOf(g.Id), tags.TagsOf(g.Id))),
            }));
            return ErrorHandler.OkExitCode;
        }

        if (library.Games.Count == 0)
        {
            output.WriteLine("A biblioteca está vazia. Rode vnf library scan <pasta> para reconhecer os seus jogos.");
            return ErrorHandler.OkExitCode;
        }

        var showTags = tags.Tags.Count > 0;
        List<TableColumn> columns = [CliViews.Column("Id"), CliViews.Column("Título"), CliViews.Column("Engine"), CliViews.Column("VNDB"), CliViews.Column("Projeto"), CliViews.Column("Tempo"), CliViews.Column("Último jogo")];
        if (showTags)
        {
            columns.Add(CliViews.Column("Minhas tags"));
        }

        var table = CliViews.NewTable([.. columns]);
        foreach (var game in games)
        {
            var stats = played.StatsOf(game.Id);
            List<IRenderable> cells =
            [
                new Markup(game.Id),
                new Markup(CliViews.Escape(game.Title)),
                new Markup(CliViews.Escape(game.Engine.Name)),
                new Markup(CliViews.Escape(MatchText(game))),
                new Markup(game.ProjectPath is null ? "[dim]·[/]" : "sim"),
                new Markup(stats.Sessions == 0 ? "[dim]·[/]" : CliViews.Escape(PlayTime.Format(stats.Total))),
                new Markup(stats.LastPlayedAt is null ? "[dim]·[/]" : CliViews.Escape(LastPlayedText(stats))),
            ];
            if (showTags)
            {
                var own = tags.TagsOf(game.Id);
                cells.Add(new Markup(own.Count == 0 ? "[dim]·[/]" : CliViews.Escape(string.Join(", ", own))));
            }

            table.AddRow([.. cells]);
        }

        cli.Ansi.Write(table);
        if (!filter.IsEmpty)
        {
            output.WriteLine($"Filtro por tag: {DescribeFilter(shownFilter)}.{(saveFilter ? " Salvo." : string.Empty)}");
        }
        else if (saveFilter)
        {
            output.WriteLine("Filtro por tag salvo apagado.");
        }

        output.WriteLine(games.Count == library.Games.Count
            ? $"{CliFormat.Count(games.Count, "jogo", "jogos")} na biblioteca."
            : $"{CliFormat.Count(games.Count, "jogo", "jogos")} de {CliFormat.Number(library.Games.Count)} na biblioteca.");
        return ErrorHandler.OkExitCode;
    }

    private static bool HasTagFilterOptions(ParseResult parseResult) =>
        (parseResult.GetValue(TagOption) ?? []).Length > 0
        || (parseResult.GetValue(NotOption) ?? []).Length > 0
        || parseResult.GetValue(AnyOption)
        || parseResult.GetValue(SaveFilterOption)
        || parseResult.GetValue(SavedFilterOption);

    private static LibraryTagFilter TagFilterOf(ParseResult parseResult, UserTags tags)
    {
        var include = parseResult.GetValue(TagOption) ?? [];
        var exclude = parseResult.GetValue(NotOption) ?? [];
        var any = parseResult.GetValue(AnyOption);
        if (parseResult.GetValue(SavedFilterOption))
        {
            if (include.Length > 0 || exclude.Length > 0 || any || parseResult.GetValue(SaveFilterOption))
            {
                throw new UsageException("--saved usa o filtro que já está salvo; não combina com --tag, --any, --not nem --save.");
            }

            return tags.SavedFilter;
        }

        if (any && include.Length == 0)
        {
            throw new UsageException("--any combina as tags de --tag; informe pelo menos uma --tag.");
        }

        return LibraryTagFilter.Create(include, any, exclude);
    }

    private static object FilterData(LibraryTagFilter filter) => new { include = filter.Include, any = filter.Any, exclude = filter.Exclude };

    private static string DescribeFilter(LibraryTagFilter filter)
    {
        var parts = new List<string>();
        if (filter.Include.Count > 0)
        {
            parts.Add($"com {(filter.Any ? "qualquer uma de" : "todas de")} {string.Join(", ", filter.Include)}");
        }

        if (filter.Exclude.Count > 0)
        {
            parts.Add($"sem {string.Join(", ", filter.Exclude)}");
        }

        return string.Join("; ", parts);
    }

    private static IReadOnlyList<LibraryGame> Filter(IReadOnlyList<LibraryGame> games, string? engine, string? state)
    {
        if (state is not null && !States.Contains(state, StringComparer.Ordinal))
        {
            throw new UsageException($"Estado desconhecido: '{state}'. Use um de: {string.Join(", ", States)}.");
        }

        return [.. games
            .Where(g => engine is null || string.Equals(g.Engine.Id, engine, StringComparison.OrdinalIgnoreCase))
            .Where(g => state switch
            {
                "confirmed" => g.IsConfirmed,
                "pending" => !g.IsConfirmed && g.Candidates.Count > 0,
                "unmatched" => !g.IsConfirmed && g.Candidates.Count == 0,
                _ => true,
            })];
    }

    private static string MatchText(LibraryGame game)
    {
        if (game.IsConfirmed)
        {
            var by = game.Confirmation == LibraryConfirmation.Auto ? "auto" : "manual";
            return $"{game.VndbId} ({by})";
        }

        if (game.Candidates.Count > 0)
        {
            return $"{CliFormat.Count(game.Candidates.Count, "candidato", "candidatos")}";
        }

        return game.SearchedAt is null ? "·" : "não achado";
    }

    private static void WriteSheet(TextWriter output, LibraryGame game, LibraryPrivacy privacy, PlayStats stats, IReadOnlyList<string> userTags)
    {
        output.WriteLine(game.Title);
        output.WriteLine($"  Id: {game.Id}");
        output.WriteLine($"  Pasta: {game.Folder}");
        if (game.Exe is not null)
        {
            output.WriteLine($"  Executável: {game.Exe}");
        }

        output.WriteLine($"  Engine: {game.Engine.Name}{(game.Engine.Translatable ? " (o VNForge traduz)" : string.Empty)}");
        if (game.WindowTitle is not null)
        {
            output.WriteLine($"  Título da janela: {game.WindowTitle}");
        }

        output.WriteLine($"  Projeto de tradução: {game.ProjectPath ?? "nenhum"}");
        output.WriteLine($"  Minhas tags: {(userTags.Count == 0 ? "nenhuma" : string.Join(", ", userTags))}");
        LibraryListCommands.WriteLines(output, game);
        output.WriteLine($"  Localidade japonesa: {(game.RequiresJapaneseLocale ? "sim (abre pelo Locale Emulator)" : "não")}");
        output.WriteLine(stats.Sessions == 0
            ? "  Tempo de jogo: nunca jogado"
            : $"  Tempo de jogo: {PlayTime.Format(stats.Total)} em {CliFormat.Count(stats.Sessions, "sessão", "sessões")}; último em {LastPlayedText(stats)}");
        var card = LibraryVisibility.Card(game, privacy);
        if (card is null)
        {
            output.WriteLine("  VNDB: " + (game.Candidates.Count > 0 ? "ainda sem confirmação; candidatos:" : "ainda sem jogo confirmado (vnf library match)."));
            foreach (var candidate in game.Candidates)
            {
                output.WriteLine("    " + Describe(candidate));
            }

            return;
        }

        output.WriteLine($"  VNDB: {game.VndbId} (https://vndb.org/{game.VndbId})");
        if (card.OriginalTitle is not null && card.OriginalTitle != card.Title)
        {
            output.WriteLine($"  Título original: {card.OriginalTitle}");
        }

        if (card.Released is not null)
        {
            output.WriteLine($"  Lançamento: {card.Released}");
        }

        output.WriteLine($"  Duração: {LengthText(card.LengthMinutes, card.Length)}");
        output.WriteLine($"  Capa: {(card.CoverHidden ? "escondida (conteúdo adulto; use --show-adult)" : card.CoverUrl ?? "sem capa")}");
        output.WriteLine("  Tags: " + (card.Tags.Count == 0 ? "nenhuma" : string.Join(", ", card.Tags.Take(15).Select(t => t.Name))) + (card.HiddenTags > 0 ? $" ({CliFormat.Count(card.HiddenTags, "tag escondida", "tags escondidas")}: spoiler ou conteúdo adulto)" : string.Empty));
        if (card.Synopsis is { Length: > 0 })
        {
            output.WriteLine("  Sinopse:");
            foreach (var line in card.Synopsis.ReplaceLineEndings("\n").Split('\n'))
            {
                output.WriteLine("    " + line);
            }

            if (card.SynopsisHasSpoilers)
            {
                output.WriteLine("    (trechos de spoiler escondidos; use --show-spoilers)");
            }
        }

        output.WriteLine("  " + VndbCredit.ShortText);
    }

    private static string LengthText(int? minutes, int? category)
    {
        if (minutes is { } m)
        {
            return m >= 60 ? $"cerca de {(m / 60.0).ToString("0", PortugueseBrazil)} h" : $"{m} min";
        }

        return category switch
        {
            1 => "muito curta (menos de 2 h)",
            2 => "curta (2 a 10 h)",
            3 => "média (10 a 30 h)",
            4 => "longa (30 a 50 h)",
            5 => "muito longa (mais de 50 h)",
            _ => "sem votos",
        };
    }

    private static int Link(VnfCli cli, ParseResult parseResult, string reference)
    {
        var project = parseResult.GetValue(cli.Options.Project);
        var remove = parseResult.GetValue(RemoveOption);
        if (remove == (project is not null))
        {
            throw new UsageException(remove ? "--remove não leva --project." : "Informe o projeto com --project <pasta>, ou use --remove para desligar.");
        }

        var (library, changed) = FileOf(cli).Update(current =>
        {
            var game = current.Find(reference);
            return (current, remove
                ? LibraryProjectLinker.Unlink(current, game)
                : LibraryProjectLinker.Link(current, game, Path.GetFullPath(project!, cli.CurrentDirectory)));
        });
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { game = GameData(changed, library.Privacy, userTags: TagsFileOf(cli).Load().TagsOf(changed.Id)) }));
        }
        else
        {
            output.WriteLine(remove ? $"{changed.Title}: projeto desligado." : $"{changed.Title}: ligado ao projeto {changed.ProjectPath}.");
        }

        return ErrorHandler.OkExitCode;
    }

    private static int Privacy(VnfCli cli, ParseResult parseResult)
    {
        var newSpoilers = ParseYesNo(parseResult.GetValue(SpoilersOption), "--spoilers");
        var newAdult = ParseYesNo(parseResult.GetValue(AdultOption), "--adult");
        var (library, changed) = FileOf(cli).Update(current =>
        {
            var next = new LibraryPrivacy(newSpoilers ?? current.Privacy.ShowSpoilers, newAdult ?? current.Privacy.ShowAdult);
            var differs = next != current.Privacy;
            current.Privacy = next;
            return (current, differs);
        });
        var spoilers = library.Privacy.ShowSpoilers;
        var adult = library.Privacy.ShowAdult;

        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { privacy = library.Privacy, changed }));
        }
        else
        {
            output.WriteLine($"Spoilers: {(spoilers ? "visíveis" : "escondidos")}. Conteúdo adulto: {(adult ? "visível" : "escondido")}.{(changed ? " Gravado." : string.Empty)}");
        }

        return ErrorHandler.OkExitCode;
    }

    private static int TagAdd(VnfCli cli, ParseResult parseResult, string reference, string[] names)
    {
        var game = FileOf(cli).Load().Find(reference);
        var color = parseResult.GetValue(ColorOption);
        var normalized = names.Select(UserTagName.Normalize).Distinct(StringComparer.Ordinal).ToList();
        var (added, already, own) = TagsFileOf(cli).Update(tags =>
        {
            var added = new List<string>();
            var already = new List<string>();
            foreach (var name in normalized)
            {
                (tags.Has(game.Id, name) ? already : added).Add(name);
                tags.Add(game.Id, name, color);
            }

            return (added, already, tags.TagsOf(game.Id));
        });
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { game = new { id = game.Id, title = game.Title }, added, already, tags = own }));
        }
        else
        {
            output.WriteLine($"{game.Title}: " + string.Join(" ", TagChangeParts(("adicionadas", added), ("já tinha", already))));
        }

        return ErrorHandler.OkExitCode;
    }

    private static int TagRemove(VnfCli cli, ParseResult parseResult, string reference, string[] names)
    {
        var game = FileOf(cli).Load().Find(reference);
        var normalized = names.Select(UserTagName.Normalize).Distinct(StringComparer.Ordinal).ToList();
        var (removed, missing, own) = TagsFileOf(cli).Update(tags =>
        {
            var removed = new List<string>();
            var missing = new List<string>();
            foreach (var name in normalized)
            {
                (tags.Remove(game.Id, name) ? removed : missing).Add(name);
            }

            return (removed, missing, tags.TagsOf(game.Id));
        });
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { game = new { id = game.Id, title = game.Title }, removed, notPresent = missing, tags = own }));
        }
        else
        {
            output.WriteLine($"{game.Title}: " + string.Join(" ", TagChangeParts(("retiradas", removed), ("o jogo não tinha", missing))));
        }

        return ErrorHandler.OkExitCode;
    }

    private static IEnumerable<string> TagChangeParts(params (string Label, List<string> Names)[] groups) =>
        groups.Where(g => g.Names.Count > 0).Select(g => $"{g.Label}: {string.Join(", ", g.Names)}.");

    private static int TagList(VnfCli cli, ParseResult parseResult, string? reference)
    {
        var library = FileOf(cli).Load();
        var tags = TagsFileOf(cli).Load();
        var output = parseResult.InvocationConfiguration.Output;
        var json = parseResult.GetValue(cli.Options.Json);
        if (reference is not null)
        {
            var game = library.Find(reference);
            var own = tags.TagsOf(game.Id);
            if (json)
            {
                output.WriteLine(JsonEnvelope.Result(new { game = new { id = game.Id, title = game.Title }, tags = own.Select(n => tags.Get(n)) }));
            }
            else
            {
                output.WriteLine($"{game.Title}: {(own.Count == 0 ? "sem tags suas" : string.Join(", ", own))}.");
            }

            return ErrorHandler.OkExitCode;
        }

        var ids = library.Games.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var rows = tags.Tags.Select(t => (Tag: t, Games: tags.GamesWith(t.Name).Count(ids.Contains))).ToList();
        var filter = tags.SavedFilter.Masked(library.Games, tags, library.Privacy, out _);
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new { tags = rows.Select(r => new { name = r.Tag.Name, color = r.Tag.Color, games = r.Games }), filter = FilterData(filter) }));
            return ErrorHandler.OkExitCode;
        }

        if (rows.Count == 0)
        {
            output.WriteLine("Você ainda não criou tags. Ponha uma num jogo com vnf library tag add <jogo> <tag>.");
            if (!filter.IsEmpty)
            {
                output.WriteLine($"Filtro salvo: {DescribeFilter(filter)}.");
            }

            return ErrorHandler.OkExitCode;
        }

        var table = CliViews.NewTable(CliViews.Column("Tag"), CliViews.Column("Cor"), CliViews.Column("Jogos", right: true));
        foreach (var (tag, games) in rows)
        {
            table.AddRow(new Markup(CliViews.Escape(tag.Name)), new Markup(tag.Color ?? "[dim]·[/]"), new Markup(CliFormat.Number(games)));
        }

        cli.Ansi.Write(table);
        output.WriteLine(filter.IsEmpty ? "Nenhum filtro por tag salvo." : $"Filtro salvo: {DescribeFilter(filter)}.");
        return ErrorHandler.OkExitCode;
    }

    private static int TagRename(VnfCli cli, ParseResult parseResult, string from, string to)
    {
        var library = FileOf(cli).Load();
        var merge = parseResult.GetValue(MergeOption);
        var target = UserTagName.Normalize(to);
        var (source, merged, games, notices) = TagsFileOf(cli).Update(tags =>
        {
            var source = tags.Get(from).Name;
            var merged = target != source && tags.TryGet(target, out _);
            var games = GamesInLibrary(tags, library, source);
            var edit = tags.Rename(source, target, merge);
            return (source, merged, games, edit.Notices);
        });
        var changed = target != source;
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { from = source, to = target, merged, games, changed, notices }));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine(!changed
            ? $"A tag já se chama '{target}'."
            : $"{(merged ? "Tag mesclada" : "Tag renomeada")}: '{source}' virou '{target}' em {CliFormat.Count(games, "jogo", "jogos")}.");
        WriteNotices(parseResult, notices);
        return ErrorHandler.OkExitCode;
    }

    private static int TagDelete(VnfCli cli, ParseResult parseResult, string name)
    {
        var library = FileOf(cli).Load();
        var output = parseResult.InvocationConfiguration.Output;
        var preview = TagsFileOf(cli).Load();
        var key = preview.Get(name).Name;
        var count = GamesInLibrary(preview, library, key);
        if (count > 0 && !ConfirmationPrompt.Confirm(parseResult, cli, $"Apagar a tag '{key}'? Ela sai de {CliFormat.Count(count, "jogo", "jogos")}."))
        {
            output.WriteLine("Operação cancelada. Nada foi apagado.");
            return ErrorHandler.CancelledExitCode;
        }

        var (games, notices) = TagsFileOf(cli).Update(tags =>
        {
            var games = GamesInLibrary(tags, library, key);
            return (games, tags.Delete(key, library.Games).Notices);
        });
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { deleted = key, games, notices }));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Tag '{key}' apagada, de {CliFormat.Count(games, "jogo", "jogos")}.");
        WriteNotices(parseResult, notices);
        return ErrorHandler.OkExitCode;
    }

    private static void WriteNotices(ParseResult parseResult, IReadOnlyList<string> notices)
    {
        foreach (var notice in notices)
        {
            parseResult.InvocationConfiguration.Output.WriteLine("Aviso: " + notice);
        }
    }

    private static int GamesInLibrary(UserTags tags, GameLibrary library, string name)
    {
        var ids = library.Games.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        return tags.GamesWith(name).Count(ids.Contains);
    }

    private static bool? ParseYesNo(string? text, string option)
    {
        if (text is null)
        {
            return null;
        }

        return text.Trim().ToLowerInvariant() switch
        {
            "sim" or "s" or "on" or "true" or "1" => true,
            "nao" or "não" or "n" or "off" or "false" or "0" => false,
            _ => throw new UsageException($"{option} aceita sim ou nao, não '{text}'."),
        };
    }

    private static object GameData(LibraryGame game, LibraryPrivacy privacy, PlayStats? stats = null, IReadOnlyList<string>? userTags = null) => new
    {
        id = game.Id,
        title = game.Title,
        folder = game.Folder,
        exe = game.Exe,
        engine = new { id = game.Engine.Id, name = game.Engine.Name, source = game.Engine.Source.ToString().ToLowerInvariant(), translatable = game.Engine.Translatable },
        windowTitle = game.WindowTitle,
        vndbId = game.VndbId,
        confirmation = game.Confirmation.ToString().ToLowerInvariant(),
        score = game.MatchScore,
        searched = game.SearchedAt is not null,
        candidates = game.Candidates.Select(c => new { vndbId = c.VndbId, title = c.Title, original = c.OriginalTitle, released = c.Released, score = c.Score, evidence = c.Evidence }),
        project = game.ProjectPath,
        userTags,
        list = LibraryListCommands.Data(game),
        requiresJapaneseLocale = game.RequiresJapaneseLocale,
        play = stats is null ? null : PlayData(stats),
        card = LibraryVisibility.Card(game, privacy),
    };

    private sealed class ScanLines(TextWriter error) : IProgress<LibraryScanProgress>
    {
        private const int Every = 500;

        private int _games;

        public void Report(LibraryScanProgress value)
        {
            if (value.Games != _games)
            {
                _games = value.Games;
                error.WriteLine($"Achado: {value.Current}");
            }
            else if (value.Directories % Every == 0)
            {
                error.WriteLine($"{CliFormat.Count(value.Directories, "pasta varrida", "pastas varridas")}, {CliFormat.Count(value.Games, "jogo", "jogos")}...");
            }
        }
    }

    private sealed class MatchLines(TextWriter error) : IProgress<LibraryMatchProgress>
    {
        public void Report(LibraryMatchProgress value) =>
            error.WriteLine($"{CliFormat.Number(value.Done)}/{CliFormat.Number(value.Total)}  {value.Game.Title}");
    }
}
