using VNForge.App.Controls;
using VNForge.Core;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Spelling;
using VNForge.Core.Vndb;

namespace VNForge.App.ViewModels;

/// <summary>Os personagens e o glossário do projeto, que o Editor consulta para mostrar o falante em português e o contexto da linha.</summary>
public sealed record EditorContext(CharacterBook Characters, IReadOnlyList<GlossaryTerm> Glossary)
{
    /// <summary>Os tratamentos entre personagens (<c>address-terms.json</c>), que o corretor aceita como palavras do projeto ("Ken-chan").</summary>
    public IReadOnlyList<AddressTerm> AddressTerms { get; init; } = [];

    /// <summary>O papel de cada personagem do VNDB neste jogo (<c>main</c>, <c>primary</c>, <c>side</c>, <c>appears</c>), por id (<c>c1234</c>), lido de <c>vndb.json</c>; vazio sem ligação.</summary>
    public IReadOnlyDictionary<string, string> VndbRoles { get; init; } = new Dictionary<string, string>();

    /// <summary>As palavras que o revisor mandou o corretor ignorar (<c>ignored-words.json</c>).</summary>
    public IReadOnlyList<IgnoredWord> IgnoredWords { get; init; } = [];

    /// <summary>As palavras do projeto que o corretor aceita: traduções do glossário, dos personagens e dos tratamentos, e as ignoradas.</summary>
    public ProjectDictionary SpellWords() => ProjectDictionary.Create(Glossary, Characters.Entries, AddressTerms, IgnoredWords.Select(w => w.Word));

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

        return new EditorContext(new CharacterBook(Try(folder.LoadCharacters)), Try(folder.LoadGlossary)) { AddressTerms = Try(folder.LoadAddressTerms), IgnoredWords = Try(folder.LoadIgnoredWords), VndbRoles = ReadRoles(folder) };
    }

    /// <summary>O contexto de uma linha: o falante cadastrado e os termos do glossário que aparecem no original.</summary>
    public LineContext For(string? speaker, string source)
    {
        var character = Characters.Find(speaker);
        return new LineContext(character?.Target, character?.Tone, GlossaryMatcher.Find(source, Glossary), character is { Gender: CharacterGender.Unknown }, CharacterPalette.For(character))
        {
            SpeakerImage = character?.Image,
            SpeakerRole = character is { VndbId: { } id } && VndbRoles.TryGetValue(id, out var role) ? RoleLabel(role, character.Gender) : null,
        };
    }

    /// <summary>"Protagonista", "Principal", "Secundária"/"Secundário" (concorda com o gênero) ou "Aparece": o papel do VNDB, como o cabeçalho do inspetor o diz.</summary>
    public static string RoleLabel(string role, CharacterGender gender) => role switch
    {
        "main" => "Protagonista",
        "primary" => "Principal",
        "side" => gender == CharacterGender.Female ? "Secundária" : "Secundário",
        _ => "Aparece",
    };

    private static Dictionary<string, string> ReadRoles(ProjectFolder folder)
    {
        try
        {
            return folder.LoadVndbLink()?.Characters.ToDictionary(c => c.Id, c => c.Role, StringComparer.Ordinal) ?? [];
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
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
/// <param name="SpeakerColor">A cor do falante cadastrado (#467); nula quando ele não está no Glossário.</param>
public sealed record LineContext(string? SpeakerTarget, string? Voice, IReadOnlyList<GlossaryTerm> Glossary, bool GenderMissing = false, CharacterColor? SpeakerColor = null)
{
    /// <summary>A imagem do falante no VNDB, para o retrato do cabeçalho do inspetor; nula sem imagem (o retrato desenhado vale).</summary>
    public VndbImageRef? SpeakerImage { get; init; }

    /// <summary>O papel do falante no jogo ("Secundária"), quando o projeto está ligado ao VNDB; nulo sem ligação.</summary>
    public string? SpeakerRole { get; init; }
}
