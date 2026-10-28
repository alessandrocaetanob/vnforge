using VNForge.AI.Translation;

namespace VNForge.AI.BatchRequest;

/// <summary>A reescrita de uma linha a pedido do tradutor.</summary>
/// <param name="Id">Id da <see cref="VNForge.Core.Text.TextUnit"/> reescrita.</param>
/// <param name="Translation">A tradução inteira já reescrita, com os mesmos marcadores de controle da tradução de antes.</param>
/// <param name="Refused">O modelo recusou esta linha mesmo após o fallback; <paramref name="Translation"/> fica vazia.</param>
public sealed record BatchRewrite(string Id, string Translation, bool Refused = false);

/// <summary>
/// Reescreve as traduções de um lote conforme um pedido escrito em português ("trocar você por tu"). É a fronteira entre o pedido em lote e o
/// provedor de IA: o provedor Claude e o <see cref="FakeBatchRequestProvider"/> dos testes a implementam.
/// </summary>
public interface IBatchRequestProvider
{
    /// <summary>
    /// Decide, para cada linha do <paramref name="batch"/>, se o <paramref name="request"/> se aplica a ela. Devolve só as linhas que mudam,
    /// com o mesmo <see cref="BatchRewrite.Id"/>; linha que o pedido não alcança fica fora da resposta.
    /// </summary>
    Task<IReadOnlyList<BatchRewrite>> RewriteAsync(string request, TranslationBatch batch, CancellationToken ct);
}
