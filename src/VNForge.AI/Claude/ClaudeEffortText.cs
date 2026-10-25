namespace VNForge.AI.Claude;

/// <summary>O esforço como aparece em <c>vnforge.json</c> e em <c>--effort</c>: <c>low</c>, <c>medium</c> ou <c>high</c>.</summary>
public static class ClaudeEffortText
{
    public static bool TryParse(string? text, out ClaudeEffort effort)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "low":
                effort = ClaudeEffort.Low;
                return true;
            case "medium":
                effort = ClaudeEffort.Medium;
                return true;
            case "high":
                effort = ClaudeEffort.High;
                return true;
            default:
                effort = ClaudeEffort.Medium;
                return false;
        }
    }

    /// <summary>O esforço do texto; qualquer outra coisa vale <see cref="ClaudeEffort.Medium"/>.</summary>
    public static ClaudeEffort ParseOrDefault(string? text) => TryParse(text, out var effort) ? effort : ClaudeEffort.Medium;
}
