using System.Security.Cryptography;
using System.Text;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>
/// O retrato das traduções no momento de um teste no jogo: por script, o hash de cada linha que o build grava (<see cref="TextUnit.NeedsRewrite"/>).
/// Guarda só hashes, nunca o texto. Linha ausente do retrato é linha sem tradução no jogo.
/// </summary>
/// <param name="Scripts">Chave do script, id da linha e hash curto do que o jogo recebeu nela (tradução e falante escolhido).</param>
public sealed record TranslationSnapshot(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Scripts);

/// <summary>Uma cena cuja tradução mudou desde o último teste no jogo.</summary>
/// <param name="Key">Chave do script (<c>yst00012</c>).</param>
/// <param name="Number">A posição da cena entre todas as do projeto, em ordem de chave, a partir de 1; é o "Cena N" do Editor para as cenas sem nome.</param>
/// <param name="Lines">Quantas falas mudaram: traduzidas, editadas ou apagadas depois do teste.</param>
/// <param name="UnitIds">Os ids das linhas que mudaram e ainda existem na cena, na ordem do arquivo.</param>
public sealed record ChangedScene(string Key, int Number, int Lines, IReadOnlyList<string> UnitIds);

/// <summary>Por que a lista de cenas mudadas está vazia, ou que ela tem cenas.</summary>
public enum ChangesSinceTestState
{
    /// <summary>Ainda não houve teste no jogo neste projeto.</summary>
    NoTest,

    /// <summary>O último teste não guardou o retrato: foi feito por uma versão anterior ou foi o teste só dos acentos.</summary>
    NoSnapshot,

    /// <summary>O último teste não guardou o retrato porque os textos do projeto não leram na hora.</summary>
    SnapshotFailed,

    /// <summary>Nada mudou desde o último teste.</summary>
    Unchanged,

    /// <summary>Há cenas com tradução diferente da que foi para o jogo.</summary>
    Changed,
}

/// <summary>As cenas que mudaram desde o último teste no jogo, na ordem da lista de cenas do Editor (ordem da chave).</summary>
/// <param name="TestAt">Quando foi o último teste; nulo enquanto não houve nenhum.</param>
/// <param name="AccentOnly">O último teste foi só o dos acentos, que não leva a tradução do projeto para o jogo.</param>
public sealed record ChangesSinceTest(ChangesSinceTestState State, DateTimeOffset? TestAt, bool AccentOnly, IReadOnlyList<ChangedScene> Scenes)
{
    public static ChangesSinceTest None { get; } = new(ChangesSinceTestState.NoTest, null, false, []);

    public int TotalLines => Scenes.Sum(s => s.Lines);
}

/// <summary>Monta o retrato das traduções de um teste e o compara com os textos de hoje.</summary>
public static class TranslationChanges
{
    private const int HashHexLength = 12;

    /// <summary>
    /// O retrato dos textos que o teste mandou para o jogo. Com <paramref name="onlyScript"/> só essa cena entra, porque as outras ficaram em japonês no jogo;
    /// a comparação posterior as lista como mudadas, que é o que elas são em relação ao jogo.
    /// </summary>
    public static TranslationSnapshot Snapshot(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts, string? onlyScript = null)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var scripts = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (key, units) in texts.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            if (onlyScript is not null && !string.Equals(key, onlyScript, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = HashLines(units);
            if (lines.Count > 0)
            {
                scripts[key] = lines;
            }
        }

        return new TranslationSnapshot(scripts);
    }

    /// <summary>
    /// Compara os <paramref name="texts"/> de hoje com o retrato do <paramref name="lastTest"/>. Uma linha mudou quando o hash dela é outro, quando
    /// ganhou tradução depois do teste, quando a perdeu ou quando saiu do projeto.
    /// </summary>
    public static ChangesSinceTest Since(GameLastTest? lastTest, IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (lastTest is null)
        {
            return ChangesSinceTest.None;
        }

        var accentOnly = lastTest.Kind == GameTestKind.AccentTest;
        if (lastTest.SnapshotFailed && !accentOnly)
        {
            return new ChangesSinceTest(ChangesSinceTestState.SnapshotFailed, lastTest.At, false, []);
        }

        if (lastTest.Snapshot is not { } snapshot || accentOnly)
        {
            return new ChangesSinceTest(ChangesSinceTestState.NoSnapshot, lastTest.At, accentOnly, []);
        }

        var scenes = new List<ChangedScene>();
        var number = 0;
        foreach (var (key, units) in texts.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            number++;
            var now = HashLines(units);
            var before = snapshot.Scripts.TryGetValue(key, out var saved) ? saved : null;
            var ids = units.Select(u => u.Id).Where(id => Differs(now, before, id)).ToList();
            var removed = before is null ? 0 : CountRemoved(before, units);
            if (ids.Count + removed > 0)
            {
                scenes.Add(new ChangedScene(key, number, ids.Count + removed, ids));
            }
        }

        return new ChangesSinceTest(scenes.Count == 0 ? ChangesSinceTestState.Unchanged : ChangesSinceTestState.Changed, lastTest.At, false, scenes);
    }

    private static bool Differs(Dictionary<string, string> now, IReadOnlyDictionary<string, string>? before, string id)
    {
        var hasNow = now.TryGetValue(id, out var current);
        string? previous = null;
        var hasBefore = before?.TryGetValue(id, out previous) ?? false;
        return hasNow != hasBefore || (hasNow && !string.Equals(current, previous, StringComparison.Ordinal));
    }

    private static int CountRemoved(IReadOnlyDictionary<string, string> before, IReadOnlyList<TextUnit> units)
    {
        var existing = units.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        return before.Keys.Count(id => !existing.Contains(id));
    }

    private static Dictionary<string, string> HashLines(IReadOnlyList<TextUnit> units)
    {
        var lines = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var unit in units.Where(u => u.NeedsRewrite))
        {
            lines[unit.Id] = HashOf(unit);
        }

        return lines;
    }

    private static string HashOf(TextUnit unit)
    {
        // Falante escolhido nulo (usa o original) e vazio (some o nome) dão arquivos de jogo diferentes, então o hash os separa.
        var bytes = Encoding.UTF8.GetBytes($"{unit.Translation}\u0000{(unit.ChosenSpeaker is null ? "-" : "+" + unit.ChosenSpeaker)}");
        return Convert.ToHexString(SHA256.HashData(bytes))[..HashHexLength].ToLowerInvariant();
    }
}
