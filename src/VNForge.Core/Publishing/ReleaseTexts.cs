using System.Text;
using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Publishing;

/// <summary>Uma fala que mudou desde o patch anterior, com o texto de antes e o de agora.</summary>
/// <param name="SceneNumber">O "Cena N" da fala (<see cref="ChangedScene.Number"/>).</param>
/// <param name="LineNumber">A posição da fala na cena, a partir de 1, contando todas as falas na ordem do arquivo.</param>
/// <param name="Before">O texto que foi no patch anterior; nulo quando a fala não tinha tradução lá.</param>
/// <param name="After">O texto de agora, com o falante na frente quando há.</param>
public sealed record ChangeExample(int SceneNumber, int LineNumber, string? Before, string After);

/// <summary>
/// O texto das traduções que cada versão levou, em <c>releases/texts/&lt;versão&gt;.json</c>: o retrato de <see cref="ReleaseRecord.Snapshot"/> só guarda hashes,
/// e a próxima versão precisa do texto de antes para mostrar "o que mudou". Fica fora de <c>releases/*.json</c> para a leitura do histórico não carregar
/// o texto de todas as versões. É texto do tradutor, nunca o original do jogo.
/// </summary>
public static class ReleaseTexts
{
    private const string FolderName = "texts";

    private static readonly JsonSerializerOptions Json = new() { NewLine = "\n" };

    private static string PathOf(ProjectFolder project, string version) =>
        Path.Combine(project.Root, ReleaseService.FolderName, FolderName, version + ".json");

    /// <summary>Grava as falas que o build escreve no jogo (<see cref="TextUnit.NeedsRewrite"/>), por cena e id. Falha de disco não derruba o lançamento: só faltará o "antes" da próxima versão.</summary>
    public static void Save(ProjectFolder project, string version, IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(texts);
        var scenes = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (key, units) in texts)
        {
            var lines = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var unit in units.Where(u => u.NeedsRewrite))
            {
                lines[unit.Id] = Line(unit);
            }

            if (lines.Count > 0)
            {
                scenes[key] = lines;
            }
        }

        try
        {
            var path = PathOf(project, version);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(scenes, Json) + "\n", new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Complemento da próxima versão; a versão que acabou de ficar pronta não depende dele.
        }
    }

    /// <summary>O texto que a <paramref name="version"/> levou; nulo quando ela é anterior a este arquivo ou ele não lê.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Load(ProjectFolder project, string version)
    {
        ArgumentNullException.ThrowIfNull(project);
        var path = PathOf(project, version);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllBytes(path), Json);
            return read?.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, string>)p.Value, StringComparer.Ordinal);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Uma fala por cena mudada, nas primeiras <paramref name="max"/> cenas, com o texto de <paramref name="before"/> e o de agora. Sem o texto da versão anterior
    /// (<paramref name="before"/> nulo) não há o que mostrar e a lista sai vazia.
    /// </summary>
    public static IReadOnlyList<ChangeExample> Examples(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? before,
        IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts,
        ChangesSinceTest changes,
        int max)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(changes);
        var examples = new List<ChangeExample>();
        if (before is null)
        {
            return examples;
        }

        foreach (var scene in changes.Scenes)
        {
            if (examples.Count >= max)
            {
                break;
            }

            if (!texts.TryGetValue(scene.Key, out var units))
            {
                continue;
            }

            var saved = before.TryGetValue(scene.Key, out var lines) ? lines : null;
            var wanted = scene.UnitIds.ToHashSet(StringComparer.Ordinal);
            for (var i = 0; i < units.Count; i++)
            {
                if (!wanted.Contains(units[i].Id) || !units[i].NeedsRewrite)
                {
                    continue;
                }

                string? previous = null;
                saved?.TryGetValue(units[i].Id, out previous);
                examples.Add(new ChangeExample(scene.Number, i + 1, previous, Line(units[i])));
                break;
            }
        }

        return examples;
    }

    private static string Line(TextUnit unit) =>
        unit.EffectiveSpeaker is { Length: > 0 } speaker ? $"{speaker}: {unit.Translation}" : unit.Translation!;
}
