namespace VNForge.App.Tests.Rendering;

/// <summary>Onde os PNGs vão: <c>VNFORGE_RENDER_DIR</c> quando definida (para pôr nos PRs), senão uma pasta temporária.</summary>
internal static class RenderOutput
{
    public const string Variable = "VNFORGE_RENDER_DIR";

    public static string Directory { get; } =
        Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } configured
            ? configured
            : Path.Combine(Path.GetTempPath(), "vnforge-app-tests", "render");

    public static string PathFor(string fileName) => Path.Combine(Directory, fileName);
}
