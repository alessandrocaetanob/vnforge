using VNForge.Core.Review;

namespace VNForge.App.ViewModels;

public sealed partial class SettingsViewModel
{
    /// <summary>Acrescenta a troca repetida ao guia de estilo (<c>style.md</c>); o salvamento automático dos Ajustes a grava. A regra que já está no texto não se repete.</summary>
    public void AddStyleRule(RepeatedCorrection correction)
    {
        ArgumentNullException.ThrowIfNull(correction);
        StyleGuide = correction.AddTo(StyleGuide);
    }
}
