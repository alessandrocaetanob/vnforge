using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace VNForge.App.Controls;

/// <summary>
/// O diálogo sobre a tela (v3.css, <c>.scrim</c> e <c>.dialog</c>): um fundo escurecido que cobre o que está embaixo e um cartão no alto, sem janela nova.
/// Fica como última filha do <c>Grid</c> da página (sem <c>RowSpan</c> nem <c>ColumnSpan</c> ele cobre só a célula); fechado, não ocupa nem captura nada. <c>Escape</c> fecha e devolve o foco a quem o tinha.
/// <c>Content</c> é o corpo, <see cref="Footer"/> a faixa de botões e <see cref="Title"/> o título.
/// </summary>
public sealed class ScreenDialog : ContentControl
{
    public static readonly StyledProperty<bool> IsOpenProperty = AvaloniaProperty.Register<ScreenDialog, bool>(nameof(IsOpen), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<ScreenDialog, string?>(nameof(Title));
    public static readonly StyledProperty<object?> FooterProperty = AvaloniaProperty.Register<ScreenDialog, object?>(nameof(Footer));
    public static readonly StyledProperty<ICommand?> CloseCommandProperty = AvaloniaProperty.Register<ScreenDialog, ICommand?>(nameof(CloseCommand));

    private IInputElement? previousFocus;
    private Button? closeButton;

    public ScreenDialog() => Focusable = true;

    protected override Type StyleKeyOverride => typeof(ScreenDialog);

    /// <summary>Disparado quando o diálogo fecha, por <c>Escape</c>, pelo botão de fechar ou por <see cref="Close"/>.</summary>
    public event EventHandler? Closed;

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    /// <summary>Comando extra que roda quando o diálogo fecha (o botão de fechar, <c>Escape</c>).</summary>
    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        CloseCommand?.Execute(null);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsOpenProperty)
        {
            return;
        }

        PseudoClasses.Set(":open", IsOpen);
        if (IsOpen)
        {
            previousFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
        }
        else if (previousFocus is { } element)
        {
            previousFocus = null;
            Dispatcher.UIThread.Post(() => element.Focus(), DispatcherPriority.Input);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsOpen && e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (closeButton is not null)
        {
            closeButton.Click -= OnCloseClicked;
        }

        closeButton = e.NameScope.Find<Button>("PART_Close");
        if (closeButton is not null)
        {
            closeButton.Click += OnCloseClicked;
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}