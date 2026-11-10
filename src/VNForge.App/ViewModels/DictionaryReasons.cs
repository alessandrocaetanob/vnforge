using VNForge.Core.Dictionaries;

namespace VNForge.App.ViewModels;

/// <summary>As frases da aba Dicionários que dizem por que uma condição deixou a regra de fora (#539): o motivo da amostra e o resumo do que fica sem troca.</summary>
public static class DictionaryReasons
{
    /// <summary>"falante diferente", "outra cena", "sem o trecho": o motivo de cada condição que falhou, na ordem da regra.</summary>
    public static string Why(IReadOnlyList<DictionaryCondition> conditions, IReadOnlyList<int>? failed)
    {
        var reasons = (failed ?? []).Where(i => i < conditions.Count).Select(i => conditions[i].Kind switch
        {
            DictionaryConditionKind.Speaker => "falante diferente",
            DictionaryConditionKind.Scene => "outra cena",
            _ => "sem o trecho",
        }).ToList();
        return reasons.Count == 0 ? "a condição não bate" : string.Join(", ", reasons);
    }

    /// <summary>
    /// "2 linhas do Ren fora do Capítulo 1 não mudam": o grupo maior de linhas que falham numa condição só, com o falante da regra quando ele bate.
    /// Sem linha que falhe numa condição só, o resumo geral.
    /// </summary>
    public static string LeftOut(IReadOnlyList<DictionaryCondition> conditions, DictionaryImpact impact)
    {
        var group = (impact.BlockedByOne ?? new Dictionary<int, int>())
            .Where(g => g.Key < conditions.Count)
            .OrderByDescending(g => g.Value)
            .ThenBy(g => g.Key)
            .FirstOrDefault();
        if (group.Value == 0)
        {
            return $"{DictionaryRuleRow.LinesLabel(impact.Blocked)} com este texto não mudam por causa das condições";
        }

        var failing = conditions[group.Key];
        var parts = new List<string>();
        if (failing.Kind == DictionaryConditionKind.Speaker)
        {
            parts.Add("de outro falante");
        }
        else
        {
            if (conditions.FirstOrDefault(c => c.Kind == DictionaryConditionKind.Speaker) is { } speaker)
            {
                parts.Add($"do {speaker.Value.Trim()}");
            }

            parts.Add(failing.Kind == DictionaryConditionKind.Scene ? $"fora do {failing.Value.Trim()}" : $"sem {failing.Value.Trim()}");
        }

        return $"{DictionaryRuleRow.LinesLabel(group.Value)} {string.Join(' ', parts)} {(group.Value == 1 ? "não muda" : "não mudam")}";
    }
}
