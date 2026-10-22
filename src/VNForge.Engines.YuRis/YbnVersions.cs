namespace VNForge.Engines.YuRis;

/// <summary>
/// Faixa de versões que os arquivos <c>.ybn</c> de apoio (YSCM, YSTL) aceitam: o <c>VerRange</c> do yuri
/// (<c>fileformat/common.py:8</c>), de 200 a 599, a mesma margem do <see cref="Ypf.YpfVersionDefaults"/>.
/// </summary>
internal static class YbnVersions
{
    public const uint Min = 200;
    public const uint Max = 599;

    public static bool IsKnown(uint version) => version is >= Min and <= Max;
}
