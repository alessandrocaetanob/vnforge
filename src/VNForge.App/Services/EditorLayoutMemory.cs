namespace VNForge.App.Services;

/// <summary>
/// Como a pessoa deixou o Editor: se a coluna das cenas ficou recolhida (#393) e se a grade mostra as linhas como roteiro (#460). Grava <c>editor-layout.json</c> (ver <see cref="BoolMemory"/>);
/// a coluna aberta e as linhas em colunas são o padrão de quem nunca mexeu.
/// </summary>
public sealed class EditorLayoutMemory(string? directory = null) : BoolMemory(directory, "editor-layout.json")
{
    private const string ScenesCollapsedKey = "scenes-collapsed";
    private const string ScriptDensityKey = "lines-script";

    public bool ScenesCollapsed
    {
        get => Get(ScenesCollapsedKey) ?? false;
        set => Set(ScenesCollapsedKey, value);
    }

    /// <summary>A grade mostra cada linha como roteiro (o original em cima, a tradução embaixo); falso é a grade em colunas.</summary>
    public bool ScriptDensity
    {
        get => Get(ScriptDensityKey) ?? false;
        set => Set(ScriptDensityKey, value);
    }
}
