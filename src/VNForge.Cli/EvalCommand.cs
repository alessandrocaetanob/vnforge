using System.CommandLine;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Eval;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.Core.Eval;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using static VNForge.Cli.AiCommandSupport;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf eval</c>: o banco de provas. Cada configuração (modelo, esforço, contexto, com ou sem bíblia) traduz as mesmas linhas aprovadas do projeto, e o relatório mostra
/// custo, tempo e qualidade (chrF contra a aprovada e a nota de um juiz de IA que não sabe qual configuração gerou o texto). Não grava tradução no projeto.
/// </summary>
internal static class EvalCommand
{
    private const int DefaultSample = 200;
    private const int MaxSample = 2000;
    private const string DefaultJudgeModel = ClaudeModels.Sonnet;

    private static readonly Option<string[]> ConfigOption = new("--config")
    {
        Description = "Uma configuração a comparar, no formato modelo[:effort[:contexto[:com-biblia|sem-biblia]]], por exemplo sonnet:medium:15:sem-biblia. Repita a opção para cada configuração. Sem ela, vale o último conjunto da rodada anterior ou opus, sonnet e haiku.",
        HelpName = "configuração",
    };

    private static readonly Option<int?> SampleOption = new("--sample")
    {
        Description = $"Quantas linhas aprovadas sortear (padrão {DefaultSample}); todas as configurações traduzem as mesmas.",
        HelpName = "n",
    };

    private static readonly Option<int?> SeedOption = new("--seed")
    {
        Description = "Semente do sorteio (padrão 1): a mesma semente repete a mesma amostra.",
        HelpName = "n",
    };

    private static readonly Option<int?> PairsOption = new("--pairs")
    {
        Description = $"Quantos pares a rodada às cegas do app ganha (padrão {EvalRunRequest.DefaultPairCount}).",
        HelpName = "n",
    };

    private static readonly Option<bool> NoJudgeOption = new("--no-judge")
    {
        Description = "Roda sem o juiz de IA: só chrF, custo e tempo.",
    };

    private static readonly Option<string?> JudgeModelOption = new("--judge-model")
    {
        Description = $"Modelo do juiz de IA: {string.Join(", ", ClaudeModels.All)}. Padrão: {DefaultJudgeModel}.",
        HelpName = "modelo",
    };

    private static readonly Option<bool> DryRunOption = new("--dry-run")
    {
        Description = "Só estima o custo da rodada; não chama a API nem grava nada.",
    };

    private static readonly Option<bool> ReportOption = new("--report")
    {
        Description = "Mostra o relatório da última rodada, sem rodar nada.",
    };

    private static readonly Option<string?> UseOption = new("--use")
    {
        Description = "Marca a configuração (a letra do relatório) como a escolhida para o M4 na última rodada, sem rodar nada.",
        HelpName = "letra",
    };

    public static Command Create(VnfCli cli)
    {
        var command = new Command("eval", "Banco de provas: compara modelo, effort e contexto nas linhas aprovadas, em custo e qualidade.");
        command.Options.Add(ConfigOption);
        command.Options.Add(SampleOption);
        command.Options.Add(SeedOption);
        command.Options.Add(PairsOption);
        command.Options.Add(NoJudgeOption);
        command.Options.Add(JudgeModelOption);
        command.Options.Add(DryRunOption);
        command.Options.Add(ReportOption);
        command.Options.Add(UseOption);
        command.SetAction((parseResult, token) => RunAsync(cli, parseResult, token));
        return command;
    }

    private static async Task<int> RunAsync(VnfCli cli, ParseResult parseResult, CancellationToken token)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var error = parseResult.InvocationConfiguration.Error;
        var json = parseResult.GetValue(cli.Options.Json);
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var rate = AppBudgetFile.Load(cli.UserConfigDirectory).Settings.Rate;

        if (parseResult.GetValue(UseOption) is { } letter)
        {
            return Choose(output, json, project, rate, letter);
        }

        if (parseResult.GetValue(ReportOption))
        {
            var latest = EvalStore.LoadLatest(project) ?? throw new UsageException("Ainda não há rodada neste projeto.", "Rode 'vnf eval' para fazer a primeira.");
            return WriteReport(output, json, latest, rate);
        }

        var sample = parseResult.GetValue(SampleOption) ?? DefaultSample;
        if (sample is < 1 or > MaxSample)
        {
            throw new UsageException($"--sample precisa ficar entre 1 e {MaxSample} (recebeu {sample}).");
        }

        var seed = parseResult.GetValue(SeedOption) ?? 1;
        var pairs = parseResult.GetValue(PairsOption) ?? EvalRunRequest.DefaultPairCount;
        if (pairs < 0)
        {
            throw new UsageException("--pairs precisa ser zero ou maior.");
        }

        var configs = ReadConfigs(parseResult, project);
        var judgeModel = parseResult.GetValue(NoJudgeOption) ? null : ReadJudgeModel(parseResult);

        if (parseResult.GetValue(DryRunOption))
        {
            return WriteDryRun(output, json, EvalEstimator.Estimate(project, configs, sample, seed, new TranslationRunOptions().BatchSize, judgeModel), rate);
        }

        var budget = TranslateCommand.ReadBudget(parseResult, cli, error);
        var estimate = EvalEstimator.Estimate(project, configs, sample, seed, new TranslationRunOptions().BatchSize, judgeModel);
        var tracker = new RunCostTracker(project, configs[0].Model, budget.Rate, () => null);
        var guard = tracker.CreateGuard(budget.Limits, _ => budget.Rate.ToBrl(estimate.Dollars), (question, _) =>
            Task.FromResult(ConfirmationPrompt.Confirm(parseResult, cli, $"A rodada custa cerca de {Brl(question.EstimatedCost)}, acima do valor de aviso de {Brl(question.WarnAbove)}. Continuar?")));
        var estimateBrl = budget.Rate.ToBrl(estimate.Dollars);
        if (budget.Limits.Limit is { } cap && estimateBrl > cap - tracker.SpentBrl())
        {
            throw new UsageException(
                $"A rodada custa cerca de {Brl(estimateBrl)}, mas só restam {Brl(Math.Max(0m, cap - tracker.SpentBrl()))} do limite de gasto de {Brl(cap)}.",
                "Rode menos configurações, use --no-judge ou aumente o limite nos Ajustes do app.");
        }

        if (await guard.AuthorizeAsync(budget.Rate.ToBrl(estimate.Dollars), token).ConfigureAwait(false) is { } stop)
        {
            throw new UsageException(
                stop == BudgetStop.LimitReached ? "O gasto do projeto já chegou ao limite definido nos Ajustes do app." : "A rodada não foi confirmada: nada foi enviado à IA.",
                stop == BudgetStop.LimitReached ? "Aumente o limite nos Ajustes do app para rodar o banco de provas." : null);
        }

        var request = BuildRequest(cli, project, configs, sample, seed, pairs, judgeModel, budget, json ? null : new Progress<EvalProgress>(p => error.WriteLine(ProgressText(p))));
        var round = await EvalRunner.RunAsync(request, token).ConfigureAwait(false);
        return WriteReport(output, json, round, budget.Rate);
    }

    private static EvalRunRequest BuildRequest(
        VnfCli cli, ProjectFolder project, IReadOnlyList<EvalConfig> configs, int sample, int seed, int pairs, string? judgeModel, BudgetSettings budget, IProgress<EvalProgress>? progress)
    {
        string? key = null;
        string Key() => key ??= RequireApiKey(cli);

        EvalProvider Provider(EvalConfig config)
        {
            var effort = ClaudeEffortText.ParseOrDefault(config.Effort);
            var translator = cli.TranslationProviderFactory is { } factory
                ? factory(config.Model, effort)
                : ClaudeProviderFactory.Create(Key(), new ClaudeOptions { Model = config.Model, Effort = effort });
            return new EvalProvider(translator, () => (translator as ClaudeTranslationProvider)?.Usage);
        }

        IJudgeProvider? judge = judgeModel is null
            ? null
            : cli.JudgeProviderFactory is { } judgeFactory
                ? judgeFactory(judgeModel, ClaudeEffort.Low)
                : ClaudeProviderFactory.CreateJudge(Key(), new ClaudeOptions { Model = judgeModel, Effort = ClaudeEffort.Low });

        return new EvalRunRequest(project, configs, sample, seed, Provider)
        {
            Judge = judge,
            JudgeModel = judgeModel,
            PairCount = pairs,
            Limits = budget.Limits,
            Rate = budget.Rate,
            Progress = progress,
        };
    }

    private static IReadOnlyList<EvalConfig> ReadConfigs(ParseResult parseResult, ProjectFolder project)
    {
        var specs = parseResult.GetValue(ConfigOption) ?? [];
        if (specs.Length > 0)
        {
            return EvalConfigParser.ParseAll(specs);
        }

        return EvalStore.LoadConfigs(project) is { Count: > 0 } saved ? saved : EvalConfigParser.Defaults;
    }

    private static string ReadJudgeModel(ParseResult parseResult)
    {
        var model = parseResult.GetValue(JudgeModelOption) ?? DefaultJudgeModel;
        return ClaudeModels.All.Contains(model)
            ? model
            : throw new UsageException($"Modelo do juiz desconhecido: '{model}'. Use um de: {string.Join(", ", ClaudeModels.All)}.");
    }

    private static int Choose(TextWriter output, bool json, ProjectFolder project, ExchangeRate rate, string letter)
    {
        var round = EvalStore.LoadLatest(project) ?? throw new UsageException("Ainda não há rodada neste projeto.", "Rode 'vnf eval' para fazer a primeira.");
        var id = letter.Trim().ToUpperInvariant();
        if (round.Reports.All(r => r.Config.Id != id))
        {
            throw new UsageException($"A última rodada não tem a configuração '{letter}'.", $"As letras dela são: {string.Join(", ", round.Reports.Select(r => r.Config.Id))}.");
        }

        round = round.WithChosen(id);
        EvalStore.SaveRound(project, round);
        var chosen = round.Reports.First(r => r.Config.Id == id).Config;
        EvalChoice.Apply(project, chosen);
        if (!json)
        {
            output.WriteLine($"Configuração {id} ({chosen.Title}) escolhida para o M4.");
            output.WriteLine($"O vnforge.json passa a usar {chosen.Model}, effort {chosen.Effort} e {chosen.ContextLines} falas de contexto de cada lado.");
            return ErrorHandler.OkExitCode;
        }

        return WriteReport(output, json, round, rate);
    }

    private static string ProgressText(EvalProgress progress) =>
        $"Configuração {progress.ConfigId} ({progress.ConfigNumber} de {progress.ConfigCount}): {(progress.Phase == EvalPhase.Translating ? "traduzindo" : "avaliando com o juiz")}...";

    private static int WriteDryRun(TextWriter output, bool json, EvalEstimate estimate, ExchangeRate rate)
    {
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                dryRun = true,
                sampleLines = estimate.SampleLines,
                goldLines = estimate.GoldLines,
                dollars = estimate.Dollars,
                configs = estimate.Configs.Select(c => new
                {
                    id = c.Config.Id,
                    model = c.Config.Model,
                    effort = c.Config.Effort,
                    contextLines = c.Config.ContextLines,
                    withBible = c.Config.WithBible,
                    inputTokens = c.Estimate.Usage.Input,
                    outputTokens = c.Estimate.Usage.Output,
                    dollars = c.Estimate.Dollars,
                    judgeDollars = c.JudgeDollars,
                }),
            }));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Estimativa do banco de provas: {EvalText.CountLines(estimate.SampleLines)} sorteadas de {EvalText.Number(estimate.GoldLines)} aprovadas, em {Count(estimate.Configs.Count, "configuração", "configurações")}.");
        foreach (var config in estimate.Configs)
        {
            output.WriteLine($"  {config.Config.Id}  {config.Config.Title} · {config.Config.ContextText}: {EvalText.Brl(rate.ToBrl(config.Dollars))}");
        }

        output.WriteLine($"Custo aproximado da rodada: {EvalText.Brl(rate.ToBrl(estimate.Dollars))} ({Dollars(estimate.Dollars)}).");
        output.WriteLine("É uma ordem de grandeza: o custo real aparece ao fim de 'vnf eval'.");
        return ErrorHandler.OkExitCode;
    }

    private static int WriteReport(TextWriter output, bool json, EvalRound round, ExchangeRate rate)
    {
        var recommended = EvalReading.Recommended(round);
        var anyAnswered = round.PairsDone > 0;
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                round = round.Id,
                sampleLines = round.Sample.Count,
                goldLines = round.GoldLines,
                remainingLines = round.RemainingLines,
                judgeModel = round.JudgeModel,
                pairs = round.Pairs.Count,
                pairsDone = round.PairsDone,
                recommended = recommended?.Config.Id,
                chosen = round.ChosenConfigId,
                reading = EvalReading.Sentence(round),
                configs = round.Reports.Select(r =>
                {
                    var blind = round.BlindScore(r.Config.Id);
                    return new
                    {
                        id = r.Config.Id,
                        model = r.Config.Model,
                        effort = r.Config.Effort,
                        contextLines = r.Config.ContextLines,
                        withBible = r.Config.WithBible,
                        chrf = r.MeanChrf,
                        judge = r.MeanJudge,
                        refusedLines = r.RefusedLines,
                        dollars = r.Dollars,
                        judgeDollars = r.JudgeDollars,
                        projectedDollars = r.ProjectedDollars(round.RemainingLines),
                        projectedBatchDollars = r.ProjectedBatchDollars(round.RemainingLines),
                        projectedSeconds = r.ProjectedTime(round.RemainingLines).TotalSeconds,
                        blindWins = blind.Wins,
                        blindPairs = blind.Total,
                        error = r.Error,
                    };
                }),
            }));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Rodada {round.Id}: {EvalText.CountLines(round.Sample.Count)} sorteadas de {EvalText.Number(round.GoldLines)} aprovadas, a mesma amostra em todas as configurações.");
        foreach (var report in round.Reports)
        {
            var (normal, batch) = EvalText.GameCost(report, round.RemainingLines, rate);
            var judge = report.MeanJudge is { } score ? EvalText.Score(score) : "sem juiz";
            var chosen = round.ChosenConfigId == report.Config.Id ? " · escolhida para o M4" : string.Empty;
            output.WriteLine($"  {report.Config.Id}  {report.Config.ModelName} · {report.Config.Detail}{chosen}");
            output.WriteLine(
                $"      jogo todo {EvalText.Brl(normal)} (lote {EvalText.Brl(batch)}) · {EvalText.Duration(report.ProjectedTime(round.RemainingLines))} · chrF {EvalText.Score(report.MeanChrf)} · juiz {judge} · às cegas {EvalText.Blind(round.BlindScore(report.Config.Id), anyAnswered)}");
            if (report.RefusedLines > 0 || report.Error is not null)
            {
                output.WriteLine($"      {Count(report.RefusedLines, "linha sem tradução", "linhas sem tradução")}{(report.Error is { } problem ? $": {problem}" : string.Empty)}");
            }
        }

        output.WriteLine("chrF: quanto a tradução se parece com a aprovada, de 0 a 100. Juiz de IA: nota de 0 a 10, sem saber qual configuração gerou o texto.");
        output.WriteLine($"O custo e o tempo do jogo todo valem para as {EvalText.CountLines(round.RemainingLines)} que faltam, com o preço de hoje.");
        if (EvalReading.Sentence(round) is { Length: > 0 } sentence)
        {
            output.WriteLine(sentence);
        }

        if (recommended is not null && round.ChosenConfigId is null)
        {
            output.WriteLine($"Sugestão para o M4: {recommended.Config.Id}. Confirme com 'vnf eval --use {recommended.Config.Id}'.");
        }

        return ErrorHandler.OkExitCode;
    }
}
