namespace VNForge.AI.Claude;

/// <summary>Resultado do "Testar conexão" dos Ajustes: o modelo usado e uma frase em português para mostrar ao usuário.</summary>
public sealed record ConnectionTestResult(bool Success, string Model, string Message);

/// <summary>
/// Teste de conexão da chave: um pedido mínimo ao modelo mais barato (Haiku), com poucos tokens de saída. Qualquer resposta da API
/// prova que a chave vale e que a rede chega lá, mesmo que o texto volte vazio ou o modelo recuse; falha de chave, limite ou rede
/// vira a mesma frase em português que o resto do app usa (<see cref="ClaudeErrorMessages"/>). A chave nunca entra na mensagem.
/// </summary>
public static class ClaudeConnectionTest
{
    private const string Schema = """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""";

    public static async Task<ConnectionTestResult> RunAsync(IClaudeMessageClient client, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        var request = new ClaudeRequest(ClaudeModels.Haiku, ClaudeEffort.Low, 256, "Responda só com o JSON pedido.", "Responda {\"ok\": true}.", Schema);
        var name = ModelName(ClaudeModels.Haiku);
        try
        {
            await client.CompleteAsync(request, ct).ConfigureAwait(false);
            return new ConnectionTestResult(true, ClaudeModels.Haiku, $"Conexão funcionando: a API respondeu pelo {name}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return new ConnectionTestResult(false, ClaudeModels.Haiku, ClaudeErrorMessages.Describe(e));
        }
    }

    // "claude-haiku-5-5" vira "Claude Haiku 5.5"; a tela usa ProjectLabels.Model, mas esta camada não depende do app.
    private static string ModelName(string id)
    {
        var parts = id.Split('-');
        return $"Claude {char.ToUpperInvariant(parts[1][0])}{parts[1][1..]} {string.Join('.', parts[2..])}";
    }
}
