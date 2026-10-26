using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class ConferenceView : UserControl
{
    private readonly HashSet<Key> held = [];

    public ConferenceView()
    {
        InitializeComponent();

        // Enter faz "Corrigir e ir para o próximo", e a lista não pode engolir a tecla (por isso o túnel); botão focado usa o Enter dele. Segurar a tecla repete o KeyDown: sem o filtro, a lista corrigiria um problema atrás do outro.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => held.Remove(e.Key), RoutingStrategies.Tunnel);
        LostFocus += (_, _) => held.Clear();
    }

    /// <summary>Ao abrir a página o foco fica nela, para o Enter valer sem um clique antes.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => Root.Focus(), DispatcherPriority.Input);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Handled || e.KeyModifiers != KeyModifiers.None || e.Source is Button || DataContext is not ConferenceViewModel vm)
        {
            return;
        }

        if (!held.Add(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (vm.FixAndNextCommand.CanExecute(null))
        {
            vm.FixAndNextCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Duplo clique na ocorrência abre a linha no Editor, igual ao botão "Abrir linha". Duplo clique num botão da linha não conta.</summary>
    private void OnIssueDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        if (sender is Control { DataContext: ConferenceIssueRowViewModel row } && DataContext is ConferenceViewModel vm && vm.OpenLineCommand.CanExecute(row))
        {
            vm.OpenLineCommand.Execute(row);
            e.Handled = true;
        }
    }
}
