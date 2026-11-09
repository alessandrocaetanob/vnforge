using VNForge.Core.Projects;

namespace VNForge.AI.Bible;

/// <summary>
/// Divide o roteiro do projeto nos capítulos que a IA lê, um por vez: os mesmos grupos do Painel e de <c>vnf scripts</c> (<see cref="ScriptGrouping"/>),
/// sem o grupo "Menus e nomes". Um capítulo que não cabe no contexto do modelo é dividido na fronteira das cenas.
/// </summary>
public static class BibleChapterPlanner
{
    /// <summary>
    /// O contexto de 1M tokens é padrão no Opus, no Sonnet e no Haiku 5.5. A estimativa de tokens é uma heurística (<see cref="Cost.CostEstimator"/>), então o capítulo
    /// só chega a 700 mil tokens, o que deixa folga para a instrução, a resposta e o erro da estimativa.
    /// </summary>
    public const int MaxChapterTokens = 700_000;

    /// <exception cref="VNForge.Core.Glossary.GlossaryFileException">O <c>scripts.json</c> ou o <c>characters.json</c> é inválido.</exception>
    /// <exception cref="VNForge.Core.Text.TextFileException">Algum <c>text/*.json</c> é inválido.</exception>
    public static IReadOnlyList<BibleChapterInput> Plan(ProjectFolder project, int maxChapterTokens = MaxChapterTokens)
    {
        ArgumentNullException.ThrowIfNull(project);
        var texts = project.LoadAllText();
        var names = project.LoadScriptNames();
        var groups = ScriptGrouping.Build(names, ProjectStatusReporter.Create(texts).Files);
        var characters = project.LoadCharacters()
            .Where(c => !string.IsNullOrWhiteSpace(c.Source) && !string.IsNullOrWhiteSpace(c.Target))
            .Select(c => new BibleCharacterName(c.Source.Trim(), c.Target.Trim()))
            .ToList();

        List<BibleChapterInput> chapters = [];
        foreach (var group in groups.Where(g => !g.IsExtra))
        {
            // O nome que a bíblia deu na última geração não volta como pista: a IA nomearia a cena pelo próprio nome antigo.
            var scenes = group.ScriptKeys
                .Where(texts.ContainsKey)
                .Select(key => new BibleSceneInput(key, names.CustomName(key) ?? names.DetectedName(key) ?? string.Empty, [.. texts[key].Where(u => !string.IsNullOrWhiteSpace(u.Source))]))
                .Where(s => s.Lines.Count > 0)
                .ToList();
            if (scenes.Count > 0)
            {
                chapters.AddRange(Split(new BibleChapterInput(group.Name, group.Number, scenes, characters), maxChapterTokens));
            }
        }

        return chapters;
    }

    /// <summary>
    /// Divide o capítulo em partes de até <paramref name="maxTokens"/>, sem cortar uma cena ao meio ("Manhã (parte 1)", "Manhã (parte 2)"). Um capítulo que cabe volta como veio.
    /// Uma cena maior que o limite fica sozinha na parte dela.
    /// </summary>
    public static IReadOnlyList<BibleChapterInput> Split(BibleChapterInput chapter, int maxTokens)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        if (chapter.Tokens() <= maxTokens)
        {
            return [chapter];
        }

        List<List<BibleSceneInput>> parts = [[]];
        long used = 0;
        foreach (var scene in chapter.Scenes)
        {
            var tokens = (chapter with { Scenes = [scene] }).Tokens();
            if (parts[^1].Count > 0 && used + tokens > maxTokens)
            {
                parts.Add([]);
                used = 0;
            }

            parts[^1].Add(scene);
            used += tokens;
        }

        return [.. parts.Select((scenes, i) => chapter with { Name = $"{chapter.Name} (parte {i + 1})", Scenes = scenes })];
    }
}
