using VNForge.Core.Glossary;

namespace VNForge.Core.Vndb;

/// <summary>Um personagem do VNDB como o projeto o guarda em <c>vndb.json</c>, com os textos já limpos (<see cref="VndbText.Clean"/>).</summary>
/// <param name="Id">Id do VNDB (<c>c1234</c>).</param>
/// <param name="Name">Nome em letras latinas.</param>
/// <param name="Original">Nome na escrita original, ou nulo quando o VNDB não o tem.</param>
/// <param name="Aliases">Apelidos.</param>
/// <param name="Gender">O gênero real do personagem (o que concorda na tradução), ou <see cref="CharacterGender.Unknown"/> quando o VNDB não diz.</param>
/// <param name="Description">Descrição sem a marcação do VNDB, ou nula.</param>
/// <param name="Role"><c>main</c>, <c>primary</c>, <c>side</c> ou <c>appears</c>: o maior papel dele neste jogo.</param>
/// <param name="Image">A imagem do personagem no VNDB, ou nula quando ele não tem; <c>vndb.json</c> de antes dela abre com nulo.</param>
public sealed record VndbLinkedCharacter(
    string Id,
    string Name,
    string? Original,
    IReadOnlyList<string> Aliases,
    CharacterGender Gender,
    string? Description,
    string Role,
    VndbImageRef? Image = null);

/// <summary>
/// O jogo do VNDB que o usuário confirmou para o projeto, gravado em <c>vndb.json</c>. Guarda a sinopse, que a bíblia do jogo lê, e todos os personagens do VNDB;
/// os que casam com os nomes do jogo (<c>ES.CHAR.NAME</c>) vão para <c>characters.json</c> em <see cref="VndbProjectLinker.ApplyCharacters"/>.
/// </summary>
/// <param name="VisualNovelId">Id do VNDB (<c>v1234</c>).</param>
/// <param name="Title">Título em letras latinas.</param>
/// <param name="OriginalTitle">Título na escrita original, ou nulo.</param>
/// <param name="Synopsis">Sinopse sem a marcação do VNDB e sem spoilers, ou nula. Fica no idioma do VNDB (em geral inglês).</param>
/// <param name="Characters">Os personagens do jogo no VNDB.</param>
/// <param name="CharactersTruncated">O VNDB tinha mais personagens do que o cliente trouxe.</param>
/// <param name="Cover">A capa do jogo no VNDB (a referência, não os bytes), que o cabeçalho do jogo mostra; nula nos projetos ligados antes dela ser guardada ou quando o VNDB não tem.</param>
public sealed record VndbLink(
    string VisualNovelId,
    string Title,
    string? OriginalTitle,
    string? Synopsis,
    IReadOnlyList<VndbLinkedCharacter> Characters,
    bool CharactersTruncated = false,
    VndbImageRef? Cover = null);

/// <summary>O jogo do VNDB e a evidência de que é o jogo da pasta: <see cref="Evidence"/> separa o "só o título" do "combina com a pasta e o exe".</summary>
public sealed record VndbCandidate(VndbVisualNovel VisualNovel, VndbEvidence Evidence);

/// <summary>O quanto o VNDB confirma que um resultado é o jogo da pasta.</summary>
public enum VndbEvidence
{
    /// <summary>Só o título da busca casou.</summary>
    TitleOnly,

    /// <summary>Além da busca, o nome da pasta do jogo ou do executável aparece num dos títulos do VNDB.</summary>
    FolderAndExe,
}
