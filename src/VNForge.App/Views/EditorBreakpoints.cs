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
    /// A grade de linhas leva o que sobra (v4): o inspetor inteiro parte de 340 px e só passa disso em janelas largas, até 600 px. A 1440 px de janela ele tem 340 px e a caixa
    /// do jogo encolhe em proporção (nunca rola); com 600 px ela mostra as 73 colunas do perfil sem encolher (#392).
    /// </summary>
    public const double InspectorMinWidth = 340;

    public const double InspectorMaxWidth = 600;

    public const double InspectorStars = 1.0;

    /// <summary>A lista de cenas leva o nome da cena e o id pequeno; 190 px bastam e deixam a largura para o texto da grade.</summary>
    public const double ScenesMaxWidth = 190;

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
