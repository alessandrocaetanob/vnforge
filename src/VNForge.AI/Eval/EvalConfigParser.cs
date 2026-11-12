using VNForge.AI.Claude;
using VNForge.Core;
using VNForge.Core.Eval;
using VNForge.Core.Projects;

namespace VNForge.AI.Eval;

/// <summary>Erro esperado do banco de provas (configuração inválida, projeto sem linhas aprovadas); a mensagem vai direto à pessoa.</summary>
public sealed class EvalException(string message, Exception? inner = null) : VNForgeException(message, inner);

/// <summary>
/// Lê uma configuração escrita como <c>modelo[:effort[:contexto[:bíblia]]]</c> (<c>sonnet:medium:15:com-biblia</c>). O modelo aceita o apelido (<c>opus</c>, <c>sonnet</c>,
/// <c>haiku</c>, <c>fable</c>) ou o id completo; o esforço, <c>low</c>, <c>medium</c> ou <c>high</c> (padrão <c>medium</c>); o contexto, o número de falas de cada lado
/// (padrão 15); a bíblia, <c>com-biblia</c> ou <c>sem-biblia</c> (padrão sem).
/// </summary>
public static class EvalConfigParser
{
    public const int DefaultContextLines = 15;

    public const int MaxContextLines = AiConfig.MaxContextLines;

    public const int MaxConfigs = 26;

    public static EvalConfig Parse(string spec, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var parts = (spec ?? string.Empty).Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is 0 or > 4 || parts[0].Length == 0)
        {
            throw new EvalException($"Configuração inválida: '{spec}'. Use modelo[:effort[:contexto[:com-biblia|sem-biblia]]], por exemplo sonnet:medium:15:com-biblia.");
        }

        var model = ModelFor(parts[0]);
        var effort = Part(parts, 1) is { } effortText ? EffortOf(effortText, spec) : "medium";
        var context = Part(parts, 2) is { } contextText ? ContextOf(contextText, spec) : DefaultContextLines;
        var bible = Part(parts, 3) is { } bibleText && WithBible(bibleText, spec);
        return new EvalConfig(id, model, effort, context, bible);
    }

    private static string? Part(string[] parts, int index) => parts.Length > index && parts[index].Length > 0 ? parts[index] : null;

    private static string EffortOf(string text, string? spec) =>
        ClaudeEffortText.TryParse(text, out _)
            ? text.ToLowerInvariant()
            : throw new EvalException($"Esforço desconhecido em '{spec}': '{text}'. Use low, medium ou high.");

    private static int ContextOf(string text, string? spec) =>
        int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n <= MaxContextLines
            ? n
            : throw new EvalException($"Contexto inválido em '{spec}': '{text}'. Use um número de 0 a {MaxContextLines}.");

    private static bool WithBible(string text, string? spec) => text.ToLowerInvariant() switch
    {
        "com-biblia" or "com-bíblia" or "biblia" or "bíblia" => true,
        "sem-biblia" or "sem-bíblia" => false,
        _ => throw new EvalException($"Bíblia inválida em '{spec}': '{text}'. Use com-biblia ou sem-biblia."),
    };

    /// <summary>Lê várias configurações e dá a cada uma a letra da posição (A, B, C...).</summary>
    public static IReadOnlyList<EvalConfig> ParseAll(IEnumerable<string> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);
        var list = specs.ToList();
        if (list.Count > MaxConfigs)
        {
            throw new EvalException($"No máximo {MaxConfigs} configurações por rodada.");
        }

        return [.. list.Select((spec, i) => Parse(spec, EvalConfig.LetterFor(i)))];
    }

    /// <summary>As configurações de partida de um projeto que ainda não montou as suas: os três modelos baratos o bastante para comparar, com o mesmo contexto.</summary>
    public static IReadOnlyList<EvalConfig> Defaults { get; } =
    [
        new("A", ClaudeModels.Opus, "medium", DefaultContextLines, false),
        new("B", ClaudeModels.Sonnet, "medium", DefaultContextLines, false),
        new("C", ClaudeModels.Haiku, "low", DefaultContextLines, false),
    ];

    private static string ModelFor(string text)
    {
        var lower = text.ToLowerInvariant();
        if (ClaudeModels.All.Contains(lower))
        {
            return lower;
        }

        return lower switch
        {
            "opus" => ClaudeModels.Opus,
            "sonnet" => ClaudeModels.Sonnet,
            "haiku" => ClaudeModels.Haiku,
            "fable" => ClaudeModels.Fable,
            _ => throw new EvalException($"Modelo desconhecido: '{text}'. Use opus, sonnet, haiku, fable ou um de: {string.Join(", ", ClaudeModels.All)}."),
        };
    }
}
