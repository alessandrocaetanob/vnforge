using System.CommandLine;
using System.Globalization;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary><c>vnf status</c>: progresso do projeto por status, por arquivo e por personagem, lido de <c>text/*.json</c>.</summary>
internal static class StatusCommand
{
    private const string NoSpeakerLabel = "(sem falante)";

    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    public static Command Create(VnfCli cli)
    {
        var command = new Command("status", "Mostra o progresso da tradução: linhas por status, por arquivo e por personagem.");
        command.SetAction(parseResult => Run(cli, parseResult, parseResult.InvocationConfiguration.Output));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult, TextWriter output)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var status = ProjectStatusReporter.Create(project);

        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                total = Data(status.Total),
                files = status.Files.Select(f => Data(f.Counts, "key", f.Key)),
                characters = status.Characters.Select(c => Data(c.Counts, "speaker", c.Speaker)),
            }));
            return ErrorHandler.OkExitCode;
        }

        var total = status.Total;
        output.WriteLine($"{Number(total.Total)} {(total.Total == 1 ? "linha" : "linhas")}; {Percent(total)} com tradução.");
        WriteCounts(output, total, "  ");

        output.WriteLine();
        output.WriteLine("Por arquivo:");
        foreach (var file in status.Files)
        {
            output.WriteLine($"  {file.Key}: {Line(file.Counts)}");
        }

        output.WriteLine();
        output.WriteLine("Por personagem:");
        foreach (var character in status.Characters)
        {
            output.WriteLine($"  {character.Speaker ?? NoSpeakerLabel}: {Line(character.Counts)}");
        }

        return ErrorHandler.OkExitCode;
    }

    private static Dictionary<string, object?> Data(StatusCounts counts, string? keyName = null, string? keyValue = null)
    {
        var data = new Dictionary<string, object?>();
        if (keyName is not null)
        {
            data[keyName] = keyValue;
        }

        data["lines"] = counts.Total;
        data["untranslated"] = counts.Untranslated;
        data["machine"] = counts.Machine;
        data["translated"] = counts.Translated;
        data["reviewed"] = counts.Reviewed;
        data["approved"] = counts.Approved;
        data["percent"] = counts.Percent;
        return data;
    }

    private static void WriteCounts(TextWriter output, StatusCounts counts, string indent)
    {
        output.WriteLine($"{indent}sem tradução: {Number(counts.Untranslated)}");
        output.WriteLine($"{indent}máquina: {Number(counts.Machine)}");
        output.WriteLine($"{indent}traduzida: {Number(counts.Translated)}");
        output.WriteLine($"{indent}revisada: {Number(counts.Reviewed)}");
        output.WriteLine($"{indent}aprovada: {Number(counts.Approved)}");
    }

    private static string Line(StatusCounts c) =>
        $"{Number(c.Total)} {(c.Total == 1 ? "linha" : "linhas")} ({Percent(c)}): "
        + $"{Number(c.Untranslated)} sem tradução, {Number(c.Machine)} máquina, {Number(c.Translated)} traduzida(s), {Number(c.Reviewed)} revisada(s), {Number(c.Approved)} aprovada(s)";

    private static string Percent(StatusCounts c) => string.Create(PortugueseBrazil, $"{c.Percent:0.#}%");

    private static string Number(int value) => value.ToString("N0", PortugueseBrazil);
}
