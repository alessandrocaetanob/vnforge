using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
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

    // Os ids de cada texto, achados uma vez em Fill: o mouse passa por eles a cada movimento.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock, IReadOnlyList<LineIdMention>> Mentions = [];

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    static LineLinks() => SourceProperty.Changed.AddClassHandler<TextBlock>((block, e) => Fill(block, e.NewValue as string));

    public static string? GetSource(TextBlock block) => block.GetValue(SourceProperty);

    public static void SetSource(TextBlock block, string? value) => block.SetValue(SourceProperty, value);

    public static ICommand? GetCommand(TextBlock block) => block.GetValue(CommandProperty);

    public static void SetCommand(TextBlock block, ICommand? value) => block.SetValue(CommandProperty, value);

    private static void Fill(TextBlock block, string? text)
    {
        block.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        block.PointerMoved -= OnMoved;
        Mentions.Remove(block);
        block.Text = null;
        block.Inlines?.Clear();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var mentions = LineIds.Find(text);
        if (mentions.Count == 0)
        {
            block.Text = text;
            return;
        }

        Mentions.Add(block, mentions);
        // SelectableTextBlock trata o PointerPressed para começar a seleção: o link precisa ouvir mesmo assim.
        block.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        block.PointerMoved += OnMoved;

        var position = 0;
        foreach (var mention in mentions)
        {
            if (mention.Start > position)
            {
                block.Inlines?.Add(new Run(text[position..mention.Start]));
            }

            var link = new Run(mention.UnitId) { TextDecorations = TextDecorations.Underline };
            link.Bind(TextElement.ForegroundProperty, new DynamicResourceExtension("Vnf.AccentText"));
            block.Inlines?.Add(link);
            position = mention.Start + mention.Length;
        }

        if (position < text.Length)
        {
            block.Inlines?.Add(new Run(text[position..]));
        }
    }

    private static LineIdMention? MentionUnder(TextBlock block, PointerEventArgs e)
    {
        if (!Mentions.TryGetValue(block, out var mentions))
        {
            return null;
        }

        var point = e.GetPosition(block) - new Point(block.Padding.Left, block.Padding.Top);
        var hit = block.TextLayout.HitTestPoint(point);
        return hit.IsInside ? mentions.FirstOrDefault(m => hit.TextPosition >= m.Start && hit.TextPosition < m.Start + m.Length) : null;
    }

    private static void OnMoved(object? sender, PointerEventArgs e)
    {
        if (sender is TextBlock block)
        {
            block.Cursor = MentionUnder(block, e) is null ? null : HandCursor;
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
