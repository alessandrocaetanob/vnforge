using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>
/// "Detalhes técnicos" (v3.css, <c>.tech</c>): um <see cref="Expander"/> fechado por padrão onde ficam o que a pessoa que traduz não precisa ver
/// (motor, pasta, acentos, nome do modelo, registro completo). O título vai em <c>Header</c> e o conteúdo é livre.
/// </summary>
public sealed class TechnicalDetails : Expander
{
    public TechnicalDetails() => Header = "Detalhes técnicos";

    protected override Type StyleKeyOverride => typeof(TechnicalDetails);
}