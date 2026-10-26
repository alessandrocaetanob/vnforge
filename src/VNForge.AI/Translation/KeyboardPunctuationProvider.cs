using VNForge.Core.Projects;

namespace VNForge.AI.Translation;

/// <summary>
/// Envolve um provedor e troca a pontuação japonesa que a IA copia do original (<c>。 ～ （</c>) pela do teclado em cada tradução devolvida
/// (<see cref="KeyboardPunctuation"/>). Vai por fora do cache: o cache guarda o que a IA respondeu e a opção do projeto vale na hora de gravar.
/// </summary>
public sealed class KeyboardPunctuationProvider(ITranslationProvider inner, KeyboardPunctuation punctuation) : ITranslationProvider
{
    /// <summary>O <paramref name="provider"/> com a troca do projeto; o próprio <paramref name="provider"/> quando a opção está desligada.</summary>
    public static ITranslationProvider Wrap(ITranslationProvider provider, ProjectConfig config)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return KeyboardPunctuation.For(config) is { } punctuation ? new KeyboardPunctuationProvider(provider, punctuation) : provider;
    }

    public async Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
    {
        var results = await inner.TranslateAsync(batch, ct).ConfigureAwait(false);
        return [.. results.Select(r => r.Refused || r.Translation.Length == 0 ? r : r with { Translation = punctuation.Apply(r.Translation) })];
    }
}
