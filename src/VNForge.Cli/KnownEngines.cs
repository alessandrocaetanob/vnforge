using VNForge.Core.Engines;
using VNForge.Engines.YuRis;

namespace VNForge.Cli;

/// <summary>Engines que o <c>vnf</c> sabe reconhecer, na ordem em que são tentadas.</summary>
internal static class KnownEngines
{
    public static IReadOnlyList<IEngine> All { get; } = [new YuRisEngine()];
}
