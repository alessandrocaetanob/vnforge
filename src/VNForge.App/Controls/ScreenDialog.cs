using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace VNForge.App.Controls;

/// <summary>
/// O diálogo sobre a tela (v3.css, <c>.scrim</c> e <c>.dialog</c>): um fundo escurecido que cobre a janela inteira e um cartão no alto, sem janela nova.
/// Aberto, o cartão vive na <see cref="OverlayLayer"/> do Avalonia, acima de tudo e com o tamanho da janela; este controle fica onde o XAML o pôs,
/// sem ocupar nem capturar nada, e leva <c>DataContext</c>, <c>Content</c>, <see cref="Footer"/> e <see cref="Title"/> para o cartão.
/// O foco fica preso no cartão (Tab cicla entre os controles dele), <c>Escape</c> e o botão de fechar fecham, e o foco volta a quem o tinha.
/// <see cref="Closed"/> e <see cref="CloseCommand"/> disparam sempre que <see cref="IsOpen"/> passa a falso, por <c>Escape</c>, pelo botão, por <see cref="Close"/> ou por binding.
/// </summary>
/// <remarks>Como o cartão sai da árvore da página, estilos declarados só dentro dela não alcançam o corpo do diálogo; os estilos do aplicativo e o tema valem.</remarks>
public sealed class ScreenDialog : ContentControl
{
    public static readonly StyledProperty<bool> IsOpenProperty = AvaloniaProperty.Register<ScreenDialog, bool>(nameof(IsOpen), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<ScreenDialog, string?>(nameof(Title));
    public static readonly StyledProperty<object?> FooterProperty = AvaloniaProperty.Register<ScreenDialog, object?>(nameof(Footer));
    public static readonly StyledProperty<ICommand?> CloseCommandProperty = AvaloniaProperty.Register<ScreenDialog, ICommand?>(nameof(CloseCommand));

    private readonly List<IDisposable> bindings = [];
    private IInputElement? previousFocus;
    private ScreenDialogSurface? surface;
    private OverlayLayer? layer;

    protected override Type StyleKeyOverride => typeof(ScreenDialog);

    /// <summary>Disparado quando o diálogo fecha, por <c>Escape</c>, pelo botão de fechar, por <see cref="Close"/> ou quando <see cref="IsOpen"/> vira falso por binding.</summary>
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

    /// <summary>Comando extra que roda quando o diálogo fecha (o botão de fechar, <c>Escape</c>, ou <see cref="IsOpen"/> falso).</summary>
    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public void Close() => IsOpen = false;

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
            Show();
            return;
        }

        Hide();
        if (change.OldValue is true)
        {
            CloseCommand?.Execute(null);
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (IsOpen)
        {
            Show();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Hide(restoreFocus: false);
        base.OnDetachedFromVisualTree(e);
    }

    private void Show()
    {
        if (surface is not null || OverlayLayer.GetOverlayLayer(this) is not { } overlay)
        {
            return;
        }

        previousFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var card = new ScreenDialogSurface(Close);
        surface = card;
        layer = overlay;
        bindings.Add(card.Bind(DataContextProperty, this.GetObservable(DataContextProperty)));
        bindings.Add(card.Bind(ContentProperty, this.GetObservable(ContentProperty)));
        bindings.Add(card.Bind(ContentTemplateProperty, this.GetObservable(ContentTemplateProperty)));
        bindings.Add(card.Bind(ScreenDialogSurface.TitleProperty, this.GetObservable(TitleProperty)));
        bindings.Add(card.Bind(ScreenDialogSurface.FooterProperty, this.GetObservable(FooterProperty)));
        bindings.Add(overlay.GetObservable(BoundsProperty).Subscribe(new ActionObserver<Rect>(bounds =>
        {
            card.Width = bounds.Width;
            card.Height = bounds.Height;
        })));
        overlay.Children.Add(card);
        Dispatcher.UIThread.Post(() => card.Focus(), DispatcherPriority.Input);
    }

    private void Hide(bool restoreFocus = true)
    {
        if (surface is not { } card)
        {
            return;
        }

        foreach (var binding in bindings)
        {
            binding.Dispose();
        }

        bindings.Clear();
        layer?.Children.Remove(card);
        card.Content = null;
        card.Footer = null;
        surface = null;
        layer = null;
        var element = previousFocus;
        previousFocus = null;
        if (restoreFocus && element is not null)
        {
            Dispatcher.UIThread.Post(() => element.Focus(), DispatcherPriority.Input);
        }
    }

    private sealed class ActionObserver<T>(Action<T> next) : IObserver<T>
    {
        public void OnNext(T value) => next(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}

/// <summary>O cartão do <see cref="ScreenDialog"/> na camada de sobreposição: o fundo escurecido, o cartão e o ciclo de Tab dentro dele.</summary>
internal sealed class ScreenDialogSurface : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<ScreenDialogSurface, string?>(nameof(Title));
    public static readonly StyledProperty<object?> FooterProperty = AvaloniaProperty.Register<ScreenDialogSurface, object?>(nameof(Footer));

    private readonly Action close;
    private Button? closeButton;

    public ScreenDialogSurface(Action close)
    {
        this.close = close;
        Focusable = true;
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            close();
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

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => close();
}
