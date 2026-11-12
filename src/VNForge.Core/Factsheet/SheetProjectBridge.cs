using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;

namespace VNForge.Core.Factsheet;

/// <summary>
/// O que a ficha levou para o projeto: os personagens do Glossário, se a bíblia ganhou a sinopse, e quantos textos ficaram de fora por ainda não terem a etapa pedida.
/// <paramref name="StaleCharacters"/> conta os personagens de ficha gravada antes de as edições serem guardadas: o spoiler deles é desconhecido, então a descrição não vai até a ficha ser buscada de novo.
/// </summary>
public sealed record SheetBridgeResult(VndbApplyResult Characters, bool SynopsisApplied, int NotReady, int StaleCharacters = 0);

/// <summary>
/// Leva a ficha para o projeto quando o jogo vira projeto de tradução: a descrição em português dos personagens vai para o <c>characters.json</c> do Glossário (casando
/// pelos nomes do jogo, como <see cref="VndbProjectLinker.ApplyCharacters(ProjectFolder)"/>) e a sinopse vai para a bíblia (<c>bible.json</c>). Como esses textos entram em
/// cada pedido de tradução, <b>nunca levam spoiler</b>: os trechos <c>[spoiler]</c> saem, e o personagem que o VNDB marca como spoiler naquele jogo entra só com o
/// nome e o gênero, sem descrição. Só vale o texto que já passou da etapa <c>minimum</c> (revisado, por padrão), para uma tradução da IA ainda não conferida não virar
/// contexto de todas as falas.
/// O que a pessoa já escreveu fica: a bíblia só ganha a sinopse se está sem uma, e o personagem só ganha a descrição se não tem, ou se a que tem é o original do VNDB
/// em inglês, que a tradução substitui.
/// </summary>
public static class SheetProjectBridge
{
    public static SheetBridgeResult Apply(
        ProjectFolder project, GameSheet game, IReadOnlyList<CharacterSheet> characters, TranslationStatus minimum = TranslationStatus.Reviewed)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(characters);
        var notReady = 0;

        var original = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        List<VndbLinkedCharacter> linked = [];
        foreach (var character in characters)
        {
            linked.Add(Link(character, game.VisualNovelId, minimum, original, ref notReady));
        }

        var applied = VndbProjectLinker.ApplyCharacters(
            project, linked, (current, vndb) => original.TryGetValue(vndb.Id, out var english) && english.Contains(current));

        var synopsisApplied = false;
        if (game.Synopsis is { } synopsis)
        {
            if (Ready(synopsis, minimum))
            {
                synopsisApplied = ApplySynopsis(project, synopsis);
            }
            else
            {
                notReady++;
            }
        }

        return new SheetBridgeResult(applied, synopsisApplied, notReady, characters.Count(c => c.HasUnknownSpoiler(game.VisualNovelId)));
    }

    private static VndbLinkedCharacter Link(
        CharacterSheet character, string visualNovelId, TranslationStatus minimum, Dictionary<string, HashSet<string>> original, ref int notReady)
    {
        var appearance = character.In(visualNovelId);
        string? description = null;
        if (character.Description is { } text && (appearance?.WorstSpoiler ?? 0) == 0)
        {
            if (Ready(text, minimum))
            {
                description = SheetMarkup.StripSpoilers(text.Target);
                original[character.Id] = [.. new[] { SheetMarkup.StripSpoilers(text.Source), VndbText.Clean(text.Source) }.OfType<string>()];
            }
            else
            {
                notReady++;
            }
        }

        return new VndbLinkedCharacter(
            character.Id,
            character.Name,
            character.Original,
            character.Aliases,
            VndbProjectLinker.GenderOf(character.Gender),
            description,
            appearance?.Role ?? "appears",
            character.Image);
    }

    private static bool ApplySynopsis(ProjectFolder project, SheetText synopsis)
    {
        if (SheetMarkup.StripSpoilers(synopsis.Target) is not { } plain)
        {
            return false;
        }

        var applied = false;
        project.UpdateBible(bible =>
        {
            applied = bible.Synopsis.Length == 0;
            return applied ? bible.WithGeneratedOverview(plain, bible.Timeline) : bible;
        });
        return applied;
    }

    private static bool Ready(SheetText text, TranslationStatus minimum) => text.HasTarget && text.Status >= minimum;
}
