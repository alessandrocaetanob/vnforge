using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class EditorView : UserControl
{
    private EditorViewModel? current;
    private TopLevel? watchedTopLevel;
    private double windowSize;

    private Flyout SceneFlyout => (Flyout)ScenesTrigger.Flyout!;

    private ColumnDefinition ScenesColumn => Columns.ColumnDefinitions[0];

    private ColumnDefinition InspectorColumn => Columns.ColumnDefinitions[2];

    private bool scenesInFlyout;

    public EditorView()
    {
        InitializeComponent();

        // O campo de tradução (AvaloniaEdit) usa Ctrl+H como Backspace, herança do WPF; descendo pelo túnel, os atalhos da busca chegam antes dele.
        AddHandler(KeyDownEvent, OnSearchShortcut, RoutingStrategies.Tunnel);
        SceneFlyout.Opening += (_, _) => ((ScenesPanel)SceneFlyout.Content!).DataContext = current;
        // O flyout é outra janela: o Ctrl+B dentro dele não passa pelo Editor e precisa fechá-lo daqui.
        ((ScenesPanel)SceneFlyout.Content!).AddHandler(KeyDownEvent, OnFlyoutShortcut, RoutingStrategies.Tunnel);
    }

    /// <summary>A largura que decide o layout é a da janela, não a do Editor: o trilho de navegação tira uma fatia dela.</summary>
    private void OnTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == TopLevel.ClientSizeProperty)
        {
            ApplyBreakpoints();
        }
    }

    /// <summary>Abaixo de 1400 px de janela: cenas num flyout do cabeçalho, botões secundários como ícones e o inspetor em abas, com 360 px.</summary>
    private void ApplyBreakpoints()
    {
        var windowWidth = TopLevel.GetTopLevel(this)?.ClientSize.Width ?? 0;
        if (windowWidth <= 0)
        {
            return;
        }

        windowSize = windowWidth;
        var tabs = EditorBreakpoints.UsesInspectorTabs(windowWidth);
        ApplyScenesColumn();
        LineDetailView.SetUseTabs(this, tabs);

        InspectorColumn.MinWidth = tabs ? EditorBreakpoints.InspectorTabsWidth : EditorBreakpoints.InspectorMinWidth;
        InspectorColumn.MaxWidth = tabs ? EditorBreakpoints.InspectorTabsWidth : EditorBreakpoints.InspectorMaxWidth;
        InspectorColumn.Width = tabs ? new GridLength(EditorBreakpoints.InspectorTabsWidth) : new GridLength(EditorBreakpoints.InspectorStars, GridUnitType.Star);
    }

    /// <summary>
    /// A lista de cenas na grade: a coluna inteira, a faixa do estado recolhido (#393) ou, abaixo de 1400 px, nenhuma (o flyout do cabeçalho a substitui).
    /// Roda também quando a pessoa recolhe ou abre, sem refazer o inspetor.
    /// </summary>
    private void ApplyScenesColumn()
    {
        if (windowSize <= 0)
        {
            return;
        }

        var placement = EditorBreakpoints.PlaceScenes(windowSize, current?.IsScenesCollapsed ?? false);
        var flyout = placement == ScenesPlacement.Flyout;
        if (!flyout)
        {
            SceneFlyout.Hide();
        }

        scenesInFlyout = flyout;
        Classes.Set("scenes-flyout", flyout);

        ScenesBorder.IsVisible = placement == ScenesPlacement.Column;
        ScenesStrip.IsVisible = placement == ScenesPlacement.Strip;
        var strip = EditorBreakpoints.ScenesStripWidth;
        ScenesColumn.MinWidth = placement switch { ScenesPlacement.Flyout => 0, ScenesPlacement.Strip => strip, _ => 180 };
        ScenesColumn.MaxWidth = placement switch { ScenesPlacement.Flyout => 0, ScenesPlacement.Strip => strip, _ => EditorBreakpoints.ScenesMaxWidth };
        ScenesColumn.Width = placement switch
        {
            ScenesPlacement.Flyout => new GridLength(0),
            ScenesPlacement.Strip => new GridLength(strip),
            _ => new GridLength(1, GridUnitType.Star),
        };
        // Sem a coluna das cenas sobra o espaço entre colunas ao lado dela.
        Columns.Margin = new Thickness(flyout ? -Columns.ColumnSpacing : 0, 0, 0, 0);
    }

    /// <summary>Ctrl+B: recolhe ou abre a coluna das cenas; abaixo de 1400 px, onde a lista é um flyout, abre ou fecha o flyout.</summary>
    private void ToggleScenes()
    {
        if (current is { IsReadingScene: true })
        {
            return;
        }

        if (!scenesInFlyout)
        {
            current?.ToggleScenesCommand.Execute(null);
        }
        else if (SceneFlyout.IsOpen)
        {
            SceneFlyout.Hide();
        }
        else
        {
            SceneFlyout.ShowAt(ScenesTrigger);
        }
    }

    private void OnFlyoutShortcut(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.B && e.KeyModifiers == KeyModifiers.Control)
        {
            SceneFlyout.Hide();
            e.Handled = true;
        }
    }

    private void OnSearchShortcut(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control || current is null)
        {
            return;
        }

        if (e.Key == Key.B)
        {
            ToggleScenes();
            e.Handled = true;
            return;
        }

        var command = e.Key switch
        {
            Key.F => current.OpenSearchCommand,
            Key.H => current.OpenReplaceCommand,
            _ => null,
        };
        if (command is not null)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Voltar ao Editor depois de Ajustes: o perfil pode ter mudado, e a prévia da linha aberta tem de acompanhar.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as EditorViewModel)?.RefreshPreview();
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            watchedTopLevel = top;
            top.PropertyChanged += OnTopLevelPropertyChanged;
            ApplyBreakpoints();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (watchedTopLevel is not null)
        {
            watchedTopLevel.PropertyChanged -= OnTopLevelPropertyChanged;
            watchedTopLevel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (current is not null)
        {
            current.SearchOpened -= OnSearchOpened;
            current.PropertyChanged -= OnEditorPropertyChanged;
        }

        current = DataContext as EditorViewModel;
        if (current is not null)
        {
            current.SearchOpened += OnSearchOpened;
            current.PropertyChanged += OnEditorPropertyChanged;
        }

        ApplyScenesColumn();
    }

    /// <summary>Fechar o painel de busca ou o modo revisão tira o foco do campo que sumiu; ele volta para o texto da linha, para os atalhos do Editor continuarem valendo.</summary>
    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.SelectedScript))
        {
            SceneFlyout.Hide();
        }
        else if (e.PropertyName == nameof(EditorViewModel.IsScenesCollapsed))
        {
            ApplyScenesColumn();
        }
        else if (e.PropertyName == nameof(EditorViewModel.IsScriptDensity))
        {
            KeepSelectedLineInView();
        }

        if (e.PropertyName == nameof(EditorViewModel.EmptyActionText))
        {
            // SelectFilter notifica EmptyActionText a cada troca de filtro. A faixa de filtros rola quando a janela é estreita; o filtro que acabou de ligar (por exemplo "Soa estranho", ao sair do leitor) não pode ficar fora da vista.
            Dispatcher.UIThread.Post(
                () => this.GetVisualDescendants().OfType<FilterChip>().FirstOrDefault(c => c.IsEffectivelyVisible && c.IsChecked == true)?.BringIntoView(),
                DispatcherPriority.Loaded);
        }

        if ((e.PropertyName == nameof(EditorViewModel.IsSearchOpen) && current is { IsSearchOpen: false })
            || (e.PropertyName == nameof(EditorViewModel.IsReviewing) && current is { IsReviewing: false })
            || (e.PropertyName == nameof(EditorViewModel.IsReadingScene) && current is { IsReadingScene: false }))
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (this.GetVisualDescendants().OfType<LineDetailView>().FirstOrDefault() is { } detail)
                    {
                        detail.FocusTranslation();
                    }
                    else
                    {
                        LinesList.Focus();
                    }
                },
                DispatcherPriority.Input);
        }
    }

    /// <summary>
    /// Trocar a densidade muda a altura das linhas e a rolagem, que está em pixels, passa a apontar para outras linhas: depois do novo layout a linha aberta volta para a tela.
    /// </summary>
    private void KeepSelectedLineInView() =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (current?.SelectedLine is { } line)
                {
                    LinesList.ScrollIntoView(line);
                }
            },
            DispatcherPriority.Loaded);

    /// <summary>Ctrl+F e Ctrl+H abrem o painel; o foco vai para o campo da busca.</summary>
    private void OnSearchOpened(object? sender, bool replace) => SearchPanel.FocusField(replace);
}
