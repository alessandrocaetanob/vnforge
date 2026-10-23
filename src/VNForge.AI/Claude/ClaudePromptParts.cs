using System.Text;
using System.Text.Json;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Claude;

/// <summary>Trechos de prompt e de resposta que o provedor de tradução e o de revisão dividem.</summary>
internal static class ClaudePromptParts
{
    /// <summary>Acrescenta ao prompt de sistema o guia de estilo, o glossário e os personagens do lote.</summary>
    public static void AppendContext(StringBuilder sb, TranslationBatch batch, string glossaryHeading)
    {
        if (!string.IsNullOrWhiteSpace(batch.StyleGuide))
            sb.AppendLine().AppendLine("Guia de estilo:").AppendLine(batch.StyleGuide.Trim());

        if (batch.Glossary.Count > 0)
        {
            sb.AppendLine().AppendLine(glossaryHeading);
            foreach (var g in batch.Glossary)
                sb.AppendLine(string.IsNullOrWhiteSpace(g.Notes) ? $"- {g.Source} => {g.Target}" : $"- {g.Source} => {g.Target} ({g.Notes})");
        }

        if (batch.Characters.Count > 0)
        {
            sb.AppendLine().AppendLine("Personagens:");
            foreach (var c in batch.Characters)
                sb.AppendLine(CharacterLine(c));
        }
    }

    /// <summary>Acrescenta ao prompt do usuário o contexto anterior, que o modelo lê mas não processa.</summary>
    public static void AppendPrevious(StringBuilder sb, TranslationBatch batch, string heading)
    {
        if (batch.PreviousLines.Count == 0)
            return;

        sb.AppendLine(heading);
        foreach (var u in batch.PreviousLines)
            sb.AppendLine($"{(u.Speaker is null ? string.Empty : u.Speaker + ": ")}{u.Source}");
        sb.AppendLine();
    }

    /// <summary>Um lote só com <paramref name="line"/>, no mesmo contexto de <paramref name="batch"/>.</summary>
    public static TranslationBatch SingleLine(TranslationBatch batch, TextUnit line) => new([line])
    {
        PreviousLines = batch.PreviousLines,
        Glossary = batch.Glossary,
        Characters = batch.Characters,
        StyleGuide = batch.StyleGuide,
    };

    /// <summary>
    /// Lê <c>{ "&lt;array&gt;": [ { "id": ..., "&lt;value&gt;": ... } ] }</c> e devolve os valores na ordem do lote. Exige exatamente os
    /// ids do lote, sem repetição.
    /// </summary>
    public static IReadOnlyList<string> ParseValues(string text, string arrayName, string valueName, TranslationBatch batch)
    {
        Dictionary<string, string> byId = new(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(text);
            foreach (var item in doc.RootElement.GetProperty(arrayName).EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? throw new ClaudeResponseException("Resposta com id nulo.");
                if (!byId.TryAdd(id, item.GetProperty(valueName).GetString() ?? string.Empty))
                    throw new ClaudeResponseException($"Resposta com id repetido: '{id}'.");
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ClaudeResponseException("A resposta do Claude não é o JSON esperado.", e);
        }

        var expected = batch.Lines.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var extra = byId.Keys.Where(k => !expected.Contains(k)).ToList();
        var missing = expected.Where(k => !byId.ContainsKey(k)).ToList();
        if (extra.Count > 0 || missing.Count > 0)
            throw new ClaudeResponseException($"Ids da resposta não batem com o lote. Faltam: [{string.Join(", ", missing)}]; sobram: [{string.Join(", ", extra)}].");

        return [.. batch.Lines.Select(u => byId[u.Id])];
    }

    private static string CharacterLine(CharacterInfo c)
    {
        var details = new List<string>();
        if (c.Gender is not null) details.Add($"gênero: {c.Gender}");
        if (c.Tone is not null) details.Add($"tom: {c.Tone}");
        return details.Count == 0 ? $"- {c.Source} => {c.Target}" : $"- {c.Source} => {c.Target} ({string.Join("; ", details)})";
    }
}
