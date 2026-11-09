using VNForge.Core.Dictionaries;
using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>O texto que o build gravaria para uma linha, que é o que as regras de codificação e de caixa medem.</summary>
internal static class BuiltText
{
    /// <summary>
    /// A tradução depois do pipeline do perfil, quando a linha é de um papel que o perfil formata (como o build faz); sem tradução a gravar
    /// ou com marcador inválido devolve nulo, porque as outras regras é que apontam isso. <paramref name="dictionary"/> dá o falante e a cena às regras
    /// do pós-dicionário com condição; sem ele só valem as regras sem condição e as de trecho do original.
    /// </summary>
    public static string? For(TextUnit unit, FormattingProfile profile, DictionaryContext? dictionary = null)
    {
        if (!unit.NeedsRewrite)
        {
            return null;
        }

        var text = unit.Translation!;
        if (!profile.Kinds.Contains(unit.Kind))
        {
            return text;
        }

        try
        {
            return FormattingPipeline.Create(profile).Format(text, (dictionary ?? DictionaryContext.Empty).Line(unit)).Text;
        }
        catch (Exception ex) when (ex is ArgumentException or FormattingException)
        {
            return null;
        }
    }

    /// <summary>Trechos de texto comum, sem os marcadores de controle; marcador inválido não deixa nada a conferir.</summary>
    public static IEnumerable<string> PlainSegments(string text)
    {
        try
        {
            return ControlMarkers.Split(text).Where(s => !s.IsMarker && s.Text.Length > 0).Select(s => s.Text).ToList();
        }
        catch (ArgumentException)
        {
            return [];
        }
    }
}
