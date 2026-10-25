using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class EditorView : UserControl
{
    private EditorViewModel? current;

    private Flyout SceneFlyout => (Flyout)ScenesTrigger.Flyout!;

    private ColumnDefinition ScenesColumn => Columns.ColumnDefinitions[0];

    private ColumnDefinition InspectorColumn => Columns.ColumnDefinitions[2];

    public EditorView()
    {
        InitializeComponent();

        // O campo de tradução (AvaloniaEdit) usa Ctrl+H como Backspace, herança do WPF; descendo pelo túnel, os atalhos da busca chegam antes dele.
        AddHandler(KeyDownEvent, OnSearchShortcut, RoutingStrategies.Tunnel);
        SceneFlyout.Opening += (_, _) => ((ScenesPanel)SceneFlyout.Content!).DataContext = current;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            ApplyBreakpoints();
        }
    }

    /// <summary>Abaixo de 1400 px de janela: cenas num flyout do cabeçalho, botões secundários como ícones e o inspetor em abas, com 360 px.</summary>
    private void ApplyBreakpoints()
    {
        if (Bounds.Width <= 0)
        {
            return;
        }

        var windowWidth = EditorBreakpoints.WindowWidth(Bounds.Width);
        var flyout = EditorBreakpoints.UsesSceneFlyout(windowWidth);
        var tabs = EditorBreakpoints.UsesInspectorTabs(windowWidth);
        Classes.Set("scenes-flyout", flyout);
        LineDetailView.SetUseTabs(this, tabs);

        ScenesBorder.IsVisible = !flyout;
        ScenesColumn.MinWidth = flyout ? 0 : 180;
        ScenesColumn.MaxWidth = flyout ? 0 : 248;
        ScenesColumn.Width = flyout ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        // Sem a coluna das cenas sobra o espaço entre colunas ao lado dela.
        Columns.Margin = new Thickness(flyout ? -Columns.ColumnSpacing : 0, 0, 0, 0);

        InspectorColumn.MinWidth = tabs ? EditorBreakpoints.InspectorTabsWidth : 320;
        InspectorColumn.MaxWidth = tabs ? EditorBreakpoints.InspectorTabsWidth : 420;
        InspectorColumn.Width = tabs ? new GridLength(EditorBreakpoints.InspectorTabsWidth) : new GridLength(1.69, GridUnitType.Star);
    }

    private void OnSearchShortcut(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control || current is null)
        {
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
    }

    /// <summary>Fechar o painel tira o foco do campo que sumiu; ele volta para o texto da linha, para os atalhos do Editor continuarem valendo.</summary>
    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.SelectedScript))
        {
            SceneFlyout.Hide();
        }

        if (e.PropertyName == nameof(EditorViewModel.IsSearchOpen) && current is { IsSearchOpen: false })
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

    /// <summary>Ctrl+F e Ctrl+H abrem o painel; o foco vai para o campo da busca.</summary>
    private void OnSearchOpened(object? sender, bool replace) => SearchPanel.FocusField(replace);
}
