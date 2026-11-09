using System.CommandLine;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Images;
using VNForge.AI.Settings;
using VNForge.Core.Engines;
using VNForge.Core.Images;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using static VNForge.Cli.AiCommandSupport;

namespace VNForge.Cli;

/// <summary><c>vnf images scan</c>: lista as imagens dos pacotes do jogo e pede ao Claude com visão quais têm texto em japonês e o que dizem (<c>images.json</c>).</summary>
internal static class ImagesCommand
{
    private const int ProgressEvery = 10;

    private static readonly Option<string?> ModelOption = new("--model")
    {
        Description = $"Modelo Claude com visão: {string.Join(", ", ClaudeModels.All)}. Padrão: {ClaudeModels.Haiku}.",
        HelpName = "modelo",
    };

    private static readonly Option<bool> DryRunOption = new("--dry-run")
    {
        Description = "Só lista as imagens e estima tokens e custo; não chama a API nem grava nada.",
    };

    private static readonly Option<bool> ForceOption = new("--force")
    {
        Description = "Lê de novo as imagens que o inventário já tem.",
    };

    private static readonly Option<int?> LimitOption = new("--limit")
    {
        Description = "Para depois de n leituras (bom para testar o prompt).",
        HelpName = "n",
    };

    private static readonly Option<int?> ConcurrencyOption = new("--concurrency")
    {
        Description = "Chamadas em paralelo (padrão 4).",
        HelpName = "n",
    };

    private static readonly Option<int?> MinPixelsOption = new("--min-pixels")
    {
        Description = "Ignora imagens com menos pixels (largura vezes altura) que isto (padrão 2048, algo como 64 por 32).",
        HelpName = "n",
    };

    public static Command Create(VnfCli cli)
    {
        var command = new Command("images", "Imagens do jogo com texto em japonês (images.json).");
        command.Subcommands.Add(CreateScan(cli));
        return command;
    }

    private static Command CreateScan(VnfCli cli)
    {
        var command = new Command(
            "scan",
            "Lê com a IA quais imagens do jogo têm texto em japonês e grava images.json.");
        command.Options.Add(ModelOption);
        command.Options.Add(DryRunOption);
        command.Options.Add(ForceOption);
        command.Options.Add(LimitOption);
        command.Options.Add(ConcurrencyOption);
        command.Options.Add(MinPixelsOption);
        command.SetAction((parseResult, token) => ScanAsync(cli, parseResult, token));
        return command;
    }

    private static async Task<int> ScanAsync(VnfCli cli, ParseResult parseResult, CancellationToken token)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var error = parseResult.InvocationConfiguration.Error;
        var json = parseResult.GetValue(cli.Options.Json);
        var model = ReadModel(parseResult);
        var options = ReadOptions(parseResult);
        var concurrency = parseResult.GetValue(ConcurrencyOption) ?? 4;
        if (concurrency < 1)
        {
            throw new UsageException("--concurrency precisa ser 1 ou maior.");
        }

        var limit = parseResult.GetValue(LimitOption);
        if (limit < 0)
        {
            throw new UsageException("--limit precisa ser zero ou maior.");
        }

        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var config = project.LoadConfig();
        var engine = KnownEngines.All.FirstOrDefault(e => string.Equals(e.Id, config.Engine.Id, StringComparison.Ordinal))
            ?? throw new UsageException($"A engine '{config.Engine.Id}' do {ProjectFolder.ConfigFileName} não é conhecida por esta versão do vnf.");
        var gameDirectory = Path.GetFullPath(config.Game.Path, project.Root);

        if (!json)
        {
            error.WriteLine("Lendo os pacotes do jogo...");
        }

        var listing = ImageLister.List(gameDirectory, engine.Archives, options, token);
        var existing = project.LoadImageInventory();
        var plan = ImageScanPlan.Build(listing, existing, parseResult.GetValue(ForceOption), limit);
        var estimate = ImageScanCost.Estimate(plan.ToRead, model);
        var dryRun = parseResult.GetValue(DryRunOption);

        if (dryRun)
        {
            return WriteDryRun(output, json, model, listing, plan, estimate);
        }

        if (!json)
        {
            WriteListing(output, model, listing, plan, estimate);
        }

        ImageScanResult result;
        if (plan.ToRead.Count == 0)
        {
            result = ImageTextScanner.WithoutReading(plan);
        }
        else
        {
            var refusal = CheckBudget(parseResult, cli, project, ReadBudget(parseResult, cli, error), estimate);
            if (refusal is not null)
            {
                return WriteStopped(output, json, model, listing, plan, estimate, refusal);
            }

            var reader = cli.ImageTextReaderFactory is { } factory ? factory(model) : CreateClaudeReader(cli, model);
            result = await new ImageTextScanner(reader, engine.Archives)
                .ScanAsync(gameDirectory, plan, new ImageScanRun { Concurrency = concurrency }, new LineProgress(error, json), token).ConfigureAwait(false);
        }

        // O gasto entra na conta antes da gravação: a API já cobrou, e uma falha ao gravar o images.json não pode apagar essa conta.
        var usage = result.InputTokens + result.OutputTokens > 0 ? new ClaudeUsage(result.InputTokens, result.OutputTokens, 0, 0) : null;
        if (usage is not null)
        {
            RunCostTracker.Record(project, model, usage);
        }

        if (!result.Entries.SequenceEqual(existing))
        {
            project.SaveImageInventory(result.Entries);
        }

        var exitCode = result.Failed.Count > 0 || result.AbortReason is not null ? ErrorHandler.ErrorExitCode : ErrorHandler.OkExitCode;
        WriteResult(output, json, model, listing, plan, estimate, result, usage, exitCode);
        token.ThrowIfCancellationRequested();
        return exitCode;
    }

    private static string ReadModel(ParseResult parseResult)
    {
        var model = parseResult.GetValue(ModelOption) ?? ClaudeModels.Haiku;
        return ClaudeModels.All.Contains(model)
            ? model
            : throw new UsageException($"Modelo desconhecido: '{model}'. Use um de: {string.Join(", ", ClaudeModels.All)}.");
    }

    private static ImageScanOptions ReadOptions(ParseResult parseResult)
    {
        var defaults = new ImageScanOptions();
        var minPixels = parseResult.GetValue(MinPixelsOption) ?? defaults.MinPixels;
        return minPixels < 0 ? throw new UsageException("--min-pixels precisa ser zero ou maior.") : defaults with { MinPixels = minPixels };
    }

    private static IImageTextReader CreateClaudeReader(VnfCli cli, string model) =>
        ClaudeProviderFactory.CreateImageReader(RequireApiKey(cli), new ClaudeOptions { Model = model, Effort = ClaudeEffort.Low, MaxTokens = 2000 });

    /// <summary>O orçamento do <c>app.json</c>. Arquivo ilegível avisa no erro padrão e só segue com <c>--yes</c>: ele pode esconder um limite que a pessoa pôs.</summary>
    private static BudgetSettings ReadBudget(ParseResult parseResult, VnfCli cli, TextWriter error)
    {
        var reading = AppBudgetFile.Load(cli.UserConfigDirectory);
        if (reading.Problem is not { } problem)
        {
            return reading.Settings;
        }

        error.WriteLine($"Aviso: {problem} O limite de gasto e o valor de aviso do app não puderam ser conferidos.");
        return parseResult.GetValue(cli.Options.Yes)
            ? reading.Settings
            : throw new UsageException("Corrija ou apague o app.json, ou use --yes para ler as imagens sem conferir o limite de gasto.");
    }

    /// <summary>Confere o limite de gasto e o valor de aviso do app contra a estimativa; devolve por que parou, ou nulo para seguir.</summary>
    private static string? CheckBudget(ParseResult parseResult, VnfCli cli, ProjectFolder project, BudgetSettings budget, CostEstimate estimate)
    {
        var cost = CostEstimator.Brl(estimate, budget.Rate);
        if (budget.Limits.Limit is { } limit)
        {
            var spent = budget.Rate.ToBrl(AiUsageLog.Load(project).Dollars);
            if (spent + cost > limit)
            {
                return $"Parou antes de chamar a IA: o gasto do projeto ({Brl(spent)}) mais esta leitura (cerca de {Brl(cost)}) passa do limite de gasto de {Brl(limit)}. Aumente o limite nos Ajustes do app ou leia menos imagens com --limit.";
            }
        }

        if (budget.Limits.WarnAbove is { } warn && cost > warn
            && !ConfirmationPrompt.Confirm(parseResult, cli, $"Esta leitura custa cerca de {Brl(cost)}, acima do valor de aviso de {Brl(warn)}. Continuar?"))
        {
            return "A leitura não foi confirmada: nenhuma imagem foi enviada à IA.";
        }

        return null;
    }

    private static int WriteDryRun(TextWriter output, bool json, string model, ImageListing listing, ImageScanPlan plan, CostEstimate estimate)
    {
        if (json)
        {
            var data = JsonBase(model, true, listing, plan, estimate);
            output.WriteLine(JsonEnvelope.Result(data));
            return ErrorHandler.OkExitCode;
        }

        WriteListing(output, model, listing, plan, estimate);
        return ErrorHandler.OkExitCode;
    }

    private static int WriteStopped(TextWriter output, bool json, string model, ImageListing listing, ImageScanPlan plan, CostEstimate estimate, string reason)
    {
        if (json)
        {
            var data = JsonBase(model, false, listing, plan, estimate);
            data["stopped"] = reason;
            output.WriteLine(JsonEnvelope.Result(data, ErrorHandler.ErrorExitCode));
        }
        else
        {
            output.WriteLine(reason);
        }

        return ErrorHandler.ErrorExitCode;
    }

    private static void WriteListing(TextWriter output, string model, ImageListing listing, ImageScanPlan plan, CostEstimate estimate)
    {
        output.WriteLine($"Pacotes lidos: {Number(listing.ArchivesRead)}. {Count(listing.Candidates.Count, "imagem", "imagens")} que a IA lê (PNG, JPEG, GIF e WebP); {SkippedText(listing.Skipped)}.");
        foreach (var warning in listing.Warnings.Take(10))
        {
            output.WriteLine($"Aviso: {warning}");
        }

        output.WriteLine($"Estimativa ({model}): {Count(plan.ToRead.Count, "imagem", "imagens")} para a IA, {Count(plan.Reused, "imagem já está", "imagens já estão")} no inventário.");
        if (plan.ToRead.Count > 0)
        {
            WriteEstimateTail(output, estimate.Usage, estimate.Dollars, "vnf images scan");
        }
    }

    private static string SkippedText(IReadOnlyList<SkippedImage> skipped)
    {
        if (skipped.Count == 0)
        {
            return "nenhuma ignorada";
        }

        var parts = new List<string>();
        void Add(ImageSkipReason reason, string singular, string plural)
        {
            var count = skipped.Count(s => s.Reason == reason);
            if (count > 0)
            {
                parts.Add($"{Number(count)} {(count == 1 ? singular : plural)}");
            }
        }

        Add(ImageSkipReason.TooSmall, "pequena demais", "pequenas demais");
        Add(ImageSkipReason.UnsupportedFormat, "em formato que a IA não lê (BMP ou do jogo)", "em formato que a IA não lê (BMP ou do jogo)");
        Add(ImageSkipReason.NotDecodable, "com arquivo inválido", "com arquivos inválidos");
        Add(ImageSkipReason.TooLarge, "grande demais", "grandes demais");
        Add(ImageSkipReason.Unreadable, "que o pacote não entregou", "que o pacote não entregou");
        return $"{Count(skipped.Count, "ignorada", "ignoradas")}: {string.Join(", ", parts)}";
    }

    private static Dictionary<string, object?> JsonBase(string model, bool dryRun, ImageListing listing, ImageScanPlan plan, CostEstimate estimate)
    {
        int Skipped(ImageSkipReason reason) => listing.Skipped.Count(s => s.Reason == reason);
        return new Dictionary<string, object?>
        {
            ["dryRun"] = dryRun,
            ["model"] = model,
            ["archives"] = listing.ArchivesRead,
            ["candidates"] = listing.Candidates.Count,
            ["skipped"] = new
            {
                tooSmall = Skipped(ImageSkipReason.TooSmall),
                tooLarge = Skipped(ImageSkipReason.TooLarge),
                unsupportedFormat = Skipped(ImageSkipReason.UnsupportedFormat),
                notDecodable = Skipped(ImageSkipReason.NotDecodable),
                unreadable = Skipped(ImageSkipReason.Unreadable),
            },
            ["toRead"] = plan.ToRead.Count,
            ["reused"] = plan.Reused,
            ["warnings"] = listing.Warnings,
            ["estimate"] = new { images = estimate.Lines, inputTokens = estimate.Usage.Input, outputTokens = estimate.Usage.Output, dollars = estimate.Dollars },
        };
    }

    private static void WriteResult(
        TextWriter output, bool json, string model, ImageListing listing, ImageScanPlan plan, CostEstimate estimate, ImageScanResult result, ClaudeUsage? usage, int exitCode)
    {
        var dollars = RunCostTracker.DollarsFor(model, usage);
        var withText = result.Entries.Where(e => e.HasText).ToList();
        if (json)
        {
            var data = JsonBase(model, false, listing, plan, estimate);
            data["read"] = result.Read;
            data["withText"] = withText.Count;
            data["inventory"] = result.Entries.Count;
            data["images"] = withText.Select(e => new { archive = e.Archive, path = e.Path, width = e.Width, height = e.Height, text = e.Text });
            data["refused"] = result.Refused.Select(r => $"{r.Archive}/{r.Path}");
            data["failed"] = result.Failed.Select(f => new { archive = f.Archive, path = f.Path, error = f.Message });
            data["cancelled"] = result.Cancelled;
            data["abortReason"] = result.AbortReason;
            data["usage"] = UsageJson(usage);
            data["dollars"] = dollars;
            output.WriteLine(JsonEnvelope.Result(data, exitCode));
            return;
        }

        if (result.Cancelled)
        {
            output.WriteLine("Leitura cancelada. O que já foi lido ficou em images.json; rode de novo para continuar.");
        }

        output.WriteLine(
            $"{Count(result.Read, "imagem lida", "imagens lidas")}. O inventário (images.json) tem {Count(result.Entries.Count, "imagem", "imagens")}: "
            + $"{Count(withText.Count, "imagem com texto", "imagens com texto")} em japonês e {Number(result.Entries.Count - withText.Count)} sem texto.");
        if (result.Refused.Count > 0)
        {
            output.WriteLine($"{Count(result.Refused.Count, "imagem recusada", "imagens recusadas")} pelo modelo; seguem fora do inventário: {string.Join(", ", result.Refused.Take(10).Select(r => $"{r.Archive}/{r.Path}"))}{(result.Refused.Count > 10 ? ", ..." : string.Empty)}");
        }

        foreach (var failure in result.Failed.Take(10))
        {
            output.WriteLine($"Falhou: {failure.Archive}/{failure.Path}: {failure.Message}");
        }

        if (result.Failed.Count > 10)
        {
            output.WriteLine($"... e mais {Number(result.Failed.Count - 10)} falha(s); use --json para ver todas.");
        }

        if (result.AbortReason is not null)
        {
            output.WriteLine(result.AbortReason);
        }

        WriteUsage(output, usage, dollars);
    }

    private sealed class LineProgress(TextWriter error, bool json) : IProgress<ImageScanProgress>
    {
        public void Report(ImageScanProgress value)
        {
            if (json || (value.Done % ProgressEvery != 0 && value.Done != value.Total))
            {
                return;
            }

            error.WriteLine($"{Number(value.Done)}/{Number(value.Total)}  {value.Name}");
        }
    }
}
