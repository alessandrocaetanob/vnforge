using VNForge.Core.Formatting;

namespace VNForge.App.Services;

/// <summary>
/// As frases em português de um ajuste de formatação que a estratégia de acentuação não grava (<see cref="FormattingConflict"/>), as mesmas no card
/// da Conferência e na dica e na faixa de Ajustes.
/// </summary>
public static class SettingConflictTexts
{
    /// <summary>"A estratégia cp1252 não grava as reticências (…)."; o caractere vem nomeado, porque na fonte da interface "…" e "..." se confundem.</summary>
    public static string Refusal(string strategyId, FormattingConflict conflict) => $"A estratégia {strategyId} não grava {Named(conflict)}.";

    /// <summary>"Usar três pontos", "Usar aspas retas", "Desligar o travessão": o rótulo do botão que troca o ajuste pelo valor seguro.</summary>
    public static string ButtonLabel(FormattingConflict conflict) => conflict.Field switch
    {
        FormattingConflicts.WidthField => "Manter a largura como está",
        FormattingConflicts.DashField or FormattingConflicts.DialogueDashField when conflict.Safe is null => $"Desligar o {LowerFirst(conflict.Setting)}",
        _ => $"Usar {LowerFirst(conflict.SafeLabel)}",
    };

    /// <summary>"O ajuste Reticências passou de Caractere … para Três pontos."</summary>
    public static string Changed(FormattingConflict conflict) =>
        conflict.Safe is null ? $"O ajuste {conflict.Setting} foi desligado." : $"O ajuste {conflict.Setting} passou de {conflict.CurrentLabel} para {conflict.SafeLabel}.";

    private static string Named(FormattingConflict conflict)
    {
        var runes = conflict.Characters.EnumerateRunes().Select(r => r.ToString()).Distinct(StringComparer.Ordinal).ToList();
        return conflict.Field switch
        {
            FormattingConflicts.EllipsisField => "as reticências (…)",
            FormattingConflicts.QuotesField => "as aspas 「 」",
            FormattingConflicts.DashField => $"o travessão ({Codes(conflict.Characters)})",
            FormattingConflicts.DialogueDashField => $"o travessão de fala ({Codes(conflict.Characters)})",
            FormattingConflicts.WidthField => "os caracteres de largura cheia",
            _ when runes.Count == 1 => $"o caractere {Codes(conflict.Characters)}",
            _ => $"os caracteres {string.Join(", ", runes.Select(Quote))}",
        };
    }

    private static string Codes(string characters) =>
        string.Join(", ", characters.EnumerateRunes().Select(r => $"U+{r.Value:X4}"));

    private static string Quote(string text) => $"'{text}'";

    private static string LowerFirst(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];
}
