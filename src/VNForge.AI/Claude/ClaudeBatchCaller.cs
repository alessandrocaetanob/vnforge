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

    /// <param name="verb">O que o modelo recusa fazer, no infinitivo ("traduzir"); entra na mensagem de recusa.</param>
    /// <param name="parse">Lê a resposta e confere os ids do lote.</param>
    /// <param name="refused">O resultado de uma linha que o modelo recusou sozinha.</param>
    public Task<IReadOnlyList<T>> RunAsync<T>(
        TranslationBatch batch,
        CancellationToken ct,
        string schema,
        string verb,
        Func<TranslationBatch, string> buildSystem,
        Func<TranslationBatch, string> buildUser,
        Func<string, TranslationBatch, IReadOnlyList<T>> parse,
        Func<TextUnit, T> refused)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ct.ThrowIfCancellationRequested();
        return ClaudePromptParts.WithRefusalFallback(batch, b => OnceAsync(b, ct, schema, verb, buildSystem, buildUser, parse), refused);
    }

    private async Task<IReadOnlyList<T>> OnceAsync<T>(
        TranslationBatch batch,
        CancellationToken ct,
        string schema,
        string verb,
        Func<TranslationBatch, string> buildSystem,
        Func<TranslationBatch, string> buildUser,
        Func<string, TranslationBatch, IReadOnlyList<T>> parse)
    {
        var request = new ClaudeRequest(options.Model, options.Effort, options.MaxTokens, buildSystem(batch), buildUser(batch), schema);
        var reply = await client.CompleteAsync(request, ct).ConfigureAwait(false);

        if (reply.Usage is { } usage)
            lock (_usageLock)
                _usage += usage;

        if (reply.StopReason == "refusal")
            throw new ClaudeRefusalException($"O modelo recusou {verb} este lote (stop_reason: refusal).");
        if (reply.StopReason == "max_tokens")
            throw new ClaudeResponseException("A resposta foi cortada por limite de tokens; reduza o lote.");

        return parse(reply.Text, batch);
    }
}
