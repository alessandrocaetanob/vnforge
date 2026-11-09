using Anthropic.Exceptions;

namespace VNForge.AI.Claude;

/// <summary>Traduz as falhas do provedor Claude (SDK, rede, recusa) em frases em português para o usuário; nada fora de <c>VNForge.AI</c> precisa conhecer o SDK.</summary>
public static class ClaudeErrorMessages
{
    /// <summary>O erro não passa sozinho: chave recusada (401, 403) ou recurso que não existe (404). Repetir o pedido só repete o erro.</summary>
    public static bool IsPermanent(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error is AnthropicUnauthorizedException or AnthropicForbiddenException or AnthropicNotFoundException;
    }

    public static string Describe(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error switch
        {
            AnthropicUnauthorizedException or AnthropicForbiddenException => "A chave de API foi recusada. Confira a chave em Ajustes.",
            AnthropicRateLimitException => "O limite de uso da API foi atingido. Tente de novo em instantes.",
            Anthropic5xxException => "O serviço da Anthropic está indisponível no momento. Tente de novo em instantes.",
            AnthropicIOException or HttpRequestException or TimeoutException or OperationCanceledException => "Falha de rede ou tempo esgotado ao falar com a IA. Confira a conexão e tente de novo.",
            ClaudeRefusalException or ClaudeResponseException => error.Message,
            AnthropicApiException => "A API da Anthropic recusou o pedido.",
            _ => "Erro inesperado ao falar com a IA.",
        };
    }
}
