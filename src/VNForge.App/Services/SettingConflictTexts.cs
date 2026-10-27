using VNForge.Core.Formatting;

namespace VNForge.App.Services;

/// <summary>
/// As frases em português de um ajuste de formatação que a estratégia de acentuação não grava (<see cref="FormattingConflict"/>), as mesmas no card
/// da Conferência e na dica e na faixa de Ajustes.
/// </summary>
public static class SettingConflictTexts
{
    private const int MaxListed = 4;

    /// <summary>"A estratégia cp1252 não grava o '…'."; com vários caracteres, "não grava os caracteres '「' e '」'".</summary>
    public static string Refusal(string strategyId, string characters) => $"A estratégia {strategyId} não grava {Characters(characters)}.";

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

    private static string Characters(string characters)
    {
        var runes = characters.EnumerateRunes().Select(r => r.ToString()).Distinct(StringComparer.Ordinal).ToList();
        return runes.Count switch
        {
            0 => "o caractere do ajuste",
            1 => $"o '{runes[0]}'",
            <= MaxListed => $"os caracteres {string.Join(", ", runes.Take(runes.Count - 1).Select(Quote))} e {Quote(runes[^1])}",
            _ => "os caracteres de largura cheia",
        };
    }

    private static string Quote(string text) => $"'{text}'";

    private static string LowerFirst(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];
}
