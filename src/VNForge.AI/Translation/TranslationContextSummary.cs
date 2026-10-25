using VNForge.Core;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;

namespace VNForge.AI.Translation;

/// <summary>
/// O que vai junto em cada pedido de tradução, só contado: termos do glossário, personagens (com voz e gênero) e se há guia de estilo.
/// A janela "Traduzir com IA" mostra isso numa frase e avisa quando faltam gêneros.
/// </summary>
public sealed record TranslationContextSummary(int GlossaryTerms, int Characters, int CharactersWithVoice, int CharactersWithoutGender, bool HasStyleGuide)
{
    public const string StyleGuideFileName = "style.md";

    /// <summary>Lê <c>glossary.json</c>, <c>characters.json</c> e <c>style.md</c>; um arquivo que não abre conta como vazio, para a janela abrir mesmo assim.</summary>
    public static TranslationContextSummary Read(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var glossary = Try(project.LoadGlossary);
        var characters = Try(project.LoadCharacters);
        return new TranslationContextSummary(
            glossary.Count,
            characters.Count,
            characters.Count(c => !string.IsNullOrWhiteSpace(c.Tone)),
            characters.Count(c => c.Gender == CharacterGender.Unknown),
            TranslationBatchBuilder.ReadStyleGuide(Path.Combine(project.Root, StyleGuideFileName)) is { Length: > 0 });
    }

    private static IReadOnlyList<T> Try<T>(Func<IReadOnlyList<T>> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
