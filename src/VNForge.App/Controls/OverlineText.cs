using Avalonia.Controls;
using Avalonia.Media.TextFormatting;

namespace VNForge.App.Controls;

/// <summary>
/// O rótulo em overline do v3 (<c>.v3-lbl</c> no quadro: <c>text-transform: uppercase</c>, 11/16, semibold, 0,07 em). O <see cref="TextBlock"/> não tem
/// <c>text-transform</c>, então este desenha o <see cref="TextBlock.Text"/> em caixa alta sem mudá-lo: o texto, o binding, os testes e a leitura de tela
/// continuam com o texto normal. O tamanho, a cor e o espaçamento vêm do estilo <c>TextBlock.overline</c>, que este controle usa como o <see cref="TextBlock"/>.
/// </summary>
public class OverlineText : TextBlock
{
    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <summary>O que aparece na tela: o texto em caixa alta.</summary>
    public string? DisplayText => Text?.ToUpperInvariant();

    protected override TextLayout CreateTextLayout(string? text) => base.CreateTextLayout(text?.ToUpperInvariant());
}
