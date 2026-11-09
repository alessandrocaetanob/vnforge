using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace VNForge.App.Views;

/// <summary>O cabeçalho do jogo aberto (volta, capa, nome, engine, andamento, próximo passo), as abas Ficha, Tradução e Jogar e a segunda linha da Tradução.</summary>
public sealed partial class GameHeaderView : UserControl
{
    /// <summary>Abaixo desta altura de janela (um monitor de 1366×768) a capa e a linha da volta somem e o cabeçalho cabe numa linha só.</summary>
    public const double CompactBelow = 820;

    public static readonly StyledProperty<bool> IsCompactProperty = AvaloniaProperty.Register<GameHeaderView, bool>(nameof(IsCompact));

    /// <summary>A largura da capa, em pixels.</summary>
    public const double CoverWidth = 56;

    /// <summary>Do andamento (0 a 100) para a largura do preenchimento da barra no pé da capa.</summary>
    public static readonly IValueConverter CoverBarWidth = new FuncValueConverter<double, double>(percent => CoverWidth * Math.Clamp(percent, 0, 100) / 100);

    public GameHeaderView() => InitializeComponent();

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }
}
