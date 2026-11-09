using VNForge.Core;
using VNForge.Core.Dictionaries;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Services;

/// <summary>
/// A sugestão de uma linha com o original que a IA leria no lote: o pré-dicionário do projeto normaliza o <see cref="TextUnit.Source"/> antes do pedido,
/// como o <c>TranslationBatchBuilder</c> faz na tradução em lote. O resultado volta pelo mesmo id da linha.
/// </summary>
public sealed class PreDictionarySuggester(ILineSuggester inner, PreDictionary preDictionary) : ILineSuggester
{
    /// <summary>Envolve <paramref name="inner"/> com o pré-dicionário do projeto em <paramref name="root"/>; sem regra ligada, ou com o <c>formatting.json</c> ilegível, devolve <paramref name="inner"/>.</summary>
    public static ILineSuggester For(ILineSuggester inner, string? root)
    {
        if (root is null)
        {
            return inner;
        }

        try
        {
            var pre = PreDictionary.For(ProjectFolder.Open(root));
            return pre.IsEmpty ? inner : new PreDictionarySuggester(inner, pre);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return inner;
        }
    }

    public Task<LineSuggestion> SuggestAsync(TextUnit unit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(unit);
        try
        {
            return inner.SuggestAsync(preDictionary.Apply(unit), ct);
        }
        catch (FormattingException)
        {
            return inner.SuggestAsync(unit, ct);
        }
    }
}
