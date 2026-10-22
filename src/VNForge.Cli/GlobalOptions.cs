using System.CommandLine;

namespace VNForge.Cli;

/// <summary>Valores das opções globais de uma invocação.</summary>
public sealed record GlobalSettings(string? Project, bool Json, bool Verbose, bool Quiet, bool Yes);

/// <summary>Opções que valem para qualquer comando: <c>--project</c>, <c>--json</c>, <c>--verbose</c>, <c>--quiet</c> e <c>--yes</c>.</summary>
public sealed class GlobalOptions
{
    public GlobalOptions()
    {
        Project = new Option<string?>("--project")
        {
            Description = "Pasta do projeto (padrão: a pasta atual).",
            HelpName = "pasta",
            Recursive = true,
        };
        Json = new Option<bool>("--json")
        {
            Description = "Escreve o resultado e os erros em JSON, para automação.",
            Recursive = true,
        };
        Verbose = new Option<bool>("--verbose")
        {
            Description = "Grava mais detalhes no log (nível Debug).",
            Recursive = true,
        };
        Quiet = new Option<bool>("--quiet")
        {
            Description = "Grava só avisos e erros no log.",
            Recursive = true,
        };
        Yes = new Option<bool>("--yes")
        {
            Description = "Responde sim às confirmações, para uso em scripts.",
            Recursive = true,
        };
    }

    public Option<string?> Project { get; }

    public Option<bool> Json { get; }

    public Option<bool> Verbose { get; }

    public Option<bool> Quiet { get; }

    public Option<bool> Yes { get; }

    public void AddTo(Command command)
    {
        command.Options.Add(Project);
        command.Options.Add(Json);
        command.Options.Add(Verbose);
        command.Options.Add(Quiet);
        command.Options.Add(Yes);
    }

    public GlobalSettings Read(ParseResult parseResult) => new(
        parseResult.GetValue(Project),
        parseResult.GetValue(Json),
        parseResult.GetValue(Verbose),
        parseResult.GetValue(Quiet),
        parseResult.GetValue(Yes));
}
