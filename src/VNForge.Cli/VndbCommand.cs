using System.CommandLine;
using System.Globalization;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf vndb search|link|show</c>: o jogo do VNDB do projeto. <c>search</c> faz na linha de comando o que o passo "Ficha do VNDB" do assistente faz na tela,
/// <c>link</c> liga um projeto que já existe (o <c>vnf init --vndb</c> liga na criação) e <c>show</c> mostra o que está ligado.
/// </summary>
internal static class VndbCommand
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    public static Command Create(VnfCli cli)
    {
        var command = new Command("vndb", "Liga o projeto ao jogo do VNDB: sinopse e personagens com romaji, apelidos e gênero.");
        command.Subcommands.Add(CreateSearch(cli));
        command.Subcommands.Add(CreateLink(cli));
        command.Subcommands.Add(CreateShow(cli));
        return command;
    }

    /// <summary>Os dados do jogo do VNDB como o <c>--json</c> os entrega.</summary>
    internal static object Data(VndbLink link, VndbApplyResult? applied = null) => new
    {
        id = link.VisualNovelId,
        title = link.Title,
        original = link.OriginalTitle,
        synopsis = link.Synopsis,
        characters = link.Characters.Count,
        charactersTruncated = link.CharactersTruncated,
        matched = applied?.Matched,
        added = applied?.Added,
        filled = applied?.Filled,
    };

    private static Command CreateSearch(VnfCli cli)
    {
        var title = new Argument<string?>("titulo")
        {
            Description = "Título a buscar (padrão: o título da janela do jogo, com --game).",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var game = new Option<string?>("--game")
        {
            Description = "Pasta do jogo: lê o título da janela (yscfg.ybn) e usa o nome da pasta para confirmar o resultado.",
            HelpName = "pasta",
        };
        var limit = new Option<int>("--limit")
        {
            Description = "Quantos resultados mostrar (de 1 a 20; padrão 5).",
            HelpName = "n",
            DefaultValueFactory = _ => 5,
        };
        var command = new Command("search", "Busca o jogo no VNDB pelo título e diz quais resultados combinam com a pasta do jogo.");
        command.Arguments.Add(title);
        command.Options.Add(game);
        command.Options.Add(limit);
        command.SetAction((parseResult, token) => SearchAsync(cli, parseResult, parseResult.GetValue(title), parseResult.GetValue(game), parseResult.GetValue(limit), token));
        return command;
    }

    private static Command CreateLink(VnfCli cli)
    {
        var id = new Argument<string>("id") { Description = "Id do jogo no VNDB, por exemplo v17 (vnf vndb search mostra os ids)." };
        var command = new Command("link", "Liga o projeto ao jogo do VNDB e leva os personagens que casam para o characters.json.");
        command.Arguments.Add(id);
        command.SetAction((parseResult, token) => LinkAsync(cli, parseResult, parseResult.GetRequiredValue(id), token));
        return command;
    }

    private static Command CreateShow(VnfCli cli)
    {
        var command = new Command("show", "Mostra o jogo do VNDB ligado ao projeto.");
        command.SetAction(parseResult => Show(cli, parseResult));
        return command;
    }

    private static async Task<int> SearchAsync(VnfCli cli, ParseResult parseResult, string? title, string? gameArgument, int limit, CancellationToken token)
    {
        string? folderName = null;
        string? exe = null;
        if (gameArgument is not null)
        {
            var gameDir = Path.GetFullPath(gameArgument, cli.CurrentDirectory);
            var inspection = ProjectInitializer.Inspect(gameDir, KnownEngines.All);
            title ??= ProjectInitializer.ReadGameTitle(inspection, gameDir);
            folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(gameDir));
            exe = inspection.Executables.Count == 1 ? inspection.Executables[0] : null;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new UsageException(gameArgument is null
                ? "Informe o título a buscar, ou --game com a pasta do jogo."
                : "O jogo não traz o título da janela (yscfg.ybn). Informe o título a buscar.");
        }

        var candidates = await VndbProjectLinker.FindCandidatesAsync(cli.CreateVndbCatalog(), title.Trim(), folderName, exe, Math.Clamp(limit, 1, 20), token);
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                query = title.Trim(),
                results = candidates.Select(c => new
                {
                    id = c.VisualNovel.Id,
                    title = c.VisualNovel.Title,
                    original = c.VisualNovel.AltTitle,
                    released = c.VisualNovel.Released,
                    matchesFolder = c.Evidence == VndbEvidence.FolderAndExe,
                }),
            }));
            return ErrorHandler.OkExitCode;
        }

        if (candidates.Count == 0)
        {
            output.WriteLine($"O VNDB não achou jogo com o título “{title.Trim()}”. Tente outro título.");
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"{candidates.Count} {(candidates.Count == 1 ? "jogo achado" : "jogos achados")} no VNDB para “{title.Trim()}”:");
        foreach (var candidate in candidates)
        {
            output.WriteLine(Describe(candidate));
        }

        output.WriteLine("Para ligar o projeto a um deles: vnf vndb link <id> --project <pasta>");
        output.WriteLine(VndbCredit.ShortText);
        return ErrorHandler.OkExitCode;
    }

    private static async Task<int> LinkAsync(VnfCli cli, ParseResult parseResult, string id, CancellationToken token)
    {
        var project = Open(cli, parseResult);
        var link = await VndbProjectLinker.BuildAsync(cli.CreateVndbCatalog(), id.Trim(), token);
        project.SaveVndbLink(link);
        var applied = VndbProjectLinker.ApplyCharacters(project);
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(Data(link, applied)));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Projeto ligado a {link.Title} ({link.VisualNovelId}): {link.Characters.Count} personagens no VNDB.");
        output.WriteLine(applied.Matched > 0
            ? $"{applied.Matched} casaram com os nomes do jogo: {applied.Added} entraram em characters.json e {applied.Filled} ganharam campos que faltavam."
            : "Nenhum personagem casou com os nomes do jogo ainda. Os nomes vêm do vnf extract, que leva os personagens do VNDB para o characters.json.");
        if (link.CharactersTruncated)
        {
            output.WriteLine("Aviso: o jogo tem mais personagens no VNDB do que o limite de leitura; os últimos ficaram de fora.");
        }

        output.WriteLine(VndbCredit.ShortText);

        return ErrorHandler.OkExitCode;
    }

    private static int Show(VnfCli cli, ParseResult parseResult)
    {
        var project = Open(cli, parseResult);
        var link = project.LoadVndbLink() ?? throw new UsageException("O projeto não está ligado a um jogo do VNDB. Use vnf vndb link <id>.");
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(Data(link)));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Jogo: {link.Title}{(link.OriginalTitle is null ? string.Empty : $" ({link.OriginalTitle})")} · {link.VisualNovelId}");
        output.WriteLine($"Personagens no VNDB: {link.Characters.Count}");
        output.WriteLine(link.Synopsis is null ? "Sinopse: (o VNDB não tem)" : $"Sinopse: {link.Synopsis}");
        output.WriteLine(VndbCredit.ShortText);
        return ErrorHandler.OkExitCode;
    }

    private static string Describe(VndbCandidate candidate)
    {
        var vn = candidate.VisualNovel;
        var parts = new List<string> { vn.Id, vn.Title };
        if (vn.AltTitle is not null)
        {
            parts.Add($"({vn.AltTitle})");
        }

        var line = string.Join(' ', parts);
        var year = vn.Released is { Length: >= 4 } released ? released[..4] : null;
        var details = new List<string>();
        if (year is not null)
        {
            details.Add(year);
        }

        if (vn.LengthMinutes is { } minutes)
        {
            details.Add($"{(minutes / 60.0).ToString("0.#", PortugueseBrazil)} h");
        }

        details.Add(candidate.Evidence == VndbEvidence.FolderAndExe ? "combina com a pasta e o exe" : "só o título");
        return $"  {line} · {string.Join(" · ", details)}";
    }

    private static ProjectFolder Open(VnfCli cli, ParseResult parseResult) =>
        ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
}
