using VNForge.AI.BatchRequest;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.App.Controls;
using VNForge.Core;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Search;

namespace VNForge.App.Services;

/// <summary>A cotação, o limite de gasto, a qualidade, as chamadas em paralelo e a chave de um pedido em lote. <paramref name="Delay"/> troca a espera entre tentativas (os testes não esperam).</summary>
public sealed record BatchRequestSettings(ExchangeRate Rate, BudgetLimits Limits, TranslationQuality Quality, int Concurrency, bool ApiKeyConfigured, Func<TimeSpan, CancellationToken, Task>? Delay = null);

/// <summary>
/// O que o pedido vai fazer, antes de gastar: as falas que a IA lerá (já filtradas no Core pelo que o pedido cita) e a estimativa de custo.
/// A pessoa confirma esta tela; só então a IA é chamada.
/// </summary>
/// <param name="Request">O pedido como foi escrito.</param>
/// <param name="Scope">O que o pedido cita: personagens, cenas e a palavra a trocar.</param>
/// <param name="Candidates">As falas traduzidas que a IA lerá.</param>
/// <param name="SpeakerNames">Os nomes (traduzidos) dos personagens citados.</param>
/// <param name="Model">O modelo da qualidade escolhida nos Ajustes.</param>
/// <param name="Brl">Estimativa de gasto em reais, um teto.</param>
/// <param name="Rate">A cotação usada em <paramref name="Brl"/>.</param>
public sealed record BatchRequestPlan(string Request, BatchRequestScope Scope, IReadOnlyList<BatchCandidate> Candidates, IReadOnlyList<string> SpeakerNames, string Model, decimal Brl, ExchangeRate Rate)
{
    public string CostText => Money.BrlEstimate(Brl, Rate);
}

/// <summary>Como terminou a chamada da IA: a prévia (nada gravado) e o que ficou de fora.</summary>
public sealed record BatchRequestResult(ReplacePreview Preview, BatchRequestOutcome Outcome, decimal SpentBrl);

/// <summary>O pedido não pode seguir (sem chave, sem falas, limite de gasto). A mensagem vai direto para a pessoa.</summary>
public sealed class BatchRequestException(string message) : VNForgeException(message);

/// <summary>
/// O pedido em lote da paleta de comandos: o Core acha as falas que o pedido alcança, a IA as reescreve e o resultado é uma <see cref="ReplacePreview"/>,
/// a mesma do painel de busca e substituição. Aqui nada é gravado; <see cref="Plan"/> não gasta, <see cref="RunAsync"/> gasta e registra o gasto no projeto.
/// </summary>
/// <param name="providerFactory">Monta o provedor para (modelo, esforço); os testes passam o provedor falso.</param>
/// <param name="settings">Lido a cada pedido, para valer a cotação e o limite de agora.</param>
/// <param name="usageOf">Os tokens que o provedor já gastou; o padrão lê o provedor Claude.</param>
public sealed class BatchRequestService(
    Func<string, ClaudeEffort, IBatchRequestProvider> providerFactory,
    Func<BatchRequestSettings> settings,
    Func<IBatchRequestProvider, ClaudeUsage?>? usageOf = null)
{
    /// <summary>Trocar uma palavra e ajustar a frase não pede raciocínio longo; o esforço baixo mantém o gasto perto da estimativa.</summary>
    public const ClaudeEffort Effort = ClaudeEffort.Low;

    private readonly Func<IBatchRequestProvider, ClaudeUsage?> usageOf = usageOf ?? (provider => (provider as ClaudeBatchRequestProvider)?.Usage);

    /// <summary>Acha as falas do pedido e estima o custo, sem chamar a IA.</summary>
    /// <exception cref="BatchRequestException">Pedido vazio, sem chave, sem falas que ele alcance ou com o limite de gasto atingido.</exception>
    public BatchRequestPlan Plan(ProjectFolder project, string request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);
        request = request?.Trim() ?? string.Empty;
        if (request.Length == 0)
        {
            throw new BatchRequestException("Escreva o que a IA deve fazer, por exemplo: nas falas do Kenta, trocar você por tu.");
        }

        var current = settings();
        if (!current.ApiKeyConfigured)
        {
            throw new BatchRequestException("O pedido em lote usa a IA e falta a chave da Anthropic. Informe a chave em Ajustes.");
        }

        var texts = project.LoadAllText();
        var characters = project.LoadCharacters();
        var scope = BatchRequestScope.Parse(request, characters, texts.Keys);
        var candidates = scope.Select(texts, characters, ct);
        if (candidates.Count == 0)
        {
            throw new BatchRequestException(scope.IsNarrowed
                ? "Nenhuma fala traduzida bate com o que o pedido cita. Confira o nome do personagem, a cena ou a palavra."
                : "O projeto ainda não tem fala traduzida para reescrever.");
        }

        var model = QualityProfiles.For(current.Quality).Model;
        var (builder, batchSize) = Builder(project);
        var dollars = BatchRequestRunner.Estimate([.. candidates.Select(c => c.Unit)], builder, model, Effort, batchSize).Dollars;
        var brl = current.Rate.ToBrl(dollars);
        if (new RunCostTracker(project, model, current.Rate, () => null).CreateGuard(current.Limits, _ => brl, null).LimitReached)
        {
            throw new BatchRequestException("O limite de gasto do projeto foi atingido. Aumente o limite em Ajustes para pedir à IA.");
        }

        var names = characters.Where(c => scope.Speakers.Contains(c.Source, CharacterBook.NameComparer)).Select(c => c.Target).ToList();
        return new BatchRequestPlan(request, scope, candidates, names, model, brl, current.Rate);
    }

    /// <summary>Manda as falas do <paramref name="plan"/> à IA e devolve a prévia. O gasto fica registrado no projeto mesmo se o pedido for cancelado.</summary>
    /// <exception cref="BatchRequestException">Sem provedor de IA, ou nenhum lote voltou.</exception>
    /// <exception cref="OperationCanceledException">A pessoa cancelou antes de qualquer linha voltar; nada foi gravado. Com linhas prontas, a prévia as traz e <see cref="BatchRequestOutcome.Canceled"/> fica verdadeiro.</exception>
    public async Task<BatchRequestResult> RunAsync(ProjectFolder project, BatchRequestPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(plan);
        var current = settings();
        var (builder, batchSize) = Builder(project);

        IBatchRequestProvider provider;
        try
        {
            provider = providerFactory(plan.Model, Effort);
        }
        catch (InvalidOperationException e)
        {
            throw new BatchRequestException(e.Message);
        }

        var tracker = new RunCostTracker(project, plan.Model, current.Rate, () => usageOf(provider));
        var guard = tracker.CreateGuard(
            current.Limits,
            lines => current.Rate.ToBrl(BatchRequestRunner.Estimate(lines, builder, plan.Model, Effort, batchSize).Dollars),
            (_, _) => Task.FromResult(true));
        var runner = new BatchRequestRunner(
            provider,
            builder,
            new TranslationRunOptions { BatchSize = batchSize, MaxConcurrency = Math.Max(1, current.Concurrency), Budget = guard, Delay = current.Delay ?? new TranslationRunOptions().Delay });

        BatchRequestOutcome outcome;
        try
        {
            outcome = await runner.RunAsync(plan.Request, plan.Candidates, ct).ConfigureAwait(false);
        }
        finally
        {
            tracker.Record();
        }

        // Cancelar depois de um lote voltar não joga fora o que já foi pago: a prévia fica com essas linhas e o resumo diz que foi cancelado.
        if (outcome.Canceled && outcome.Items.Count == 0)
        {
            ct.ThrowIfCancellationRequested();
        }

        if (outcome.Items.Count == 0 && outcome.Failed > 0 && outcome.Problem is not null)
        {
            throw new BatchRequestException($"A IA não respondeu: {outcome.Problem}");
        }

        var preview = new ReplacePreview(new SearchOptions(plan.Request, Field: SearchField.Translation), plan.Request, outcome.Items, 0, [], 0);
        return new BatchRequestResult(preview, outcome, tracker.RunBrl);
    }

    private static (TranslationBatchBuilder Builder, int BatchSize) Builder(ProjectFolder project)
    {
        var config = project.LoadConfig();
        var builder = new TranslationBatchBuilder(
            project.LoadGlossary(),
            new CharacterBook(project.LoadCharacters()),
            TranslationBatchBuilder.ReadStyleGuide(Path.Combine(project.Root, TranslationContextSummary.StyleGuideFileName)),
            style: config.Style);
        return (builder, Math.Clamp(config.Ai.BatchSize, TranslationRunOptions.MinBatchSize, TranslationRunOptions.MaxBatchSize));
    }
}
