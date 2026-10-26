using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;

namespace VNForge.App.Controls;

/// <summary>
/// Texto de erro em que o id de uma linha (<c>yst00112:033792</c>) é um link: <c>LineLinks.Source</c> num <see cref="TextBlock"/> escreve o texto
/// com cada id em destaque e sublinhado, e clicar num deles manda um <see cref="LineRequest"/> ao <c>LineLinks.Command</c>, que abre a linha no Editor.
/// </summary>
public static class LineLinks
{
    public static readonly AttachedProperty<string?> SourceProperty = AvaloniaProperty.RegisterAttached<TextBlock, string?>("Source", typeof(LineLinks));

    public static readonly AttachedProperty<ICommand?> CommandProperty = AvaloniaProperty.RegisterAttached<TextBlock, ICommand?>("Command", typeof(LineLinks));

    static LineLinks() => SourceProperty.Changed.AddClassHandler<TextBlock>((block, e) => Fill(block, e.NewValue as string));

    public static string? GetSource(TextBlock block) => block.GetValue(SourceProperty);

    public static void SetSource(TextBlock block, string? value) => block.SetValue(SourceProperty, value);

    public static ICommand? GetCommand(TextBlock block) => block.GetValue(CommandProperty);

    public static void SetCommand(TextBlock block, ICommand? value) => block.SetValue(CommandProperty, value);

    /// <summary>O id citado no caractere <paramref name="index"/> de <paramref name="text"/>; nulo quando ali não há id.</summary>
    internal static LineIdMention? MentionAt(string? text, int index) =>
        LineIds.Find(text).FirstOrDefault(m => index >= m.Start && index < m.Start + m.Length);

    private static void Fill(TextBlock block, string? text)
    {
        block.PointerPressed -= OnPressed;
        block.PointerMoved -= OnMoved;
        block.Text = null;
        block.Inlines?.Clear();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        block.PointerPressed += OnPressed;
        block.PointerMoved += OnMoved;
        var position = 0;
        foreach (var mention in LineIds.Find(text))
        {
            if (mention.Start > position)
            {
                block.Inlines?.Add(new Run(text[position..mention.Start]));
            }

            block.Inlines?.Add(new Run(mention.UnitId)
            {
                Foreground = block.TryFindResource("Vnf.AccentText", block.ActualThemeVariant, out var brush) ? brush as IBrush : null,
                TextDecorations = TextDecorations.Underline,
            });
            position = mention.Start + mention.Length;
        }

        if (position < text.Length)
        {
            block.Inlines?.Add(new Run(text[position..]));
        }
    }

    private static LineIdMention? MentionUnder(TextBlock block, PointerEventArgs e)
    {
        var text = GetSource(block);
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var point = e.GetPosition(block) - new Point(block.Padding.Left, block.Padding.Top);
        var hit = block.TextLayout.HitTestPoint(point);
        return hit.IsInside ? MentionAt(text, hit.TextPosition) : null;
    }

    private static void OnMoved(object? sender, PointerEventArgs e)
    {
        if (sender is TextBlock block)
        {
            block.Cursor = MentionUnder(block, e) is null ? null : new Cursor(StandardCursorType.Hand);
        }
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBlock block || !e.GetCurrentPoint(block).Properties.IsLeftButtonPressed || GetCommand(block) is not { } command)
        {
            return;
        }

        if (MentionUnder(block, e) is { } mention)
        {
            var request = new LineRequest(mention.ScriptKey, mention.UnitId);
            if (command.CanExecute(request))
            {
                command.Execute(request);
                e.Handled = true;
            }
        }
    }
}
