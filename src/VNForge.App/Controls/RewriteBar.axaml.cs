using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>
/// Os chips de "Reescrever com um toque" e as duas versões que a IA devolve, medidas contra a caixa do jogo. O DataContext é o
/// <see cref="ViewModels.LineDetailViewModel"/> da linha: o inspetor do Editor e o modo revisão usam o mesmo controle.
/// </summary>
public sealed partial class RewriteBar : UserControl
{
    public RewriteBar() => InitializeComponent();
}
