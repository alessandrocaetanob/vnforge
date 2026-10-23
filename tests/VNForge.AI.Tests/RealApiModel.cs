using VNForge.AI.Claude;

namespace VNForge.AI.Tests;

/// <summary>Escolhe o modelo do teste que fala com a API real: <c>VNFORGE_TEST_MODEL</c>, ou Haiku (o mais barato).</summary>
internal static class RealApiModel
{
    public const string VariableName = "VNFORGE_TEST_MODEL";

    public static string FromEnvironment() => Resolve(Environment.GetEnvironmentVariable(VariableName));

    public static string Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ClaudeModels.Haiku;

        var model = value.Trim();
        return ClaudeModels.All.Contains(model)
            ? model
            : throw new InvalidOperationException(
                $"{VariableName}='{model}' não é um modelo conhecido. Use um de: {string.Join(", ", ClaudeModels.All)}.");
    }
}
