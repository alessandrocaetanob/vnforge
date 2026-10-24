using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

/// <summary>O painel de busca e substituição do Editor. A lógica fica no <see cref="SearchReplaceViewModel"/>; aqui só o foco, o clique no resultado e o botão de fechar.</summary>
public sealed partial class SearchReplaceView : UserControl
{
    public static readonly StyledProperty<ICommand?> CloseCommandProperty = AvaloniaProperty.Register<SearchReplaceView, ICommand?>(nameof(CloseCommand));

    public SearchReplaceView()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => CloseCommand?.Execute(null);
        ResultsList.AddHandler(TappedEvent, OnResultTapped, RoutingStrategies.Bubble);
    }

    /// <summary>O que o botão de fechar e o Esc fazem; quem hospeda o painel decide.</summary>
    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    /// <summary>
    /// Põe o foco no campo certo ao abrir o painel: o de busca, ou o de substituição quando o painel abre para substituir (Ctrl+H) e já há um padrão.
    /// A seleção do texto deixa o usuário digitar por cima.
    /// </summary>
    public void FocusField(bool replace)
    {
        var box = replace && ReplacementBox.IsVisible && !string.IsNullOrEmpty(PatternBox.Text) ? ReplacementBox : PatternBox;
        Dispatcher.UIThread.Post(
            () =>
            {
                box.Focus();
                box.SelectAll();
            },
            DispatcherPriority.Input);
    }

    /// <summary>Clicar no resultado já escolhido não muda a seleção, então o clique leva à linha de novo.</summary>
    private void OnResultTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SearchReplaceViewModel vm && (e.Source as Control)?.DataContext is SearchResultRow row)
        {
            vm.OpenResult(row);
        }
    }
}
