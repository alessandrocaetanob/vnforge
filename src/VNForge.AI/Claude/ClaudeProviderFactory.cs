using Anthropic;

namespace VNForge.AI.Claude;

/// <summary>Monta o provedor Claude real; é o único ponto fora de <c>AnthropicMessageClient</c> que conhece o SDK.</summary>
public static class ClaudeProviderFactory
{
    /// <param name="apiKey">Chave já resolvida (ambiente ou cofre); nunca vem de arquivo do projeto.</param>
    public static ClaudeTranslationProvider Create(string apiKey, ClaudeOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(options);
        return new ClaudeTranslationProvider(new AnthropicMessageClient(new AnthropicClient { ApiKey = apiKey }), options);
    }
}
