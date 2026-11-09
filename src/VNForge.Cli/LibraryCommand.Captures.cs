using System.CommandLine;
using Spectre.Console;
using VNForge.Core.Library;
using VNForge.Core.Logging;
using VNForge.Core.Output;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf library notes</c> e <c>vnf library capture take|list|remove</c>: as notas livres de cada jogo (<c>library-notes.json</c>, na pasta de configuração) e as
/// capturas da tela do jogo (imagens PNG e índice na pasta de dados do usuário, nunca no repositório nem na pasta do jogo).
/// </summary>
internal static partial class LibraryCommand
{
    private static readonly Option<string?> NotesSetOption = new("--set")
    {
        Description = "Troca a nota pelo texto dado. Com -, lê o texto da entrada padrão (para uma nota de várias linhas). Texto em branco apaga a nota.",
        HelpName = "texto",
    };

    private static readonly Option<string?> NotesAddOption = new("--add")
    {
        Description = "Acrescenta o texto numa linha nova ao fim da nota. Com -, lê o texto da entrada padrão.",
        HelpName = "texto",
    };

    private static readonly Option<bool> NotesClearOption = new("--clear")
    {
        Description = "Apaga a nota do jogo.",
    };

    private static readonly Option<string?> CaptureTitleOption = new("--title")
    {
        Description = "Acha a janela por um pedaço do título dela (sem diferenciar maiúsculas), em qualquer programa, em vez de procurar na pasta do jogo.",
        HelpName = "título",
    };

    private static readonly Option<string?> CaptureNoteOption = new("--note")
    {
        Description = "Uma anotação para a captura (de que cena é, por exemplo).",
        HelpName = "texto",
    };

    private static Command CreateNotes(VnfCli cli)
    {
        var game = new Argument<string>("jogo") { Description = "Jogo da biblioteca (id, nome da pasta ou pedaço do título)." };
        var command = new Command("notes", "Mostra ou muda as notas livres de um jogo. Ficam só neste computador e não vão para o VNDB.");
        command.Arguments.Add(game);
        command.Options.Add(NotesSetOption);
        command.Options.Add(NotesAddOption);
        command.Options.Add(NotesClearOption);
        command.SetAction(parseResult => Notes(cli, parseResult, parseResult.GetRequiredValue(game)));
        return command;
    }

    private static Command CreateCapture(VnfCli cli)
    {
        static Argument<string> NewGame() => new("jogo") { Description = "Jogo da biblioteca (id, nome da pasta ou pedaço do título)." };
        var command = new Command("capture", "Capturas da tela do jogo, guardadas com a data na pasta de dados do usuário.");

        var takeGame = NewGame();
        var take = new Command("take", "Captura a janela do jogo aberto e guarda o PNG com a data no nome.");
        take.Arguments.Add(takeGame);
        take.Options.Add(CaptureTitleOption);
        take.Options.Add(CaptureNoteOption);
        take.SetAction(parseResult => CaptureTake(cli, parseResult, parseResult.GetRequiredValue(takeGame)));
        command.Subcommands.Add(take);

        var listGame = NewGame();
        var list = new Command("list", "Lista as capturas de um jogo, da mais antiga para a mais nova, com o caminho de cada imagem.");
        list.Arguments.Add(listGame);
        list.SetAction(parseResult => CaptureList(cli, parseResult, parseResult.GetRequiredValue(listGame)));
        command.Subcommands.Add(list);

        var removeGame = NewGame();
        var removeFile = new Argument<string>("arquivo") { Description = "O nome do arquivo da captura, como o vnf library capture list mostra (aaaammdd-hhmmss.png)." };
        var remove = new Command("remove", "Apaga uma captura: tira do índice e apaga a imagem.");
        remove.Arguments.Add(removeGame);
        remove.Arguments.Add(removeFile);
        remove.SetAction(parseResult => CaptureRemove(cli, parseResult, parseResult.GetRequiredValue(removeGame), parseResult.GetRequiredValue(removeFile)));
        command.Subcommands.Add(remove);
        return command;
    }

    private static int Notes(VnfCli cli, ParseResult parseResult, string reference)
    {
        var set = parseResult.GetValue(NotesSetOption);
        var add = parseResult.GetValue(NotesAddOption);
        var clear = parseResult.GetValue(NotesClearOption);
        if ((set is null ? 0 : 1) + (add is null ? 0 : 1) + (clear ? 1 : 0) > 1)
        {
            throw new UsageException("Use só uma das opções --set, --add e --clear.");
        }

        var game = FileOf(cli).Load().Find(reference);
        var file = new GameNotesFile(cli.UserConfigDirectory);
        var editing = set is not null || add is not null || clear;
        var text = set ?? add;
        if (text == "-")
        {
            text = cli.Input.ReadToEnd();
        }

        GameNote? note;
        var changed = false;
        if (!editing)
        {
            note = file.Load().Of(game.Id);
        }
        else
        {
            var now = cli.Clock.GetLocalNow();
            (note, changed) = file.Update<(GameNote?, bool)>(notes =>
            {
                var before = notes.Of(game.Id);
                GameNote? after;
                if (clear)
                {
                    notes.Clear(game.Id);
                    after = null;
                }
                else if (set is not null)
                {
                    after = notes.Set(game.Id, text, now);
                }
                else
                {
                    after = notes.Append(game.Id, text, now);
                }

                if (before is not null && after is not null && before.Text == after.Text)
                {
                    // Gravar o mesmo texto não é uma mudança: a data da nota fica como estava.
                    notes.Set(game.Id, before.Text, before.UpdatedAt);
                    return (before, false);
                }

                return (after, before?.Text != after?.Text);
            });
        }

        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                game = new { id = game.Id, title = game.Title },
                note = note is null ? null : new { text = note.Text, updatedAt = note.UpdatedAt },
                changed,
                file = file.Path,
            }));
            return ErrorHandler.OkExitCode;
        }

        if (editing)
        {
            output.WriteLine(changed
                ? $"{game.Title}: {(note is null ? "nota apagada." : "nota gravada.")}"
                : $"{game.Title}: a nota já estava assim.");
        }

        if (note is null)
        {
            if (!editing)
            {
                output.WriteLine($"{game.Title}: sem notas. Escreva uma com vnf library notes {game.Id} --set texto.");
            }
        }
        else
        {
            output.WriteLine($"{game.Title}, nota de {note.UpdatedAt.ToString("g", PortugueseBrazil)}:");
            output.WriteLine(note.Text);
        }

        return ErrorHandler.OkExitCode;
    }

    private static int CaptureTake(VnfCli cli, ParseResult parseResult, string reference)
    {
        var game = FileOf(cli).Load().Find(reference);
        var title = parseResult.GetValue(CaptureTitleOption);
        var target = title is null ? new WindowTarget(Folder: game.Folder) : new WindowTarget(TitleContains: title);
        var image = cli.WindowCapturer.Capture(target);
        var store = new GameCapturesFile(cli.UserCapturesDirectory, cli.Clock);
        var capture = store.Save(game.Id, image, parseResult.GetValue(CaptureNoteOption));
        var path = store.PathOf(game.Id, capture);

        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { game = new { id = game.Id, title = game.Title }, capture = CaptureData(capture, path) }));
        }
        else
        {
            output.WriteLine($"{game.Title}: captura de {capture.Width}x{capture.Height} gravada em {path}.");
        }

        return ErrorHandler.OkExitCode;
    }

    private static int CaptureList(VnfCli cli, ParseResult parseResult, string reference)
    {
        var game = FileOf(cli).Load().Find(reference);
        var store = new GameCapturesFile(cli.UserCapturesDirectory, cli.Clock);
        var rows = store.Load().Of(game.Id).Select(c => (Capture: c, Path: store.PathOf(game.Id, c))).ToList();

        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                game = new { id = game.Id, title = game.Title },
                folder = Path.Combine(store.Directory, game.Id),
                captures = rows.Select(r => CaptureData(r.Capture, r.Path)),
            }));
            return ErrorHandler.OkExitCode;
        }

        if (rows.Count == 0)
        {
            output.WriteLine($"{game.Title}: sem capturas. Abra o jogo e use vnf library capture take {game.Id}.");
            return ErrorHandler.OkExitCode;
        }

        var table = CliViews.NewTable(CliViews.Column("Data"), CliViews.Column("Tamanho", right: true), CliViews.Column("Arquivo"), CliViews.Column("Nota"));
        foreach (var (capture, path) in rows)
        {
            var file = File.Exists(path) ? CliViews.Escape(capture.File) : $"{CliViews.Escape(capture.File)} [red](a imagem sumiu)[/]";
            table.AddRow(
                new Markup(capture.CapturedAt.ToString("g", PortugueseBrazil)),
                new Markup($"{capture.Width}x{capture.Height}"),
                new Markup(file),
                new Markup(capture.Note is null ? "[dim]·[/]" : CliViews.Escape(capture.Note)));
        }

        output.WriteLine($"{game.Title}: {CliFormat.Count(rows.Count, "captura", "capturas")} em {Path.Combine(store.Directory, game.Id)}.");
        cli.Ansi.Write(table);
        return ErrorHandler.OkExitCode;
    }

    private static int CaptureRemove(VnfCli cli, ParseResult parseResult, string reference, string file)
    {
        var game = FileOf(cli).Load().Find(reference);
        var store = new GameCapturesFile(cli.UserCapturesDirectory, cli.Clock);
        var removed = store.Remove(game.Id, file)
            ?? throw new LibraryException($"{game.Title} não tem a captura '{file}'. Veja as capturas com vnf library capture list {game.Id}.");

        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { game = new { id = game.Id, title = game.Title }, removed = removed.File }));
        }
        else
        {
            output.WriteLine($"{game.Title}: captura {removed.File} apagada.");
        }

        return ErrorHandler.OkExitCode;
    }

    private static object CaptureData(GameCapture capture, string path) => new
    {
        file = capture.File,
        path,
        capturedAt = capture.CapturedAt,
        width = capture.Width,
        height = capture.Height,
        note = capture.Note,
        exists = File.Exists(path),
    };
}
