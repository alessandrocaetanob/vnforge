namespace VNForge.App.Services;

/// <summary>
/// Como a pessoa deixou o Editor: hoje, se a coluna das cenas ficou recolhida (#393). Grava <c>editor-layout.json</c> (ver <see cref="BoolMemory"/>);
/// a coluna aberta é o padrão de quem nunca mexeu.
/// </summary>
public sealed class EditorLayoutMemory(string? directory = null) : BoolMemory(directory, "editor-layout.json")
{
    private const string ScenesCollapsedKey = "scenes-collapsed";

    public bool ScenesCollapsed
    {
        get => Get(ScenesCollapsedKey) ?? false;
        set => Set(ScenesCollapsedKey, value);
    }
}
