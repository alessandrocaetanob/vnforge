using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>Uma fala do contexto da cena: quem fala, o original e, quando já existe, a tradução. <paramref name="Following"/> marca a fala que vem depois da linha aberta.</summary>
public sealed record ContextLine(string Who, string Original, string? Translation = null, bool Following = false);

/// <summary>
/// "Na cena" (v3.css, <c>.ctx</c>): as falas em volta da linha aberta, uma lista de <see cref="ContextLine"/> com quem fala numa coluna de 76 px,
/// o original em japonês e a tradução embaixo. O estilo e o modelo de item estão em <c>ContextLines.axaml</c>.
/// </summary>
public sealed class ContextLines : ItemsControl
{
    protected override Type StyleKeyOverride => typeof(ContextLines);
}