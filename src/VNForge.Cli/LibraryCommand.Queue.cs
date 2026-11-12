using System.CommandLine;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Rendering;
using VNForge.Core.Library;
using VNForge.Core.Logging;
using VNForge.Core.Memory;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;

namespace VNForge.Cli;

/// <summary><c>vnf library queue|translate</c>: a fila de tradução da biblioteca e o "Traduzir este jogo", que cria o projeto do jogo escolhido.</summary>
internal static partial class LibraryCommand
{
    private static readonly Option<bool> QueueAllOption = new("--all")
    {
        Description = "Mostra também os jogos que a fila deixa de fora: com projeto, já traduzidos e de engine que o VNForge ainda não traduz.",
    };

    private static readonly Option<bool> QueueOfflineOption = new("--offline")
    {
        Description = "Não consulta o VNDB: usa só o que a última conferência guardou.",
    };

    private static readonly Option<bool> QueueAgainOption = new("--again")
    {
        Description = "Confere de novo no VNDB os jogos que já foram conferidos.",
    };

    private static readonly Option<string?> TranslateNameOption = new("--name")
    {
        Description = "Nome do projeto (padrão: o título do jogo).",
        HelpName = "nome",
    };

    private static Command CreateQueue(VnfCli cli)
    {
        var command = new Command("queue", "Lista o que você tem e ainda não existe em português no VNDB, do texto mais curto ao mais longo.");
        command.Options.Add(QueueAllOption);
        command.Options.Add(QueueOfflineOption);
        command.Options.Add(QueueAgainOption);
        command.SetAction((parseResult, token) => QueueAsync(cli, parseResult, token));
        return command;
    }

    private static Command CreateTranslate(VnfCli cli)
    {
        var game = new Argument<string>("jogo") { Description = "Jogo da biblioteca (id, nome da pasta ou pedaço do título)." };
        var folder = new Argument<string?>("pasta")
        {
            Description = "Pasta do novo projeto (padrão: uma pasta com o título do jogo, na pasta atual).",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var command = new Command("translate", "Traduzir este jogo: cria o projeto, já com a ficha e os personagens do VNDB.");
        command.Arguments.Add(game);
        command.Arguments.Add(folder);
        command.Options.Add(TranslateNameOption);
        command.SetAction((parseResult, token) => TranslateAsync(cli, parseResult, parseResult.GetRequiredValue(game), parseResult.GetValue(folder), token));
        return command;
    }

    private static async Task<int> QueueAsync(VnfCli cli, ParseResult parseResult, CancellationToken token)
    {
        var offline = parseResult.GetValue(QueueOfflineOption);
        var again = parseResult.GetValue(QueueAgainOption);
        if (offline && again)
        {
            throw new UsageException("--again confere no VNDB e --offline não o consulta; use um dos dois.");
        }

        var output = parseResult.InvocationConfiguration.Output;
        var json = parseResult.GetValue(cli.Options.Json);
        var file = FileOf(cli);
        var library = file.Load();
        var waiting = offline ? 0 : library.Games.Count(g => PortugueseTranslationChecker.NeedsCheck(g, again));
        TranslationCheckRun? run = null;
        if (waiting > 0)
        {
            var checker = new PortugueseTranslationChecker(cli.CreateVndbCatalog(), cli.Clock);
            run = await checker.CheckAllAsync(library, again, json ? null : new CheckLines(parseResult.InvocationConfiguration.Error), game => KeepCheck(file, game), token).ConfigureAwait(false);
            library = file.Load();
        }

        var exitCode = run is { Failures.Count: > 0 } or { AbortReason: not null } ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode;
        var all = parseResult.GetValue(QueueAllOption);
        var entries = TranslationQueue.Build(library.Games, library.Privacy);
        var shown = all ? entries : [.. entries.Where(e => e.State == TranslationQueueState.Queued)];
        var queued = entries.Count(e => e.State == TranslationQueueState.Queued);
        if (json)
        {
            await output.WriteLineAsync(JsonEnvelope.Result(
                new
                {
                    total = library.Privacy.Listed(library.Games).Count,
                    queued,
                    shown = shown.Count,
                    checkedNow = run?.Checked.Count ?? 0,
                    incomplete = run?.Incomplete.Select(g => g.Id) ?? [],
                    failures = run?.Failures.Select(f => new { id = f.Game.Id, title = f.Game.Title, error = f.Message }) ?? [],
                    cancelled = run?.Cancelled ?? false,
                    abortReason = run?.AbortReason,
                    entries = shown.Select((e, i) => QueueData(e, i + 1)),
                },
                exitCode)).ConfigureAwait(false);
        }
        else
        {
            WriteQueue(cli, output, library, shown, all, queued, run);
        }

        token.ThrowIfCancellationRequested();
        return exitCode;
    }

    private static void WriteQueue(VnfCli cli, TextWriter output, GameLibrary library, IReadOnlyList<TranslationQueueEntry> shown, bool all, int queued, TranslationCheckRun? run)
    {
        var listed = library.Privacy.Listed(library.Games).Count;
        if (listed == 0)
        {
            output.WriteLine("A biblioteca está vazia. Rode vnf library scan <pasta> para reconhecer os seus jogos.");
            return;
        }

        if (run is { Cancelled: true })
        {
            output.WriteLine("Conferência cancelada. O que já foi conferido ficou na biblioteca; rode de novo para continuar.");
        }

        if (shown.Count > 0)
        {
            List<TableColumn> columns = [CliViews.Column("#", right: true), CliViews.Column("Id"), CliViews.Column("Título"), CliViews.Column("Português no VNDB"), CliViews.Column("Texto"), CliViews.Column("Engine")];
            if (all)
            {
                columns.Add(CliViews.Column("Situação"));
            }

            var table = CliViews.NewTable([.. columns]);
            for (var i = 0; i < shown.Count; i++)
            {
                var entry = shown[i];
                var length = LengthCell(entry.Game.Info);
                List<IRenderable> cells =
                [
                    new Markup(CliFormat.Number(i + 1)),
                    new Markup(entry.Game.Id),
                    new Markup(CliViews.Escape(entry.Game.Title)),
                    new Markup(CliViews.Escape(PortugueseCell(entry))),
                    new Markup(length is null ? EmptyCell : CliViews.Escape(length)),
                    new Markup(CliViews.Escape(entry.Game.Engine.Name)),
                ];
                if (all)
                {
                    cells.Add(new Markup(CliViews.Escape(StateText(entry.State))));
                }

                table.AddRow([.. cells]);
            }

            cli.Ansi.Write(table);
        }

        foreach (var failure in run?.Failures.Take(10) ?? [])
        {
            output.WriteLine($"Falhou: {failure.Game.Title}: {failure.Message}");
        }

        if (run?.AbortReason is { } reason)
        {
            output.WriteLine(reason);
        }

        if (run is { Incomplete.Count: > 0 })
        {
            output.WriteLine($"{CliFormat.Count(run.Incomplete.Count, "jogo tem", "jogos têm")} edições demais no VNDB para listar todas; não dá para afirmar que falta tradução. Veja no site: {string.Join(", ", run.Incomplete.Select(g => g.VndbId))}.");
        }

        output.WriteLine(queued == 0
            ? "Nada na fila: todo jogo da biblioteca já tem projeto, já está em português no VNDB ou é de uma engine que o VNForge ainda não traduz. Veja tudo com --all."
            : $"{CliFormat.Count(queued, "jogo na fila", "jogos na fila")} de {CliFormat.Number(listed)} na biblioteca.");
        if (queued > 0)
        {
            output.WriteLine("Para criar o projeto de um deles: vnf library translate <jogo>");
        }

        if (shown.Any(e => e.Portuguese is null && !e.Game.IsConfirmed))
        {
            output.WriteLine("Jogo com \"?\" não tem o jogo do VNDB confirmado, então não dá para saber se já existe em português. Confirme com vnf library match.");
        }

        output.WriteLine(VndbCredit.ShortText);
    }

    private static async Task<int> TranslateAsync(VnfCli cli, ParseResult parseResult, string reference, string? folderArgument, CancellationToken token)
    {
        var file = FileOf(cli);
        var library = file.Load();
        var game = library.Find(reference);
        var projectDir = Path.GetFullPath(folderArgument ?? LibraryTranslationStarter.SuggestedFolderName(game), cli.CurrentDirectory);
        var result = await LibraryTranslationStarter.StartAsync(
            library,
            game,
            projectDir,
            KnownEngines.All,
            cli.CreateVndbCatalog(),
            new ProjectInitOptions(Name: parseResult.GetValue(TranslateNameOption)),
            new PersonalGlossary(cli.UserConfigDirectory),
            token).ConfigureAwait(false);

        var linked = LinkCreatedProject(file, game, result.Project.Folder.Root);

        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            await output.WriteLineAsync(JsonEnvelope.Result(new
            {
                game = new { id = linked.Id, title = linked.Title, vndbId = linked.VndbId, project = linked.ProjectPath },
                project = new
                {
                    folder = result.Project.Folder.Root,
                    engine = result.Project.Engine.Id,
                    name = result.Project.Config.Name,
                    exe = result.Project.Config.Game.Exe,
                    personalTerms = result.Project.SeededTerms,
                    personalGlossaryProblem = result.Project.PersonalGlossaryProblem,
                },
                vndb = result.Vndb is null ? null : VndbCommand.Data(result.Vndb),
            })).ConfigureAwait(false);
            return ErrorHandler.OkExitCode;
        }

        await output.WriteLineAsync($"Projeto de {linked.Title} criado em {result.Project.Folder.Root}{Path.DirectorySeparatorChar}").ConfigureAwait(false);
        await output.WriteLineAsync($"Engine: {result.Project.Engine.DisplayName}. O jogo da biblioteca ficou ligado ao projeto.").ConfigureAwait(false);
        if (result.Vndb is { } link)
        {
            await output.WriteLineAsync($"Jogo do VNDB: {link.Title} ({link.VisualNovelId}), {CliFormat.Count(link.Characters.Count, "personagem", "personagens")}. Eles entram em characters.json no vnf extract.").ConfigureAwait(false);
            await output.WriteLineAsync(VndbCredit.ShortText).ConfigureAwait(false);
        }
        else
        {
            await output.WriteLineAsync($"Aviso: {linked.Title} não tem o jogo do VNDB confirmado, então o projeto nasceu sem sinopse e personagens. Confirme com vnf library match e ligue com vnf vndb link <id> --project <pasta>.").ConfigureAwait(false);
        }

        if (result.Project.SeededTerms > 0)
        {
            await output.WriteLineAsync($"Glossário pessoal: {CliFormat.Count(result.Project.SeededTerms, "termo entrou", "termos entraram")} no glossário do projeto.").ConfigureAwait(false);
        }

        if (result.Project.PersonalGlossaryProblem is { } problem)
        {
            await output.WriteLineAsync($"Aviso: {problem}").ConfigureAwait(false);
        }

        await output.WriteLineAsync($"Próximo passo: vnf extract --project {result.Project.Folder.Root}").ConfigureAwait(false);
        return ErrorHandler.OkExitCode;
    }

    /// <summary>
    /// O projeto já existe em disco: a ligação grava sobre a biblioteca como está agora, sem refazer o que outro processo mudou.
    /// Se não der para gravar, o projeto fica órfão e a mensagem diz onde ele está e como ligá-lo depois, porque rodar <c>translate</c> de novo recusaria a pasta.
    /// </summary>
    private static LibraryGame LinkCreatedProject(LibraryFile file, LibraryGame game, string projectRoot)
    {
        try
        {
            return file.Update(current =>
            {
                if (current.Games.All(g => g.Id != game.Id))
                {
                    throw new LibraryException("o jogo saiu da biblioteca nesse meio tempo");
                }

                return LibraryProjectLinker.Link(current, current.Find(game.Id), projectRoot);
            });
        }
        catch (Exception e) when (e is LibraryException or IOException or UnauthorizedAccessException)
        {
            throw new LibraryException(
                $"O projeto foi criado em {projectRoot}{Path.DirectorySeparatorChar}, mas o jogo da biblioteca não ficou ligado a ele: {e.Message.TrimEnd('.')}. Quando der, ligue com: vnf library link {game.Id} --project \"{projectRoot}\"",
                e);
        }
    }

    /// <summary>Grava a conferência de um jogo sobre a biblioteca como está agora no disco, se o jogo ainda é o mesmo do VNDB, sem refazer o que outro processo mudou.</summary>
    private static void KeepCheck(LibraryFile file, LibraryGame game) =>
        file.Update(current =>
        {
            if (current.Games.FirstOrDefault(g => g.Id == game.Id) is { } now && now.VndbId == game.VndbId)
            {
                current.Replace(now with { Portuguese = game.Portuguese });
            }
        });

    private static string PortugueseCell(TranslationQueueEntry entry) =>
        entry.Portuguese switch
        {
            PortugueseStatus.None => "nenhuma",
            PortugueseStatus.MachineOnly => "só automática",
            PortugueseStatus.Partial => "parcial",
            PortugueseStatus.Complete => "completa",
            _ => entry.Game.IsConfirmed ? "? (não conferido)" : "? (sem casamento)",
        };

    private static string StateText(TranslationQueueState state) =>
        state switch
        {
            TranslationQueueState.Queued => "na fila",
            TranslationQueueState.InProgress => "com projeto",
            TranslationQueueState.AlreadyTranslated => "já em português",
            _ => "engine sem suporte",
        };

    /// <summary>A duração do VNDB em poucas palavras, para caber na tabela; nulo quando o VNDB não tem nenhuma.</summary>
    private static string? LengthCell(LibraryVndbInfo? info)
    {
        if (info?.LengthMinutes is > 0 and var minutes)
        {
            return minutes >= 60 ? $"~{(minutes / 60.0).ToString("0", PortugueseBrazil)} h" : $"{minutes} min";
        }

        return info?.Length switch
        {
            1 => "< 2 h",
            2 => "2 a 10 h",
            3 => "10 a 30 h",
            4 => "30 a 50 h",
            5 => "> 50 h",
            _ => null,
        };
    }

    private static object QueueData(TranslationQueueEntry entry, int position) => new
    {
        position,
        state = Camel(entry.State),
        portuguese = entry.Game.CurrentPortuguese is { } found
            ? new { status = Camel(found.Status), checkedAt = found.CheckedAt, releases = found.ReleaseIds }
            : null,
        approximateMinutes = entry.ApproximateMinutes,
        game = new
        {
            id = entry.Game.Id,
            title = entry.Game.Title,
            folder = entry.Game.Folder,
            vndbId = entry.Game.VndbId,
            engine = new { id = entry.Game.Engine.Id, name = entry.Game.Engine.Name, translatable = entry.Game.Engine.Translatable },
            project = entry.Game.ProjectPath,
            lengthMinutes = entry.Game.Info?.LengthMinutes,
            length = entry.Game.Info?.Length,
        },
    };

    private static string Camel<T>(T value)
        where T : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private sealed class CheckLines(TextWriter error) : IProgress<TranslationCheckProgress>
    {
        public void Report(TranslationCheckProgress value) =>
            error.WriteLine($"{CliFormat.Number(value.Done)}/{CliFormat.Number(value.Total)}  {value.Game.Title}");
    }
}
