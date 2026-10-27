using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

/// <summary>A lista de cenas do Editor, ligada ao <see cref="EditorViewModel"/>; a grade e o flyout do modo compacto usam o mesmo controle.</summary>
public sealed partial class ScenesPanel : UserControl
{
    /// <summary>Mostra o botão que recolhe a coluna (#393); só a coluna da grade o liga.</summary>
    public static readonly StyledProperty<bool> CanCollapseProperty = AvaloniaProperty.Register<ScenesPanel, bool>(nameof(CanCollapse));

    private EditorViewModel? editor;

    static ScenesPanel() => CanCollapseProperty.Changed.AddClassHandler<ScenesPanel>((panel, e) => panel.CollapseButton.IsVisible = (bool)e.NewValue!);

    public bool CanCollapse
    {
        get => GetValue(CanCollapseProperty);
        set => SetValue(CanCollapseProperty, value);
    }

    public ScenesPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as EditorViewModel);
    }

    private void Attach(EditorViewModel? value)
    {
        if (editor is not null)
        {
            editor.PropertyChanged -= OnEditorChanged;
        }

        editor = value;
        if (editor is not null)
        {
            editor.PropertyChanged += OnEditorChanged;
        }
    }

    /// <summary>O campo do nome do capítulo novo abre já com o cursor nele.</summary>
    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.IsNamingChapter) && editor is { IsNamingChapter: true })
        {
            Dispatcher.UIThread.Post(() => ChapterNameBox.Focus(), DispatcherPriority.Input);
        }
    }

    /// <summary>
    /// Monta o menu da cena a cada abertura, porque os capítulos mudam: "Mover para o capítulo" com a lista, "Novo capítulo..." e "Voltar ao agrupamento
    /// automático". Valem para as cenas marcadas na lista ou, sem marcação, para a aberta.
    /// </summary>
    private void OnSceneMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu || DataContext is not EditorViewModel vm || vm.Chapters.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        var move = new MenuItem { Header = "Mover para o capítulo" };
        foreach (var chapter in vm.Chapters)
        {
            // Um TextBlock no lugar do texto evita que o "_" de um nome vire tecla de acesso.
            move.Items.Add(new MenuItem { Header = new TextBlock { Text = chapter.Name }, Command = chapter.MoveHereCommand });
        }

        menu.Items.Clear();
        menu.Items.Add(move);
        menu.Items.Add(new MenuItem { Header = "Novo capítulo...", Command = vm.BeginNewChapterCommand });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Voltar ao agrupamento automático", Command = vm.ResetChaptersCommand });
    }
}
