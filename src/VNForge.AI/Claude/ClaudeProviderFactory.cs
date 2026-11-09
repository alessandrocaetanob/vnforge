using Anthropic;

namespace VNForge.AI.Claude;

/// <summary>Monta o provedor Claude real; é o único ponto fora de <c>AnthropicMessageClient</c> que conhece o SDK.</summary>
public static class ClaudeProviderFactory
{
    /// <summary>Cliente cru sobre o SDK, para chamadas que não são tradução (teste de conexão dos Ajustes); mesmas regras de <see cref="Create"/>.</summary>
    public static IClaudeMessageClient CreateClient(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return new AnthropicMessageClient(new AnthropicClient { ApiKey = apiKey });
    }

    /// <param name="apiKey">Chave já resolvida (ambiente ou cofre); nunca vem de arquivo do projeto.</param>
    public static ClaudeTranslationProvider Create(string apiKey, ClaudeOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(options);
        return new ClaudeTranslationProvider(new AnthropicMessageClient(new AnthropicClient { ApiKey = apiKey }), options);
    }

    /// <summary>Monta o provedor Claude da bíblia do jogo (<c>vnf bible generate</c>); mesmas regras de <see cref="Create"/>.</summary>
    public static VNForge.AI.Bible.ClaudeBibleProvider CreateBible(string apiKey, ClaudeOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(options);
        return new VNForge.AI.Bible.ClaudeBibleProvider(new AnthropicMessageClient(new AnthropicClient { ApiKey = apiKey }), options);
    }

    /// <summary>Monta o leitor de imagens do <c>vnf images scan</c>; mesmas regras de <see cref="Create"/>.</summary>
    public static VNForge.AI.Images.ClaudeImageTextReader CreateImageReader(string apiKey, ClaudeOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(options);
        return new VNForge.AI.Images.ClaudeImageTextReader(CreateClient(apiKey), options);
    }

    /// <summary>Monta o provedor Claude da revisão (<c>vnf review</c>); mesmas regras de <see cref="Create"/>.</summary>
    public static VNForge.AI.Review.ClaudeReviewProvider CreateReview(string apiKey, ClaudeOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(options);
        return new VNForge.AI.Review.ClaudeReviewProvider(new AnthropicMessageClient(new AnthropicClient { ApiKey = apiKey }), options);
    }
}
