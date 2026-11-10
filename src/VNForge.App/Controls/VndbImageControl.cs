using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Services;
using VNForge.Core.Vndb;

namespace VNForge.App.Controls;

/// <summary>
/// A base de <see cref="Portrait"/> e <see cref="Cover"/>: mostra uma imagem do VNDB do <see cref="VndbImages.Source"/> ou, enquanto ela não chega, quando não há imagem
/// e quando a privacidade da Biblioteca a esconde, o marcador que o quadro desenha. A imagem vem do cache fora da thread de UI; a tela nunca espera por ela.
/// </summary>
public abstract class VndbImageControl : Control
{
    public static readonly StyledProperty<VndbImageRef?> ImageProperty = AvaloniaProperty.Register<VndbImageControl, VndbImageRef?>(nameof(Image));
    public static readonly StyledProperty<string?> SeedProperty = AvaloniaProperty.Register<VndbImageControl, string?>(nameof(Seed));

    public static readonly StyledProperty<bool> LoadOnlyInViewportProperty = AvaloniaProperty.Register<VndbImageControl, bool>(nameof(LoadOnlyInViewport));

    private bool inViewport = true;
    private BitmapLease? lease;
    private CancellationTokenSource? loading;
    private IVndbImageSource? watching;

    static VndbImageControl()
    {
        AffectsRender<VndbImageControl>(SeedProperty);
        // O controle tem tamanho próprio: esticar na célula do pai o deformaria.
        HorizontalAlignmentProperty.OverrideDefaultValue<VndbImageControl>(Avalonia.Layout.HorizontalAlignment.Left);
        VerticalAlignmentProperty.OverrideDefaultValue<VndbImageControl>(Avalonia.Layout.VerticalAlignment.Top);
        ImageProperty.Changed.AddClassHandler<VndbImageControl>((control, _) => control.Reload());
    }

    protected VndbImageControl()
    {
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    /// <summary>A imagem do VNDB a mostrar; nula mostra o marcador.</summary>
    public VndbImageRef? Image
    {
        get => GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    /// <summary>O nome (do personagem ou do jogo) que escolhe a cor do marcador, para que o mesmo nome tenha sempre a mesma cor.</summary>
    public string? Seed
    {
        get => GetValue(SeedProperty);
        set => SetValue(SeedProperty, value);
    }

    /// <summary>
    /// Só baixa e segura o bitmap enquanto o controle está perto da área visível (a da rolagem que o contém, com uma tela de folga). A grade da Biblioteca liga: com centenas de jogos, só as
    /// capas à vista ocupam memória, e as que saem de vista devolvem o bitmap.
    /// </summary>
    public bool LoadOnlyInViewport
    {
        get => GetValue(LoadOnlyInViewportProperty);
        set => SetValue(LoadOnlyInViewportProperty, value);
    }

    /// <summary>A imagem do VNDB está na tela; falso quando o controle mostra o marcador.</summary>
    public bool HasImage => lease is not null;

    /// <summary>O bitmap já carregado; nulo mostra o marcador.</summary>
    protected Bitmap? LoadedBitmap => lease?.Bitmap;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (LoadOnlyInViewport)
        {
            inViewport = false;
            EffectiveViewportChanged += OnEffectiveViewportChanged;
        }

        if (VndbImages.Source is { } source)
        {
            watching = source;
            source.PrivacyChanged += OnPrivacyChanged;
        }

        Reload();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelLoading();
        EffectiveViewportChanged -= OnEffectiveViewportChanged;
        if (watching is not null)
        {
            watching.PrivacyChanged -= OnPrivacyChanged;
            watching = null;
        }

        SetLease(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ImagePeer(this);

    /// <summary>O nome que o leitor de tela diz quando a tela não deu um (<c>AutomationProperties.Name</c>).</summary>
    protected abstract string DefaultAutomationName { get; }

    // A privacidade pode mudar em qualquer thread (a CLI trocou o ajuste): a tela relê a imagem na UI e a esconde se for o caso.
    private void OnPrivacyChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Reload);

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var view = e.EffectiveViewport;
        var near = view.Width > 0 && view.Height > 0
            && new Rect(view.X - view.Width, view.Y - view.Height, view.Width * 3, view.Height * 3).Intersects(new Rect(Bounds.Size));
        if (near == inViewport)
        {
            return;
        }

        inViewport = near;
        Reload();
    }

    private void Reload()
    {
        CancelLoading();
        SetLease(null);
        if (Image is { } image && VndbImages.Source is { } source && VisualRoot is not null && (inViewport || !LoadOnlyInViewport))
        {
            if (source.TryGet(image) is { } ready)
            {
                SetLease(ready);

                // O acerto na memória não passa pela leitura da privacidade: confere em segundo plano, e se o ajuste mudou o controle se relê.
                source.RevalidatePrivacy();
            }
            else
            {
                var cts = loading = new CancellationTokenSource();
                _ = LoadAsync(source, image, cts);
            }
        }
    }

    private void SetLease(BitmapLease? next)
    {
        var previous = lease;
        lease = next;
        if (previous is not null || next is not null)
        {
            InvalidateVisual();
        }

        previous?.Dispose();
    }

    private async Task LoadAsync(IVndbImageSource source, VndbImageRef image, CancellationTokenSource owner)
    {
        BitmapLease? loaded = null;
        try
        {
            loaded = await source.LoadAsync(image, owner.Token);
            if (!owner.IsCancellationRequested && loaded is not null)
            {
                SetLease(loaded);
                loaded = null;
            }
        }
        catch (OperationCanceledException)
        {
            // A imagem mudou ou o controle saiu da tela: quem cancelou já cuidou do estado.
        }
        finally
        {
            // Uma carga que chegou depois de a imagem mudar devolve a posse sem mostrar nada.
            loaded?.Dispose();
            if (ReferenceEquals(loading, owner))
            {
                loading = null;
            }

            owner.Dispose();
        }
    }

    private void CancelLoading()
    {
        loading?.Cancel();
        loading = null;
    }

    /// <summary>Quanto desenhar a imagem em <paramref name="target"/> sem deformá-la: ocupa tudo e corta o que sobra; <paramref name="topBias"/> de 0 (alto) a 1 (pé) escolhe de onde cortar.</summary>
    protected static Rect SourceRectFor(PixelSize image, Size target, double topBias)
    {
        var scale = Math.Max(target.Width / image.Width, target.Height / image.Height);
        var width = target.Width / scale;
        var height = target.Height / scale;
        return new Rect((image.Width - width) / 2, (image.Height - height) * topBias, width, height);
    }

    protected IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : fallback;

    protected double Token(string key, double fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is double value ? value : fallback;

    protected CornerRadius Radius(string key, double fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is CornerRadius value ? value : new CornerRadius(fallback);

    /// <summary>A matiz (0 a 359) do marcador do <paramref name="seed"/>: um hash fixo (FNV-1a), que não muda de uma execução para outra.</summary>
    internal static double HueOf(string? seed)
    {
        var hash = 2166136261u;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(seed ?? string.Empty))
        {
            hash = (hash ^ b) * 16777619u;
        }

        return hash % 360;
    }

    internal static Color Hsl(double hue, double saturation, double lightness) => new HslColor(1, hue, saturation, lightness).ToRgb();

    private sealed class ImagePeer(VndbImageControl owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string? GetNameCore() =>
            AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : owner.DefaultAutomationName;
    }
}
