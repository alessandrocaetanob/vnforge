using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Dictionaries;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

/// <summary>O pré-dicionário no caminho da IA: o lote leva o original normalizado e a chave do cache lê o que a IA lê (#539).</summary>
public class PreDictionaryTests
{
    private static readonly IReadOnlyList<GlossaryTerm> NoTerms = [];

    private static readonly CharacterBook NoCast = new([]);

    private static TextUnit Unit(string id, string source, string? speaker = null) =>
        new(id, TextUnitKind.Dialogue, speaker, source, null, TranslationStatus.Untranslated);

    private static TranslationBatchBuilder Builder(params DictionaryRule[] pre) =>
        new(NoTerms, NoCast) { Pre = new PreDictionary(pre) };

    private static string Key(TranslationBatchBuilder builder, TextUnit unit)
    {
        var batch = builder.Build([unit]);
        return TranslationCache.KeyFor(batch.Lines[0], TranslationCache.ContextFor("claude-opus-5-5", batch));
    }
}
