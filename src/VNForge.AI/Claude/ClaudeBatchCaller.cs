using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Claude;

/// <summary>
/// Uma chamada ao Claude para um lote: monta o pedido, soma os tokens, trata <c>refusal</c> e <c>max_tokens</c> e devolve o texto
/// pronto para o provedor ler. É o que os provedores de tradução e de revisão têm em comum.
/// </summary>
internal sealed class ClaudeBatchCaller(IClaudeMessageClient client, ClaudeOptions options)
{
    private readonly Lock _usageLock = new();
    private ClaudeUsage _usage = new(0, 0, 0, 0);

    /// <summary>Tokens acumulados de todas as chamadas, recusas incluídas, com os lidos do cache.</summary>
    public ClaudeUsage Usage
    {
        get { lock (_usageLock) return _usage; }
    }

    public Task<IReadOnlyList<T>> RunAsync<T>(TranslationBatch batch, CancellationToken ct, ClaudeBatchSpec<T> spec)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ct.ThrowIfCancellationRequested();
        return ClaudePromptParts.WithRefusalFallback(batch, b => OnceAsync(b, ct, spec), spec.Refused);
    }

    private async Task<IReadOnlyList<T>> OnceAsync<T>(TranslationBatch batch, CancellationToken ct, ClaudeBatchSpec<T> spec)
    {
        var request = new ClaudeRequest(options.Model, options.Effort, options.MaxTokens, spec.BuildSystem(batch), spec.BuildUser(batch), spec.Schema);
        var reply = await client.CompleteAsync(request, ct).ConfigureAwait(false);

        if (reply.Usage is { } usage)
            lock (_usageLock)
                _usage += usage;

        if (reply.StopReason == "refusal")
            throw new ClaudeRefusalException($"O modelo recusou {spec.Verb} este lote (stop_reason: refusal).");
        if (reply.StopReason == "max_tokens")
            throw new ClaudeResponseException("A resposta foi cortada por limite de tokens; reduza o lote.");

        return spec.Parse(reply.Text, batch);
    }
}

/// <summary>O que muda de um provedor para outro: prompt, schema, leitura da resposta e o resultado de uma linha recusada.</summary>
/// <param name="Verb">O que o modelo recusa fazer, no infinitivo ("traduzir"); entra na mensagem de recusa.</param>
/// <param name="Parse">Lê a resposta e confere os ids do lote.</param>
/// <param name="Refused">O resultado de uma linha que o modelo recusou sozinha.</param>
internal sealed record ClaudeBatchSpec<T>(
    string Schema,
    string Verb,
    Func<TranslationBatch, string> BuildSystem,
    Func<TranslationBatch, string> BuildUser,
    Func<string, TranslationBatch, IReadOnlyList<T>> Parse,
    Func<TextUnit, T> Refused);
