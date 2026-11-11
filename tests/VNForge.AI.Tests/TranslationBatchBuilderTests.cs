using VNForge.AI.Translation;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

public class TranslationBatchBuilderTests
{
    private static TextUnit Unit(string id, string source, string? speaker = null, string? translation = null) =>
        new(id, TextUnitKind.Dialogue, speaker, source, translation,
            translation is null ? TranslationStatus.Untranslated : TranslationStatus.Translated);

    private static readonly GlossaryTerm[] Terms =
    [
        new("魔法学園", "Academia de Magia", "só o nome da escola"),
        new("先輩", "senpai"),
        new("勇者", "herói"),
    ];

    private static readonly CharacterBook Book = new(
    [
        new CharacterEntry("アリス", "Alice", CharacterGender.Female, "formal"),
        new CharacterEntry("ボブ", "Bob"),
    ]);
}
