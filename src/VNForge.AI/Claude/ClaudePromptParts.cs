using System.Text;
using System.Text.Json;
using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Claude;

/// <summary>Trechos de prompt e de resposta que o provedor de tradução e o de revisão dividem.</summary>
internal static class ClaudePromptParts
{
    /// <summary>
    /// Acrescenta ao bloco estável do prompt de sistema as escolhas de estilo e o guia de estilo, que não mudam entre lotes do mesmo
    /// projeto: é o bloco que leva o <c>cache_control</c> (ver <see cref="AnthropicMessageClient"/>).
    /// </summary>
    public static void AppendStableContext(StringBuilder sb, TranslationBatch batch)
    {
        AppendStyleChoices(sb, batch.Style);

        if (!string.IsNullOrWhiteSpace(batch.StyleGuide))
            sb.AppendLine().AppendLine("Guia de estilo:").AppendLine(batch.StyleGuide.Trim());
    }

    /// <summary>O glossário e os personagens do lote, que mudam a cada lote: vão no bloco do sistema sem <c>cache_control</c>.</summary>
    public static string BatchContext(TranslationBatch batch, string glossaryHeading)
    {
        var sb = new StringBuilder();
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

        return sb.ToString().TrimStart();
    }

    /// <summary>Acrescenta ao prompt do usuário o contexto anterior, que o modelo lê mas não processa.</summary>
    public static void AppendPrevious(StringBuilder sb, TranslationBatch batch, string heading) =>
        AppendContextLines(sb, batch.PreviousLines, heading);

    /// <summary>Acrescenta ao prompt do usuário as falas seguintes ao lote, só para o modelo entender o que vem depois.</summary>
    public static void AppendNext(StringBuilder sb, TranslationBatch batch, string heading) =>
        AppendContextLines(sb, batch.NextLines, heading);

    /// <summary>As escolhas de estilo em frases para o modelo, uma por linha, sempre na mesma ordem e com o mesmo texto.</summary>
    public static IReadOnlyList<string> StyleLines(StyleConfig style)
    {
        ArgumentNullException.ThrowIfNull(style);
        List<string> lines =
        [
            style.Honorifics switch
            {
                HonorificsPolicy.Keep => "Honoríficos: mantenha -san, -kun, -chan, -sama e senpai como no original (Misaki-chan, senpai).",
                HonorificsPolicy.Remove => "Honoríficos: remova -san, -kun, -chan, -sama e senpai; use só o nome ou um termo comum (Misaki, veterano).",
                _ => "Honoríficos: adapte. -chan e -kun saem; -san vira \"senhor\" ou \"senhora\" só em fala formal; senpai segue o glossário.",
            },
            style.Address == AddressForm.Tu
                ? "Tratamento: os personagens se tratam por \"tu\", com a conjugação de segunda pessoa (tu vais, te molhas)."
                : "Tratamento: os personagens se tratam por \"você\" (você vai se molhar).",
            NarratorLine(style.Narrator),
            style.TranslateOnomatopoeia
                ? "Onomatopeias: traduza para o equivalente em português (ドキドキ vira \"tum-tum\")."
                : "Onomatopeias: mantenha como soam em japonês, em letras latinas (doki doki).",
            style.WesternNameOrder
                ? "Nomes: escreva na ordem ocidental, nome antes do sobrenome (Misaki Sakura)."
                : "Nomes: mantenha a ordem japonesa, sobrenome antes do nome (Sakura Misaki).",
        ];

        if (style.KeyboardPunctuation)
        {
            lines.Add("Pontuação: use a do teclado ocidental (. , ! ? ~ ( ) \"). Não copie a pontuação japonesa do original (。 、 ～ （ ） 「 」 ！ ？).");
        }

        return lines;
    }

    private static void AppendStyleChoices(StringBuilder sb, StyleConfig style)
    {
        sb.AppendLine().AppendLine("Estilo da tradução:");
        foreach (var line in StyleLines(style))
            sb.Append("- ").AppendLine(line);
    }

    private static string NarratorLine(NarratorStyle narrator)
    {
        var who = narrator.WhoOrNull is { } name ? $"{name}, em" : "em";
        var person = narrator.Person == NarratorPerson.Third ? "terceira pessoa" : "primeira pessoa";
        var gender = narrator.Gender switch
        {
            NarratorGender.Male => "gênero masculino (concorde adjetivos e particípios no masculino)",
            NarratorGender.Female => "gênero feminino (concorde adjetivos e particípios no feminino)",
            _ => "gênero neutro (prefira construções sem marca de gênero)",
        };
        return $"Narrador: {who} {person}; {gender}.";
    }

    private static void AppendContextLines(StringBuilder sb, IReadOnlyList<TextUnit> lines, string heading)
    {
        if (lines.Count == 0)
            return;

        sb.AppendLine(heading);
        foreach (var u in lines)
            sb.AppendLine($"{(u.Speaker is null ? string.Empty : u.Speaker + ": ")}{u.Source}");
        sb.AppendLine();
    }

    /// <summary>Um lote só com <paramref name="line"/>, no mesmo contexto de <paramref name="batch"/>.</summary>
    public static TranslationBatch SingleLine(TranslationBatch batch, TextUnit line) => batch.ForLines([line]);

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

    /// <summary>
    /// As dúvidas que o modelo devolveu em <c>doubt</c>, pelo id da linha. A dúvida é um extra: a que vier com tipo desconhecido, sem frase ou fora do formato
    /// é descartada sem derrubar o lote, porque a tradução da linha continua valendo. Chama-se depois de <see cref="ParseValues"/>, que já recusou o JSON inválido.
    /// </summary>
    public static IReadOnlyDictionary<string, TranslationDoubt> ParseDoubts(string text, string arrayName)
    {
        var doubts = new Dictionary<string, TranslationDoubt>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(text);
        foreach (var item in doc.RootElement.GetProperty(arrayName).EnumerateArray())
        {
            if (!item.TryGetProperty("doubt", out var element) || item.GetProperty("id").GetString() is not { } id)
                continue;

            try
            {
                if (element.Deserialize<TranslationDoubt>(TextUnitJson.Options) is { } doubt && !string.IsNullOrWhiteSpace(doubt.Note))
                    doubts[id] = doubt with { Note = doubt.Note.Trim() };
            }
            catch (JsonException)
            {
                // Dúvida fora do formato: fica sem dúvida, a tradução da linha não depende dela.
            }
        }

        return doubts;
    }

    /// <summary>
    /// Roda <paramref name="once"/> no lote. Uma recusa não derruba o lote: ele é refeito linha a linha e só a linha recusada
    /// volta como <paramref name="refused"/>.
    /// </summary>
    public static async Task<IReadOnlyList<T>> WithRefusalFallback<T>(
        TranslationBatch batch, Func<TranslationBatch, Task<IReadOnlyList<T>>> once, Func<TextUnit, T> refused)
    {
        if (batch.Lines.Count == 0)
            return [];

        try
        {
            return await once(batch).ConfigureAwait(false);
        }
        catch (ClaudeRefusalException)
        {
            if (batch.Lines.Count == 1)
                return [refused(batch.Lines[0])];
        }

        // O lote tem um gatilho de recusa; isolar por linha mantém as demais.
        List<T> results = [];
        foreach (var line in batch.Lines)
            results.AddRange(await WithRefusalFallback(SingleLine(batch, line), once, refused).ConfigureAwait(false));
        return results;
    }

    private static string CharacterLine(CharacterInfo c)
    {
        var details = new List<string>();
        if (c.Gender is not null) details.Add($"gênero: {c.Gender}");
        if (c.Tone is not null) details.Add($"tom: {c.Tone}");
        return details.Count == 0 ? $"- {c.Source} => {c.Target}" : $"- {c.Source} => {c.Target} ({string.Join("; ", details)})";
    }
}
