namespace VNForge.AI.Claude;

/// <summary>Ids dos modelos Claude aceitos pelo VNForge, exatamente como a API os espera.</summary>
public static class ClaudeModels
{
    public const string Opus = "claude-opus-5-5";
    public const string Sonnet = "claude-sonnet-5-5";
    public const string Haiku = "claude-haiku-5-5";
    public const string Fable = "claude-fable-5-1";

    /// <summary>Modelos selecionáveis; o primeiro é o padrão.</summary>
    public static IReadOnlyList<string> All { get; } = [Opus, Sonnet, Haiku, Fable];
}

/// <summary>Nível de <c>output_config.effort</c>. Controla a profundidade do raciocínio e o gasto de tokens.</summary>
public enum ClaudeEffort
{
    Low,
    Medium,
    High,
    XHigh,
    Max,
}

/// <summary>Configuração do provedor Claude.</summary>
public sealed record ClaudeOptions
{
    /// <summary>Id do modelo; recusa ids fora de <see cref="ClaudeModels.All"/>.</summary>
    public string Model
    {
        get;
        init => field = ClaudeModels.All.Contains(value)
            ? value
            : throw new ArgumentException($"Modelo desconhecido: '{value}'. Use um de: {string.Join(", ", ClaudeModels.All)}.", nameof(value));
    } = ClaudeModels.Opus;

    /// <summary>Para tradução em lote, <see cref="ClaudeEffort.Low"/> ou <see cref="ClaudeEffort.Medium"/>.</summary>
    public ClaudeEffort Effort { get; init; } = ClaudeEffort.Medium;

    /// <summary>Teto de tokens da resposta, incluindo o raciocínio.</summary>
    public int MaxTokens { get; init; } = 16000;
}
