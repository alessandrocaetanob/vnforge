using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace VNForge.App.Views;

/// <summary>
/// A página Bíblia (#530); a lógica está em <see cref="ViewModels.BibleViewModel"/>. Os campos de quem aparece, tom, piadas e relações se leem como chips e lista
/// (o quadro não desenha caixa de texto neles): um clique, ou Enter e F2 com o foco na leitura, troca a leitura pela caixa de texto, e sair da caixa a devolve.
/// </summary>
public partial class BibleView : UserControl
{
    private const string Editing = ":editing";

    /// <summary>Abaixo dessa largura de página o botão "Ir para a cena no Editor" fica só com o ícone.</summary>
    private const double CompactBelow = 1200;

    public BibleView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDown);
        AddHandler(LostFocusEvent, OnLostFocus);
        this.Get<Button>("GameRow").Click += (_, _) => FocusSynopsis();
        SizeChanged += (_, e) => Classes.Set("compact", e.NewSize.Width < CompactBelow);
    }

    private void FocusSynopsis()
    {
        var box = this.Get<TextBox>("SynopsisBox");
        box.BringIntoView();
        box.Focus();
        box.CaretIndex = box.Text?.Length ?? 0;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && ReadingOf(source) is { } reading && !e.Handled)
        {
            BeginEdit(reading);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is Border { Classes: var classes } reading && classes.Contains("show") && e.Key is Key.Enter or Key.F2)
        {
            BeginEdit(reading);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && e.Source is TextBox { Classes: var boxClasses } box && boxClasses.Contains("inline-box") && box.GetVisualParent() is Panel field)
        {
            EndEdit(field);
            e.Handled = true;
        }
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox { Classes: var classes } box && classes.Contains("inline-box") && box.GetVisualParent() is Panel field)
        {
            EndEdit(field);
        }
    }

    /// <summary>A leitura (<c>Border.show</c>) de um campo em que o ponteiro caiu, ou nulo se ele caiu fora.</summary>
    private static Border? ReadingOf(Visual source) =>
        source.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("show"));

    private static void BeginEdit(Border reading)
    {
        if (reading.GetVisualParent() is not Panel field)
        {
            return;
        }

        ((IPseudoClasses)field.Classes).Set(Editing, true);
        var box = field.Children.OfType<TextBox>().First();
        Dispatcher.UIThread.Post(
            () =>
            {
                box.Focus();
                box.CaretIndex = box.Text?.Length ?? 0;
            },
            DispatcherPriority.Input);
    }

    private static void EndEdit(Panel field) => ((IPseudoClasses)field.Classes).Set(Editing, false);
}
