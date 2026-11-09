namespace VNForge.Patching;

/// <summary>Os textos de licença das bibliotecas que o instalador carrega, embutidos no assembly (a Apache-2.0 do VCDiff exige entregar a licença junto).</summary>
public static class ThirdPartyLicenses
{
    public const string FileName = "LICENCAS.txt";

    public static string Text { get; } = Load();

    private static string Load()
    {
        using var stream = typeof(ThirdPartyLicenses).Assembly.GetManifestResourceStream("VNForge.Patching.Licenses.txt")
            ?? throw new InvalidOperationException("O texto das licenças não está embutido no VNForge.Patching.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
