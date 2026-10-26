namespace VNForge.App.Views;

/// <summary>
/// Os pontos de quebra do Editor compacto (#314), em largura útil da janela (<c>TopLevel.ClientSize</c>). Ficam fora do controle para o teste conferir sem abrir janela.
/// A 1440 px o Editor mostra as três colunas do artboard; abaixo de 1400 px (um monitor de 1366×768) a lista de cenas vira um botão com
/// flyout, os botões secundários viram ícones e o inspetor, com 360 px, se divide em abas.
/// </summary>
public static class EditorBreakpoints
{
    /// <summary>Abaixo desta largura de janela a lista de cenas sai da grade e abre num flyout.</summary>
    public const double SceneFlyoutBelow = 1400;

    /// <summary>Abaixo desta largura de janela o inspetor vira abas e fica com <see cref="InspectorTabsWidth"/>.</summary>
    public const double InspectorTabsBelow = 1400;

    public const double InspectorTabsWidth = 360;

    public static bool UsesSceneFlyout(double windowWidth) => windowWidth < SceneFlyoutBelow;

    public static bool UsesInspectorTabs(double windowWidth) => windowWidth < InspectorTabsBelow;
}
