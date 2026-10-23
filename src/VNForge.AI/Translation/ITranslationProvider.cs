namespace VNForge.AI.Translation;

/// <summary>
/// Traduz um lote de linhas. É a fronteira entre o fluxo de tradução e o provedor de IA: o provedor Claude e o
/// <see cref="FakeTranslationProvider"/> dos testes a implementam, e nada fora de <c>VNForge.AI</c> conhece o SDK.
/// </summary>
public interface ITranslationProvider
{
    /// <summary>
    /// Traduz as linhas de <paramref name="batch"/>. Devolve um <see cref="TranslationResult"/> por linha, na ordem do lote,
    /// com o mesmo <see cref="TranslationResult.Id"/> e os mesmos marcadores de controle do original.
    /// </summary>
    Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct);
}
