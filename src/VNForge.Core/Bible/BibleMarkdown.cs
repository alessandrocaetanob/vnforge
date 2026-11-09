using System.Text;

namespace VNForge.Core.Bible;

/// <summary>A bíblia como texto para ler fora do VNForge (<c>vnf bible export</c> e o botão "Exportar"): Markdown em português, LF.</summary>
public static class BibleMarkdown
{
    public static string Render(GameBible bible, string title)
    {
        ArgumentNullException.ThrowIfNull(bible);
        var sb = new StringBuilder("# Bíblia de ").Append(string.IsNullOrWhiteSpace(title) ? "o jogo" : title.Trim()).Append('\n');

        if (bible.Synopsis.Length > 0)
        {
            sb.Append("\n## Sinopse\n\n").Append(bible.Synopsis).Append('\n');
        }

        if (bible.Timeline.Count > 0)
        {
            sb.Append("\n## Linha do tempo\n\n");
            foreach (var entry in bible.Timeline)
            {
                sb.Append("- ").Append(entry.Title).Append(entry.Note.Length > 0 ? $": {entry.Note}" : string.Empty).Append('\n');
            }
        }

        if (bible.Scenes.Count > 0)
        {
            sb.Append("\n## Cenas\n");
            foreach (var scene in bible.Scenes)
            {
                AppendScene(sb, scene);
            }
        }

        return sb.ToString();
    }

    private static void AppendScene(StringBuilder sb, BibleScene scene)
    {
        sb.Append("\n### ").Append(scene.Key).Append(scene.Name.Length > 0 ? $" · {scene.Name}" : string.Empty).Append("\n\n");
        sb.Append(scene.Summary.Length > 0 ? scene.Summary : "Sem resumo ainda.").Append('\n');
        AppendList(sb, "Quem aparece", scene.Characters);
        AppendList(sb, "Tom", scene.Tone);
        AppendList(sb, "Piadas que voltam", scene.Jokes);
        AppendList(sb, "Relações", scene.Relations.Select(r => $"{r.Between}: {r.Note}"));
    }

    private static void AppendList(StringBuilder sb, string label, IEnumerable<string> items)
    {
        var list = items.ToList();
        if (list.Count > 0)
        {
            sb.Append("\n- ").Append(label).Append(": ").Append(string.Join("; ", list)).Append('\n');
        }
    }
}
