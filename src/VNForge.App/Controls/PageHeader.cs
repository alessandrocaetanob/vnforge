using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>
/// Cabeçalho de página: migalha em mono (<c>mono</c> 12/16, <c>text-secondary</c>), título em <c>title-lg</c> e, à direita, as ações (<c>Content</c>),
/// alinhadas à base do título. Usado por Painel, Editor, Conferir, Build e Ajustes.
/// </summary>
public sealed class PageHeader : ContentControl
{
    public static readonly StyledProperty<string?> BreadcrumbProperty = AvaloniaProperty.Register<PageHeader, string?>(nameof(Breadcrumb));
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<PageHeader, string?>(nameof(Title));

    /// <summary>A linha de cima: pasta do projeto e engine, ou caminho do script.</summary>
    public string? Breadcrumb
    {
        get => GetValue(BreadcrumbProperty);
        set => SetValue(BreadcrumbProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}
