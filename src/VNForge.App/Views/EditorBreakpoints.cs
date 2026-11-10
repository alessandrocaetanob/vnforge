namespace VNForge.App.Views;

/// <summary>
/// Os pontos de quebra do Editor compacto (#314), em largura útil da janela (<c>TopLevel.ClientSize</c>). Ficam fora do controle para o teste conferir sem abrir janela.
/// A 1440 px o Editor mostra as três colunas do artboard; abaixo de 1400 px (um monitor de 1366×768) a lista de cenas vira um botão com
/// flyout, os botões secundários viram ícones e o inspetor, com 360 px, se divide em abas.
/// </summary>
public enum ScenesPlacement
{
    /// <summary>A lista abre num flyout do botão Cenas do cabeçalho; a grade não tem a coluna.</summary>
    Flyout,

    /// <summary>A lista ocupa a coluna da esquerda.</summary>
    Column,

    /// <summary>A coluna está recolhida numa faixa estreita com o nome da cena aberta.</summary>
    Strip,
}

public static class EditorBreakpoints
{
    /// <summary>Abaixo desta largura de janela a lista de cenas sai da grade e abre num flyout.</summary>
    public const double SceneFlyoutBelow = 1400;

    /// <summary>Abaixo desta largura de janela o inspetor vira abas e fica com <see cref="InspectorTabsWidth"/>.</summary>
    public const double InspectorTabsBelow = 1400;

    public const double InspectorTabsWidth = 360;

    /// <summary>
    /// Abaixo desta altura de janela o Editor usa o cabeçalho compacto (80 px) em vez do completo do quadro do #546 (161 px): com o completo a caixa do jogo, que fica em y=729 a 1440 px
    /// de largura e mede 75 px, só aparece inteira sem rolar a partir de cerca de 915 px de janela (a 860 faltam 55 px). Com o compacto ela começa em y=648 e cabe a partir de cerca de 835 px.
    /// </summary>
    public const double FullHeaderMinHeight = 920;

    public static bool UsesFullHeader(double windowHeight) => windowHeight >= FullHeaderMinHeight;

    /// <summary>Abaixo desta largura de janela a margem lateral do Editor cai de <see cref="WideMargin"/> para <see cref="NarrowMargin"/>, para a barra de filtros e ações caber.</summary>
    public const double NarrowMarginBelow = 1100;

    public const double WideMargin = 32;

    public const double NarrowMargin = 24;

    /// <summary>
    /// O inspetor inteiro (sem abas) tem 384 px no quadro (Vnf.InspectorWidth) e não passa disso: o original japonês quebra em duas linhas e o cartão da passada
    /// fica na altura do quadro. A caixa do jogo encolhe em proporção quando as colunas do perfil não cabem, nunca rola (#392).
    /// </summary>
    public const double InspectorMinWidth = 320;

    public const double InspectorMaxWidth = 384;

    public const double InspectorStars = 1.9;

    /// <summary>A lista de cenas tem 220 px no quadro; o que passa disso vai para a grade de linhas.</summary>
    public const double ScenesMaxWidth = 220;

    /// <summary>Largura da faixa em que a coluna das cenas se recolhe (#393): o botão de abrir e o nome da cena aberta, de pé.</summary>
    public const double ScenesStripWidth = 44;

    public static bool UsesSceneFlyout(double windowWidth) => windowWidth < SceneFlyoutBelow;

    /// <summary>
    /// Como a lista de cenas aparece: no flyout do cabeçalho abaixo de 1400 px (recolher não muda nada ali) e, de 1400 px para cima, na coluna inteira
    /// ou, recolhida, na faixa estreita.
    /// </summary>
    public static ScenesPlacement PlaceScenes(double windowWidth, bool collapsed) =>
        UsesSceneFlyout(windowWidth) ? ScenesPlacement.Flyout : collapsed ? ScenesPlacement.Strip : ScenesPlacement.Column;

    public static bool UsesInspectorTabs(double windowWidth) => windowWidth < InspectorTabsBelow;
}
