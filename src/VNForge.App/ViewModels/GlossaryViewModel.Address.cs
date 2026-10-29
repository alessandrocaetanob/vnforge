namespace VNForge.App.ViewModels;

/// <summary>A tabela "Quem chama quem" (#470): as linhas e a IA ficam no <see cref="AddressTermsViewModel"/>; a página só grava o arquivo junto com os outros.</summary>
public sealed partial class GlossaryViewModel
{
    /// <summary>Como cada personagem chama os outros (<c>address-terms.json</c>).</summary>
    public AddressTermsViewModel AddressTerms { get; }

    private void MarkAddressDirty()
    {
        if (!loading)
        {
            addressDirty = true;
            Touch();
        }
    }
}
