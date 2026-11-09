using System.CommandLine;
using System.Globalization;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Publishing;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf release</c>: o patch de diferença binária e o instalador de um arquivo para quem vai jogar (#540, c15), com o leia-me, os créditos e a lista do que
/// mudou desde o patch anterior. Não grava nada na pasta do jogo: só lê os originais e escreve na pasta de saída e em <c>releases/</c>.
/// </summary>
internal static class ReleaseCommand
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly Argument<string> VersionArgument = new("versão")
    {
        Description = "Número da versão do patch, como 1.2.0.",
    };

    private static readonly Option<string?> OutDirOption = new("--out-dir")
    {
        Description = "Pasta de saída (padrão: releases/<versão> no projeto).",
        HelpName = "pasta",
    };

    private static readonly Option<string?> CreditsOption = new("--credits")
    {
        Description = "Linha de créditos para o CREDITOS.txt, o leia-me e o instalador.",
        HelpName = "texto",
    };

    private static readonly Option<string?> NameOption = new("--name")
    {
        Description = "Nome do instalador, sem extensão (padrão: <jogo>-<idioma>-<versão>).",
        HelpName = "nome",
    };

    private static readonly Option<string?> InstallerOption = new("--installer")
    {
        Description = "Executável do instalador que recebe o patch (padrão: vnf-instalador, ao lado do vnf).",
        HelpName = "arquivo",
    };

    private static readonly Option<bool> AllowWarningsOption = new("--allow-warnings")
    {
        Description = "Lança mesmo com avisos da Conferência; erros continuam travando e saem com 2.",
    };

    public static Command Create(VnfCli cli)
    {
        var command = new Command("release", "Gera o patch e o instalador de um arquivo para os jogadores, com leia-me, créditos e mudanças.");
        command.Arguments.Add(VersionArgument);
        command.Options.Add(OutDirOption);
        command.Options.Add(CreditsOption);
        command.Options.Add(NameOption);
        command.Options.Add(InstallerOption);
        command.Options.Add(AllowWarningsOption);
        command.SetAction((parseResult, token) => Task.FromResult(Run(cli, parseResult, token)));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult, CancellationToken token)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var output = parseResult.InvocationConfiguration.Output;
        var json = parseResult.GetValue(cli.Options.Json);
        var options = new ReleaseOptions(
            parseResult.GetValue(VersionArgument) ?? string.Empty,
            Resolve(cli, parseResult.GetValue(OutDirOption)),
            parseResult.GetValue(CreditsOption),
            parseResult.GetValue(NameOption),
            Resolve(cli, parseResult.GetValue(InstallerOption)),
            parseResult.GetValue(AllowWarningsOption));

        var steps = new InlineProgress<ReleaseStep>(step =>
        {
            if (!json)
            {
                output.WriteLine($"[{(int)step + 1}/6] {Label(step)}");
            }
        });

        ReleaseResult result;
        try
        {
            result = new ReleaseService(KnownEngines.All, cli.Qa).Create(project, options, steps, token);
        }
        catch (QaBlockedException blocked)
        {
            return BuildCommand.RunQaBlocked(json, blocked, output);
        }

        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                version = result.Version,
                previousVersion = result.PreviousVersion,
                outputDirectory = result.OutputDirectory,
                files = result.Files.Select(f => new { name = f.Name, size = f.Size, sha256 = f.Sha256 }),
                gameFiles = result.GameFiles.Select(f => new { path = f.Path, originalSize = f.OriginalSize, patchSize = f.PatchSize, resultSize = f.ResultSize, created = f.Created }),
                verification = new { identical = result.Verification.Identical, restored = result.Verification.RestoredToOriginal, removed = result.Verification.RemovedCreated },
                changedScenes = result.Changes.Scenes.Count,
                changedLines = result.Changes.TotalLines,
            }));
            return ErrorHandler.OkExitCode;
        }

        WriteSummary(output, result);
        return ErrorHandler.OkExitCode;
    }

    private static string? Resolve(VnfCli cli, string? path) => path is null ? null : Path.GetFullPath(path, cli.CurrentDirectory);

    private static string Label(ReleaseStep step) => step switch
    {
        ReleaseStep.Check => "Conferir a tradução",
        ReleaseStep.Build => "Reconstruir o build",
        ReleaseStep.Diff => "Calcular as diferenças",
        ReleaseStep.Verify => "Aplicar numa cópia limpa e conferir byte a byte",
        ReleaseStep.Package => "Montar o instalador",
        _ => "Escrever leia-me, créditos e mudanças",
    };

    private static void WriteSummary(TextWriter output, ReleaseResult result)
    {
        output.WriteLine($"Versão {result.Version} pronta em {result.OutputDirectory}");
        foreach (var file in result.Files)
        {
            output.WriteLine($"  {file.Name}  {Size(file.Size)}{(file.Sha256 is null ? string.Empty : "  SHA-256 " + file.Sha256)}");
        }

        output.WriteLine($"O patch toca {result.GameFiles.Count.ToString("N0", PortugueseBrazil)} arquivo(s) do jogo e leva só as diferenças:");
        foreach (var file in result.GameFiles)
        {
            output.WriteLine($"  {file.Path}  original {Size(file.OriginalSize)}  patch {Size(file.PatchSize)}{(file.Created ? "  (arquivo novo)" : string.Empty)}");
        }

        output.WriteLine(
            $"Verificação: {result.Verification.Identical.ToString("N0", PortugueseBrazil)} arquivo(s) idênticos ao build; a desinstalação devolveu {result.Verification.RestoredToOriginal.ToString("N0", PortugueseBrazil)} original(is) byte a byte.");
        if (result.PreviousVersion is not null)
        {
            output.WriteLine($"Desde a {result.PreviousVersion}: {result.Changes.TotalLines.ToString("N0", PortugueseBrazil)} linha(s) em {result.Changes.Scenes.Count.ToString("N0", PortugueseBrazil)} cena(s) (MUDANCAS.txt).");
        }
    }

    private static string Size(long bytes) => bytes switch
    {
        >= 1024 * 1024 => (bytes / 1048576d).ToString("0.0", PortugueseBrazil) + " MB",
        >= 1024 => (bytes / 1024d).ToString("0", PortugueseBrazil) + " KB",
        _ => bytes.ToString("N0", PortugueseBrazil) + " B",
    };
}
