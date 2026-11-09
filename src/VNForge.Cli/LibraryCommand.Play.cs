using System.CommandLine;
using VNForge.Core.Library;
using VNForge.Core.Logging;
using VNForge.Core.Output;

namespace VNForge.Cli;

/// <summary><c>vnf library play|option|emulator</c>: abrir o jogo da biblioteca contando o tempo, e os ajustes que mudam como ele abre.</summary>
internal static partial class LibraryCommand
{
    private static readonly Option<string?> LocaleOption = new("--locale")
    {
        Description = "Abre o jogo pelo Locale Emulator com esta localidade, só nesta chamada. Só existe ja (japonês).",
        HelpName = "idioma",
    };

    private static readonly Option<bool> RecentOption = new("--recent")
    {
        Description = "Ordena pelo último jogado (quem nunca foi jogado vem depois, por título), em vez de só por título.",
    };

    private static readonly Option<string?> JapaneseLocaleOption = new("--japanese-locale")
    {
        Description = "O jogo pede a localidade japonesa: o Jogar abre pelo Locale Emulator. sim ou nao.",
        HelpName = "sim|nao",
    };

    private static readonly Option<bool> ClearOption = new("--clear")
    {
        Description = "Esquece o caminho do Locale Emulator.",
    };

    private static Command CreatePlay(VnfCli cli)
    {
        var game = new Argument<string>("jogo") { Description = "Jogo da biblioteca (id, nome da pasta ou pedaço do título)." };
        var command = new Command("play", "Abre o jogo e conta o tempo de jogo, até o processo e os filhos dele fecharem.");
        command.Arguments.Add(game);
        command.Options.Add(LocaleOption);
        command.SetAction((parseResult, token) => PlayAsync(cli, parseResult, parseResult.GetRequiredValue(game), token));
        return command;
    }

    private static Command CreateOption(VnfCli cli)
    {
        var game = new Argument<string>("jogo") { Description = "Jogo da biblioteca (id, nome da pasta ou pedaço do título)." };
        var command = new Command("option", "Mostra ou muda como o Jogar abre um jogo (--japanese-locale).");
        command.Arguments.Add(game);
        command.Options.Add(JapaneseLocaleOption);
        command.SetAction(parseResult => SetOption(cli, parseResult, parseResult.GetRequiredValue(game)));
        return command;
    }

    private static Command CreateEmulator(VnfCli cli)
    {
        var path = new Argument<string?>("caminho")
        {
            Description = "O LEProc.exe do Locale Emulator, ou a pasta onde ele está. Sem ele, mostra o caminho atual.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var command = new Command("emulator", "Mostra ou muda onde está o Locale Emulator, usado pelos jogos que pedem localidade japonesa.");
        command.Arguments.Add(path);
        command.Options.Add(ClearOption);
        command.SetAction(parseResult => Emulator(cli, parseResult, parseResult.GetValue(path)));
        return command;
    }

    private static async Task<int> PlayAsync(VnfCli cli, ParseResult parseResult, string reference, CancellationToken token)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var json = parseResult.GetValue(cli.Options.Json);
        var library = FileOf(cli).Load();
        var game = library.Find(reference);
        var launch = GameLaunchPlanner.Plan(library, game, ParseLocale(parseResult.GetValue(LocaleOption)));

        if (!json)
        {
            var how = launch.ViaLocaleEmulator ? " pelo Locale Emulator" : string.Empty;
            parseResult.InvocationConfiguration.Error.WriteLine($"Abrindo {game.Title}{how}. O tempo conta até o jogo fechar; Ctrl+C para de contar e deixa o jogo aberto.");
        }

        var file = new PlayLogFile(cli.UserConfigDirectory, cli.Clock);
        WriteWarnings(parseResult, file, file.Load());
        var error = parseResult.InvocationConfiguration.Error;
        var player = new GamePlayer(cli.GameLauncher, file, cli.Clock, cli.PlayTracking, message => error.WriteLine($"Aviso: {message}"));
        var session = await player.PlayAsync(game.Id, launch, token).ConfigureAwait(false);
        var stats = TotalsAfter(file, game.Id, session);

        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                game = GameData(game, library.Privacy, stats),
                launch = new { executable = launch.Executable, gameExecutable = launch.GameExecutable, viaLocaleEmulator = launch.ViaLocaleEmulator },
                session = new { startedAt = session.StartedAt, endedAt = session.EndedAt, seconds = (long)session.Duration.TotalSeconds, text = PlayTime.Format(session.Duration), interrupted = session.Interrupted },
                file = file.Path,
            }));
        }
        else
        {
            output.WriteLine(session.Interrupted
                ? $"{game.Title}: contagem interrompida em {PlayTime.Format(session.Duration)}; o jogo continua aberto."
                : $"{game.Title}: o jogo fechou. Sessão de {PlayTime.Format(session.Duration)}.");
            output.WriteLine($"Total: {PlayTime.Format(stats.Total)} em {CliFormat.Count(stats.Sessions, "sessão", "sessões")}.");
        }

        token.ThrowIfCancellationRequested();
        return ErrorHandler.OkExitCode;
    }

    private static bool? ParseLocale(string? text)
    {
        if (text is null)
        {
            return null;
        }

        return text.Trim().ToLowerInvariant() is "ja" or "ja-jp"
            ? true
            : throw new UsageException($"--locale aceita ja (japonês), não '{text}'.");
    }

    private static int SetOption(VnfCli cli, ParseResult parseResult, string reference)
    {
        var wanted = ParseYesNo(parseResult.GetValue(JapaneseLocaleOption), "--japanese-locale");
        var file = FileOf(cli);
        var (library, game, changed) = wanted is { } value
            ? file.Update(current =>
            {
                var found = current.Find(reference);
                var differs = value != found.RequiresJapaneseLocale;
                if (differs)
                {
                    found = found with { RequiresJapaneseLocale = value };
                    current.Replace(found);
                }

                return (current, found, differs);
            })
            : Show(file.Load(), reference);

        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { game = GameData(game, library.Privacy), changed }));
        }
        else
        {
            output.WriteLine($"{game.Title}: localidade japonesa {(game.RequiresJapaneseLocale ? "ligada (abre pelo Locale Emulator)" : "desligada")}.{(changed ? " Gravado." : string.Empty)}");
        }

        return ErrorHandler.OkExitCode;

        static (GameLibrary, LibraryGame, bool) Show(GameLibrary current, string reference) => (current, current.Find(reference), false);
    }

    private static int Emulator(VnfCli cli, ParseResult parseResult, string? path)
    {
        var clear = parseResult.GetValue(ClearOption);
        if (clear && path is not null)
        {
            throw new UsageException("--clear não leva um caminho.");
        }

        var file = FileOf(cli);
        var resolved = path is null ? null : GameLaunchPlanner.ResolveLocaleEmulator(Path.GetFullPath(path, cli.CurrentDirectory));
        var (current, changed) = resolved is null && !clear
            ? (file.Load().LocaleEmulatorPath, false)
            : file.Update(library =>
            {
                var next = clear ? null : resolved;
                var differs = !string.Equals(library.LocaleEmulatorPath, next, StringComparison.Ordinal);
                library.LocaleEmulatorPath = next;
                return (next, differs);
            });

        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { localeEmulator = current, changed }));
        }
        else
        {
            output.WriteLine(current is not null
                ? $"Locale Emulator: {current}.{(changed ? " Gravado." : string.Empty)}"
                : $"Locale Emulator: não configurado.{(changed ? " Caminho esquecido." : " Informe com vnf library emulator <pasta ou LEProc.exe>.")}");
        }

        return ErrorHandler.OkExitCode;
    }

    private static void WriteWarnings(ParseResult parseResult, PlayLogFile file, PlayLog played)
    {
        var error = parseResult.InvocationConfiguration.Error;
        foreach (var warning in played.Warnings)
        {
            error.WriteLine($"Aviso: {warning}");
        }

        if (file.BackUpDiscarded(played) is { } copy)
        {
            error.WriteLine($"Aviso: cópia do arquivo original, com as sessões descartadas: {copy}");
        }
    }

    // Se a gravação da sessão falhou, o arquivo pode estar ilegível ou preso: o total mostrado vira só o desta sessão, já avisado.
    private static PlayStats TotalsAfter(PlayLogFile file, string gameId, PlaySession session)
    {
        try
        {
            return file.Load().StatsOf(gameId);
        }
        catch (LibraryException)
        {
            return new PlayStats(session.Duration, session.EndedAt, 1);
        }
    }
    private static object PlayData(PlayStats stats) => new
    {
        totalSeconds = (long)stats.Total.TotalSeconds,
        total = PlayTime.Format(stats.Total),
        lastPlayedAt = stats.LastPlayedAt,
        sessions = stats.Sessions,
    };

    private static string LastPlayedText(PlayStats stats) =>
        stats.LastPlayedAt is { } last ? last.ToLocalTime().ToString("g", PortugueseBrazil) : "nunca";
}
