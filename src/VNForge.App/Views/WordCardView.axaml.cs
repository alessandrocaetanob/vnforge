using Avalonia.Controls;

namespace VNForge.App.Views;

/// <summary>O cartão do dicionário offline do inspetor (#538). Só desenha; o estado e os comandos são do <see cref="ViewModels.LineDetailViewModel"/>.</summary>
public sealed partial class WordCardView : UserControl
{
    public WordCardView() => InitializeComponent();
}
