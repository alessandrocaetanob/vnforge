using VNForge.Core;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Os personagens e o glossário do projeto, que o Editor consulta para mostrar o falante em português e o contexto da linha.</summary>
public sealed record EditorContext(CharacterBook Characters, IReadOnlyList<GlossaryTerm> Glossary)
{
    public static EditorContext Empty { get; } = new(new CharacterBook([]), []);

    /// <summary>Lê <c>characters.json</c> e <c>glossary.json</c> da pasta; um arquivo que não abre deixa aquela parte vazia, sem derrubar o Editor.</summary>
    public static EditorContext Read(string root)
    {
        ProjectFolder folder;
        try
        {
            folder = ProjectFolder.Open(root);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return Empty;
        }

        return new EditorContext(new CharacterBook(Try(folder.LoadCharacters)), Try(folder.LoadGlossary));
    }

    /// <summary>O contexto de uma linha: o falante cadastrado e os termos do glossário que aparecem no original.</summary>
    public LineContext For(string? speaker, string source)
    {
        var character = Characters.Find(speaker);
        return new LineContext(character?.Target, character?.Tone, GlossaryMatcher.Find(source, Glossary), character is { Gender: CharacterGender.Unknown });
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

/// <summary>O que o inspetor sabe sobre a linha além dela mesma.</summary>
/// <param name="SpeakerTarget">O nome do falante em português; nulo quando o personagem não está cadastrado.</param>
/// <param name="Voice">O tom de voz do personagem (<c>characters.json</c>); nulo quando não há.</param>
/// <param name="Glossary">Os termos do glossário que aparecem no original, na ordem do glossário.</param>
/// <param name="GenderMissing">O falante está cadastrado sem gênero definido, e a concordância da tradução pode sair errada.</param>
public sealed record LineContext(string? SpeakerTarget, string? Voice, IReadOnlyList<GlossaryTerm> Glossary, bool GenderMissing = false);
