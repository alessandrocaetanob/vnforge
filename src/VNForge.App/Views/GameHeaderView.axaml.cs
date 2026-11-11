using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

/// <summary>O cabeçalho do jogo aberto (volta, capa, nome, engine, andamento, próximo passo), as abas Ficha, Tradução e Jogar e a segunda linha da Tradução.</summary>
public sealed partial class GameHeaderView : UserControl
{
    public static readonly StyledProperty<bool> IsCompactProperty = AvaloniaProperty.Register<GameHeaderView, bool>(nameof(IsCompact));
    public static readonly StyledProperty<bool> IsEditorProperty = AvaloniaProperty.Register<GameHeaderView, bool>(nameof(IsEditor));
    public static readonly StyledProperty<bool> IsImagesProperty = AvaloniaProperty.Register<GameHeaderView, bool>(nameof(IsImages));

    /// <summary>A largura da capa, em pixels.</summary>
    public const double CoverWidth = 56;

    /// <summary>O espaço mínimo entre a última aba e a primeira seção quando as duas dividem a linha no cabeçalho compacto.</summary>
    public const double SectionsGap = 24;

    /// <summary>A margem à direita da segunda linha, a mesma do cabeçalho.</summary>
    private const double RightMargin = 32;

    /// <summary>Do andamento (0 a 100) para a largura do preenchimento da barra no pé da capa.</summary>
    public static readonly IValueConverter CoverBarWidth = new FuncValueConverter<double, double>(percent => CoverWidth * Math.Clamp(percent, 0, 100) / 100);

    private MenuFlyout? moreMenu;

    public GameHeaderView()
    {
        InitializeComponent();
        Sections.Tapped += OnSectionsTapped;
        Sections.KeyDown += OnSectionsKeyDown;
        LayoutUpdated += (_, _) =>
        {
            UpdateSectionsPlacement();
            UpdateActionsPlacement();
        };
        UpdateHeaderLayout();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCompactProperty)
        {
            Classes.Set("compact", IsCompact);
            UpdateHeaderLayout();
            UpdateSectionsPlacement();
        }
        else if (change.Property == IsEditorProperty)
        {
            Classes.Set("editor", IsEditor);
            UpdateHeaderLayout();
        }
        else if (change.Property == IsImagesProperty)
        {
            UpdateHeaderLayout();
        }
    }

    /// <summary>Qual das três faces do topo aparece: a do Editor (o quadro do #546, com as ações da grade), a compacta ou a completa das outras páginas.</summary>
    private void UpdateHeaderLayout()
    {
        var editor = IsEditor && !IsCompact;
        EditorHeader.IsVisible = editor;
        EditorActions.IsVisible = IsEditor;
        ImagesActions.IsVisible = IsImages && Sections.IsVisible;
        ReadAsPlayerCompact.IsVisible = IsEditor && IsCompact;
        PlaceActions(EditorActions, IsCompact ? 0 : 1);
        PlaceActions(ImagesActions, IsCompact ? 0 : 1);
        Regular.IsVisible = !editor && !IsCompact;
        Compact.IsVisible = !editor && IsCompact;
    }

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>A página aberta é o Editor: o topo traz o cabeçalho do quadro do #546 em vez do completo das outras páginas. Vale só quando <see cref="IsCompact"/> é falso.</summary>
    public bool IsEditor
    {
        get => GetValue(IsEditorProperty);
        set => SetValue(IsEditorProperty, value);
    }

    /// <summary>A página aberta é Imagens: "Abrir inventário" fica na linha do seletor de seções, à direita, como no quadro do #587, e desce para a linha de baixo quando não cabe ao lado.</summary>
    public bool IsImages
    {
        get => GetValue(IsImagesProperty);
        set => SetValue(IsImagesProperty, value);
    }

    /// <summary>
    /// No compacto a segunda linha sobe para a linha das abas; ela só sobe se couber ao lado da última aba, e o limite vem da largura medida das abas e dos
    /// itens (nenhum número fixo). Sem espaço, desce para a própria linha, sob as abas, e o cabeçalho cresce para cerca de 120 px.
    /// </summary>
    private void UpdateSectionsPlacement()
    {
        var below = IsCompact && !SectionsFitBesideTabs(out var known) && known;
        if (Classes.Contains("sections-below") != below)
        {
            Classes.Set("sections-below", below);
        }
    }

    /// <summary>Linha 0: na linha do nome, no compacto. Linha 1: ao lado do seletor de seções. Linha 2: sob o seletor, quando não cabe ao lado.</summary>
    private static void PlaceActions(StackPanel actions, int row)
    {
        if (Grid.GetRow(actions) != row)
        {
            Grid.SetRow(actions, row);
        }

        actions.VerticalAlignment = row == 1 ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        actions.Margin = row switch
        {
            0 => new Thickness(0, 6, RightMargin, 0),
            2 => new Thickness(0, 8, RightMargin, 0),
            _ => new Thickness(0, 0, RightMargin, 1),
        };
    }

    /// <summary>
    /// No cabeçalho completo do Editor as ações (Roteiro, Buscar e Traduzir com IA) dividem a linha do seletor de seções; sem largura para os dois, descem para uma linha sob ele.
    /// O limite vem das larguras medidas, como o das seções no compacto. No compacto elas ficam na linha do nome, à direita.
    /// </summary>
    private void UpdateActionsPlacement()
    {
        var images = IsImages && Sections.IsVisible;
        if (ImagesActions.IsVisible != images)
        {
            ImagesActions.IsVisible = images;
        }

        if (IsCompact || Bounds.Width <= 0 || Sections.ItemsPanelRoot is not { } panel)
        {
            return;
        }

        var items = panel.Children.Where(c => c.IsVisible).ToList();
        if (items.Count == 0 || items.Any(c => c.DesiredSize.Width <= 0))
        {
            return;
        }

        // A largura do ListBox já vem limitada pela janela; a natural é a soma dos itens, como no compacto.
        var sections = items.Sum(c => c.DesiredSize.Width) + ((items.Count - 1) * 4) + Sections.Padding.Left + Sections.Padding.Right;
        foreach (var actions in new[] { EditorActions, ImagesActions }.Where(a => a.IsVisible && a.DesiredSize.Width > 0))
        {
            var width = actions.DesiredSize.Width - actions.Margin.Left - actions.Margin.Right;
            PlaceActions(actions, 2 * RightMargin + sections + SectionsGap + width > Bounds.Width ? 2 : 1);
        }
    }

    /// <summary>A largura que a segunda linha pede e a que sobra depois das abas; <paramref name="known"/> é falso enquanto os itens ainda não foram medidos.</summary>
    private bool SectionsFitBesideTabs(out bool known)
    {
        known = false;
        if (!Sections.IsVisible || Bounds.Width <= 0 || Sections.ItemsPanelRoot is not { } panel || GameTabs.TranslatePoint(default, this) is not { } origin)
        {
            return true;
        }

        var items = panel.Children.Where(c => c.IsVisible).ToList();
        if (items.Count == 0 || items.Any(c => c.DesiredSize.Width <= 0))
        {
            return true;
        }

        known = true;
        var natural = items.Sum(c => c.DesiredSize.Width) + (items.Count - 1) * 4 + Sections.Padding.Left + Sections.Padding.Right;
        return origin.X + GameTabs.Bounds.Width + SectionsGap + natural + RightMargin <= Bounds.Width;
    }

    private void OnSectionsTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext == vm.MoreSection)
        {
            OpenMoreMenu(vm);
        }
    }

    // Marcar o "Mais" por código (a página é Lançar, Bíblia ou Imagens) não abre o menu: só o toque, e Enter, Espaço ou seta para baixo com o item em foco.
    private void OnSectionsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space or Key.Down && DataContext is MainWindowViewModel vm && e.Source is Visual source
            && source.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext == vm.MoreSection)
        {
            OpenMoreMenu(vm);
            e.Handled = true;
        }
    }

    private void OpenMoreMenu(MainWindowViewModel vm)
    {
        if (moreMenu is { IsOpen: true } || Sections.ContainerFromItem(vm.MoreSection) is not Control target)
        {
            return;
        }

        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var item in vm.MoreSections.Where(i => i.IsVisible))
        {
            menu.Items.Add(new MenuItem { Header = item.Label, IsEnabled = item.IsEnabled, Command = vm.OpenSectionCommand, CommandParameter = item });
        }

        moreMenu = menu;
        menu.ShowAt(target);
    }

    /// <summary>Só para os testes: o menu do "Mais" aberto, para a imagem e para escolher uma página por ele.</summary>
    public MenuFlyout? MoreMenu => moreMenu;

    /// <summary>Só para os testes: abre o menu do "Mais" como o toque no item faz.</summary>
    public void ShowMoreMenu()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            OpenMoreMenu(vm);
        }
    }
}
