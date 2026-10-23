using VNForge.Core.Text;

namespace VNForge.AI.Translation;

/// <summary>Por que uma linha da resposta não serve.</summary>
public enum TranslationProblemKind
{
    /// <summary>O id do lote não veio na resposta.</summary>
    MissingId,

    /// <summary>O id veio mais de uma vez.</summary>
    DuplicateId,

    /// <summary>A tradução veio vazia sem ser uma recusa.</summary>
    EmptyTranslation,

    /// <summary>Os marcadores de controle da tradução diferem dos do original.</summary>
    MarkersChanged,
}

/// <summary>Uma linha do lote cuja resposta precisa ser pedida de novo.</summary>
public sealed record TranslationProblem(string Id, TranslationProblemKind Kind, string Detail);

/// <summary>Resultado da validação: as linhas aceitas (na ordem do lote) e as que precisam de reenvio.</summary>
public sealed record TranslationValidationReport(IReadOnlyList<TranslationResult> Valid, IReadOnlyList<TranslationProblem> Problems);

/// <summary>
/// Confere a resposta do provedor contra o lote: mesmos ids e mesmos marcadores de controle (<c>\n</c>, <c>\p</c>,
/// <c>{VAR:x}</c>, <c>{EF:xx}</c>...). A ordem dos marcadores pode mudar, porque a sintaxe do português reordena frases;
/// a contagem de cada um, não. Ids que não estão no lote são ignorados.
/// </summary>
public static class TranslationResponseValidator
{
    public static TranslationValidationReport Validate(TranslationBatch batch, IReadOnlyList<TranslationResult> response)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(response);

        var byId = response.GroupBy(r => r.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var valid = new List<TranslationResult>();
        var problems = new List<TranslationProblem>();

        foreach (var line in batch.Lines)
        {
            if (!byId.TryGetValue(line.Id, out var found))
            {
                problems.Add(new(line.Id, TranslationProblemKind.MissingId, "o id não veio na resposta"));
                continue;
            }

            if (found.Count > 1)
            {
                problems.Add(new(line.Id, TranslationProblemKind.DuplicateId, $"o id veio {found.Count} vezes"));
                continue;
            }

            var result = found[0];
            if (result.Refused)
            {
                valid.Add(result);
                continue;
            }

            if (string.IsNullOrWhiteSpace(result.Translation))
            {
                problems.Add(new(line.Id, TranslationProblemKind.EmptyTranslation, "a tradução veio vazia"));
                continue;
            }

            var diff = DescribeMarkerDifference(line.Source, result.Translation);
            if (diff is not null)
            {
                problems.Add(new(line.Id, TranslationProblemKind.MarkersChanged, diff));
                continue;
            }

            valid.Add(result);
        }

        return new(valid, problems);
    }

    private static string? DescribeMarkerDifference(string source, string translation)
    {
        var expected = Count(Markers(source));
        var actual = Count(Markers(translation));
        var parts = new List<string>();
        foreach (var key in expected.Keys.Union(actual.Keys).Order(StringComparer.Ordinal))
        {
            expected.TryGetValue(key, out var e);
            actual.TryGetValue(key, out var a);
            if (e != a)
                parts.Add($"{key}: esperado {e}, veio {a}");
        }

        return parts.Count == 0 ? null : "marcadores diferentes do original (" + string.Join("; ", parts) + ")";
    }

    private static Dictionary<string, int> Count(IEnumerable<string> markers)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var m in markers)
            counts[m] = counts.GetValueOrDefault(m) + 1;
        return counts;
    }

    // Mesma gramática de ControlMarkers (Engines.YuRis, que o AI não referencia): "\x" é um marcador de um caractere
    // (inclui "\\" e "\{", barra e chave literais) e "{...}" é um marcador genérico. Chave sem fechamento e barra no fim contam como marcadores, porque ToBytes os recusa.
    private static IEnumerable<string> Markers(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\')
            {
                var length = i + 1 < text.Length ? 2 : 1;
                yield return text.Substring(i, length);
                i += length;
            }
            else if (c == '{')
            {
                var close = text.IndexOf('}', i + 1);
                if (close < 0)
                {
                    yield return "{";
                    i++;
                    continue;
                }

                yield return text.Substring(i, close - i + 1);
                i = close + 1;
            }
            else
            {
                i++;
            }
        }
    }
}
