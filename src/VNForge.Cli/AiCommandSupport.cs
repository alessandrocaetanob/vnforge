using System.CommandLine;
using System.Globalization;
using System.Text.RegularExpressions;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli;

/// <summary>Modelo, esforço, lote e paralelismo de uma execução de IA (<c>vnf translate</c> e <c>vnf review</c>).</summary>
internal sealed record AiRunSettings(string Model, ClaudeEffort Effort, int BatchSize, int Concurrency);

/// <summary>As opções que <c>vnf translate</c> e <c>vnf review</c> dividem.</summary>
internal sealed class AiOptions
{
    public Option<string?> Files { get; } = new("--files")
    {
        Description = "Só alguns scripts, por glob sobre a chave (\"yst001*\"); aceita vários, separados por vírgula.",
        HelpName = "padrão",
    };

    public Option<string?> Model { get; } = new("--model")
    {
        Description = $"Modelo Claude: {string.Join(", ", ClaudeModels.All)}. Padrão: o de 'vnf config ai' ou o do vnforge.json.",
        HelpName = "id",
    };

    public Option<string?> Effort { get; } = new("--effort")
    {
        Description = "Esforço do modelo: low, medium ou high. Padrão: o do vnforge.json.",
        HelpName = "nível",
    };

    public Option<int?> BatchSize { get; } = new("--batch-size")
    {
        Description = $"Linhas por chamada, de {TranslationRunOptions.MinBatchSize} a {TranslationRunOptions.MaxBatchSize}. Padrão: o do vnforge.json (30).",
        HelpName = "n",
    };

    public Option<int?> Concurrency { get; } = new("--concurrency")
    {
        Description = "Chamadas em paralelo (padrão 2).",
        HelpName = "n",
    };

    public Option<bool> DryRun { get; } = new("--dry-run")
    {
        Description = "Só estima tokens e custo; não chama a API nem grava nada.",
    };

    public Option<int?> Limit { get; } = new("--limit")
    {
        Description = "Para depois de n linhas (bom para testar o prompt).",
        HelpName = "n",
    };

    /// <summary>Acrescenta as opções ao comando, com <paramref name="status"/> logo depois de <c>--files</c>.</summary>
    public void AddTo(Command command, Option<string?> status)
    {
        command.Options.Add(Files);
        command.Options.Add(status);
        command.Options.Add(Model);
        command.Options.Add(Effort);
        command.Options.Add(BatchSize);
        command.Options.Add(Concurrency);
        command.Options.Add(DryRun);
        command.Options.Add(Limit);
    }

    public AiRunSettings ReadSettings(VnfCli cli, ParseResult parseResult, AiConfig ai) =>
        AiCommandSupport.ReadSettings(cli, ai, parseResult.GetValue(Model), parseResult.GetValue(Effort), parseResult.GetValue(BatchSize), parseResult.GetValue(Concurrency));

    /// <summary>O <c>--limit</c> pedido; recusa valor negativo.</summary>
    public int? ReadLimit(ParseResult parseResult)
    {
        var limit = parseResult.GetValue(Limit);
        return limit < 0 ? throw new UsageException("--limit precisa ser zero ou maior.") : limit;
    }
}

/// <summary>Tudo que <c>vnf translate</c> e <c>vnf review</c> leem antes de escolher as linhas.</summary>
internal sealed record AiCommandContext(
    TextWriter Output, TextWriter Error, bool Json, ProjectFolder Project, AiRunSettings Settings, Regex? Filter, int? Limit)
{
    public static AiCommandContext Open(VnfCli cli, ParseResult parseResult, AiOptions common)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        return new AiCommandContext(
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error,
            parseResult.GetValue(cli.Options.Json),
            project,
            common.ReadSettings(cli, parseResult, project.LoadConfig().Ai),
            AiCommandSupport.ParseFiles(parseResult.GetValue(common.Files)),
            common.ReadLimit(parseResult));
    }
}

/// <summary>Leitura de opções e formatação de mensagens que os comandos de IA dividem.</summary>
internal static class AiCommandSupport
{
    private const int ProgressBarWidth = 20;

    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Modelo: opção, depois <c>vnf config ai</c>, depois <c>vnforge.json</c>. Esforço e lote caem no <c>vnforge.json</c>.</summary>
    public static AiRunSettings ReadSettings(
        VnfCli cli, AiConfig ai, string? modelOption, string? effortOption, int? batchSizeOption, int? concurrencyOption)
    {
        var settingsFile = new AiUserSettingsFile(cli.UserConfigDirectory);
        var model = modelOption
            ?? (File.Exists(settingsFile.Path) ? settingsFile.Load().Model : null)
            ?? (ClaudeModels.All.Contains(ai.Model) ? ai.Model : ClaudeModels.Opus);
        if (!ClaudeModels.All.Contains(model))
        {
            throw new UsageException($"Modelo desconhecido: '{model}'. Use um de: {string.Join(", ", ClaudeModels.All)}.");
        }

        var effort = ParseEffort(effortOption ?? ai.Effort, strict: effortOption is not null);

        var batchSize = batchSizeOption ?? ai.BatchSize;
        if (batchSize < TranslationRunOptions.MinBatchSize || batchSize > TranslationRunOptions.MaxBatchSize)
        {
            var origin = batchSizeOption is null ? "ai.batchSize do vnforge.json" : "--batch-size";
            throw new UsageException($"{origin} precisa ficar entre {TranslationRunOptions.MinBatchSize} e {TranslationRunOptions.MaxBatchSize} (recebeu {batchSize}).");
        }

        var concurrency = concurrencyOption ?? 2;
        if (concurrency < 1)
        {
            throw new UsageException("--concurrency precisa ser 1 ou maior.");
        }

        return new AiRunSettings(model, effort, batchSize, concurrency);
    }

    /// <summary>Campos que o <c>--json</c> de <c>translate</c> e <c>review</c> repartem; cada comando acrescenta os seus.</summary>
    public static Dictionary<string, object?> JsonBase(AiRunSettings settings, bool dryRun, int lines) => new()
    {
        ["dryRun"] = dryRun,
        ["model"] = settings.Model,
        ["effort"] = settings.Effort.ToString().ToLowerInvariant(),
        ["batchSize"] = settings.BatchSize,
        ["concurrency"] = settings.Concurrency,
        ["lines"] = lines,
    };

    /// <summary>Acrescenta ao <c>--json</c> o que sobrou da execução: recusas, lotes que falharam, tokens e custo.</summary>
    public static Dictionary<string, object?> WithOutcome(
        Dictionary<string, object?> data, IEnumerable<string> refused, IReadOnlyList<TranslationBatchFailure> failed, ClaudeUsage? usage, decimal? dollars)
    {
        data["refused"] = refused.Order(StringComparer.Ordinal);
        data["failed"] = failed.Select(f => new { lines = f.LineIds, error = f.Error.Message, attempts = f.Attempts });
        data["usage"] = UsageJson(usage);
        data["dollars"] = dollars;
        return data;
    }

    public static void WriteFailures(TextWriter output, IReadOnlyList<TranslationBatchFailure> failures)
    {
        foreach (var failure in failures)
        {
            output.WriteLine($"Lote com falha ({Count(failure.LineIds.Count, "linha", "linhas")}, {Count(failure.Attempts, "tentativa", "tentativas")}): {failure.Error.Message}");
        }
    }

    public static decimal? DollarsFor(string model, ClaudeUsage? usage) =>
        usage is { } u
            ? ModelPricing.For(model).Cost(new TokenUsage(u.InputTokens, u.OutputTokens, u.CacheReadInputTokens, u.CacheCreationInputTokens))
            : null;

    /// <summary>Soma o gasto da execução ao total do projeto (<c>.cache/ai-usage.json</c>), que o Painel do app mostra.</summary>
    public static void RecordUsage(ProjectFolder project, string model, ClaudeUsage? usage)
    {
        if (usage is { } u)
        {
            AiUsageLog.TryRecord(project, new AiUsageTotals(u.InputTokens, u.OutputTokens, u.CacheReadInputTokens, u.CacheCreationInputTokens, DollarsFor(model, u) ?? 0m, 1));
        }
    }

    /// <summary>
    /// Fecha o texto do resultado: linhas recusadas, lotes que falharam, quantas linhas seguem pendentes e os tokens.
    /// </summary>
    /// <param name="refusedSuffix">Depois de "linha recusada", até os dois-pontos que antecedem os ids.</param>
    /// <param name="pendingSuffix">Depois de "linhas seguem", o que falta e como repetir.</param>
    public static void WriteOutcome(
        TextWriter output, IReadOnlyCollection<string> refused, string refusedSuffix, IReadOnlyList<TranslationBatchFailure> failed, string pendingSuffix, ClaudeUsage? usage, decimal? dollars)
    {
        if (refused.Count > 0)
        {
            output.WriteLine($"{Count(refused.Count, "linha recusada", "linhas recusadas")} {refusedSuffix}: {string.Join(", ", refused.Order(StringComparer.Ordinal).Take(10))}{(refused.Count > 10 ? ", ..." : string.Empty)}");
        }

        WriteFailures(output, failed);

        var failedLines = failed.Sum(f => f.LineIds.Count);
        if (failedLines > 0)
        {
            output.WriteLine($"{Count(failedLines, "linha segue", "linhas seguem")} {pendingSuffix}");
        }

        WriteUsage(output, usage, dollars);
    }

    /// <summary>As duas últimas linhas da estimativa do <c>--dry-run</c>.</summary>
    public static void WriteEstimateTail(TextWriter output, TokenUsage usage, decimal dollars, string command)
    {
        output.WriteLine($"~{Tokens(usage.Input)} de entrada, ~{Tokens(usage.Output)} de saída; custo estimado {Dollars(dollars)}.");
        output.WriteLine($"É uma ordem de grandeza: o custo real aparece ao fim de '{command}'.");
    }

    public static void WriteUsage(TextWriter output, ClaudeUsage? usage, decimal? dollars)
    {
        if (usage is not null)
        {
            output.WriteLine($"Tokens: {Tokens(usage.InputTokens)} de entrada, {Tokens(usage.OutputTokens)} de saída, {Tokens(usage.CacheReadInputTokens)} lidos do cache de prompt; custo {Dollars(dollars ?? 0m)}.");
        }
    }

    public static object? UsageJson(ClaudeUsage? usage) =>
        usage is null ? null : new { input = usage.InputTokens, output = usage.OutputTokens, cacheRead = usage.CacheReadInputTokens, cacheWrite = usage.CacheCreationInputTokens };

    public static string RequireApiKey(VnfCli cli) =>
        ApiKeyResolver.Resolve(cli.ApiKeyStore).Key
            ?? throw new UsageException(
                $"Sem chave de API. Defina {ApiKeyResolver.EnvironmentVariable} ou rode 'vnf config ai --set-key'. Use --dry-run para só estimar o custo.");

    /// <summary>Glob sobre a chave do script (<c>*</c> e <c>?</c>), sem diferenciar maiúsculas; <c>.json</c> no fim do padrão é ignorado.</summary>
    public static Regex? ParseFiles(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var alternatives = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? p[..^5] : p)
            .Select(p => Regex.Escape(p).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal));
        return new Regex(
            $"^(?:{string.Join('|', alternatives)})$",
            RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    public static string ProgressLine(int done, int total, string key)
    {
        var fraction = total == 0 ? 1.0 : Math.Min(1.0, (double)done / total);
        var filled = (int)Math.Floor(fraction * ProgressBarWidth);
        var bar = new string('#', filled) + new string('-', ProgressBarWidth - filled);
        return $"[{bar}] {(int)Math.Floor(fraction * 100)}%  {Number(done)}/{Number(total)}  {key}.json";
    }

    public static string Count(int value, string singular, string plural) => $"{Number(value)} {(value == 1 ? singular : plural)}";

    public static string Number(long value) => value.ToString("N0", PortugueseBrazil);

    public static string Tokens(long value) => value >= 1_000_000
        ? string.Create(PortugueseBrazil, $"{value / 1_000_000.0:0.0} M tokens")
        : $"{Number(value)} tokens";

    public static string Dollars(decimal value) => string.Create(PortugueseBrazil, $"US$ {value:0.00}");

    private static ClaudeEffort ParseEffort(string text, bool strict)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "low": return ClaudeEffort.Low;
            case "medium": return ClaudeEffort.Medium;
            case "high": return ClaudeEffort.High;
            default:
                return strict
                    ? throw new UsageException($"Esforço desconhecido: '{text}'. Use low, medium ou high.")
                    : ClaudeEffort.Medium;
        }
    }
}

/// <summary>
/// As linhas que um comando de IA vai processar e o mapa para gravá-las de volta: o conteúdo de cada <c>text/*.json</c> tocado
/// e, por id, a chave do arquivo e o índice da linha.
/// </summary>
internal sealed class LineSelection
{
    private const int PreviousLinesWindow = 30;

    public Dictionary<string, List<TextUnit>> Files { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> KeyById { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> IndexById { get; } = new(StringComparer.Ordinal);

    public List<TextUnit> Pending { get; } = [];

    /// <param name="filter">Glob sobre a chave do script; <c>null</c> aceita todas.</param>
    /// <param name="limit">Máximo de linhas; <c>null</c> sem limite.</param>
    /// <param name="wanted">Quais linhas de um script entram.</param>
    public static LineSelection Build(
        IEnumerable<KeyValuePair<string, IReadOnlyList<TextUnit>>> texts, Regex? filter, int? limit, Func<TextUnit, bool> wanted)
    {
        var selection = new LineSelection();
        foreach (var (key, units) in texts)
        {
            if (filter is not null && !filter.IsMatch(key))
            {
                continue;
            }

            var selected = units.Where(wanted).ToList();
            if (selected.Count == 0 || (limit is { } max && selection.Pending.Count >= max))
            {
                continue;
            }

            selection.Files[key] = [.. units];
            for (var i = 0; i < units.Count; i++)
            {
                selection.KeyById[units[i].Id] = key;
                selection.IndexById[units[i].Id] = i;
            }

            selection.Pending.AddRange(limit is { } cap ? selected.Take(cap - selection.Pending.Count) : selected);
        }

        return selection;
    }

    /// <summary>
    /// Aplica <paramref name="update"/> a cada resultado (devolve a linha nova, ou <c>null</c> para deixá-la como está), grava só os
    /// <c>text/*.json</c> que mudaram e devolve a chave do último script gravado (ou a do lote, se nenhum mudou), para o progresso.
    /// </summary>
    public string ApplyAndSave(ProjectFolder project, IReadOnlyList<TranslationResult> results, Func<TranslationResult, TextUnit, TextUnit?> update)
    {
        var touched = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            var key = KeyById[result.Id];
            var list = Files[key];
            var index = IndexById[result.Id];
            if (update(result, list[index]) is { } changed)
            {
                list[index] = changed;
                touched.Add(key);
            }
        }

        foreach (var key in touched)
        {
            project.SaveText(key, Files[key]);
        }

        return touched.LastOrDefault() ?? KeyById[results[0].Id];
    }

    /// <summary>Monta o lote com as linhas anteriores do script como contexto; chame sob o lock que protege <see cref="Files"/>.</summary>
    public TranslationBatch BuildBatch(TranslationBatchBuilder builder, IReadOnlyList<TextUnit> lines)
    {
        var first = lines[0].Id;
        var previous = Files[KeyById[first]].Take(IndexById[first]).TakeLast(PreviousLinesWindow).ToList();
        return builder.Build(lines, previous);
    }
}
