using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Styling;

namespace VNForge.App.Controls;

/// <summary>Um nome de uma lista com a nota que o explica: "Black" e "Hair".</summary>
public sealed record NotedName(string Name, string Note);

/// <summary>
/// Lista de nomes separados por vírgula, com a nota de cada um entre parênteses numa cor mais apagada: <c>NotedNames.Source</c> num <see cref="TextBlock"/> escreve "Long Hair (Hair), Black (Eyes)".
/// É como a revisão da ficha mostra o original dos traços, onde só o nome não diz qual traço é.
/// </summary>
public static class NotedNames
{
    public static readonly AttachedProperty<IReadOnlyList<NotedName>?> SourceProperty = AvaloniaProperty.RegisterAttached<TextBlock, IReadOnlyList<NotedName>?>("Source", typeof(NotedNames));

    static NotedNames() => SourceProperty.Changed.AddClassHandler<TextBlock>((block, e) => Fill(block, e.NewValue as IReadOnlyList<NotedName>));

    public static IReadOnlyList<NotedName>? GetSource(TextBlock block) => block.GetValue(SourceProperty);

    public static void SetSource(TextBlock block, IReadOnlyList<NotedName>? value) => block.SetValue(SourceProperty, value);

    private static void Fill(TextBlock block, IReadOnlyList<NotedName>? names)
    {
        block.Text = null;
        block.Inlines ??= [];
        block.Inlines.Clear();
        if (names is null)
        {
            return;
        }

        for (var i = 0; i < names.Count; i++)
        {
            block.Inlines.Add(new Run(i == 0 ? names[i].Name : ", " + names[i].Name));
            if (names[i].Note.Length > 0)
            {
                var note = new Run(" (" + names[i].Note + ")");
                note.Bind(TextElement.ForegroundProperty, block.GetResourceObservable("Vnf.TextTertiary"));
                block.Inlines.Add(note);
            }
        }
    }
}
