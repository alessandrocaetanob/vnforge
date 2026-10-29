using VNForge.Core.Glossary;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>A tabela "Quem chama quem" (#470): as linhas e a IA ficam no <see cref="AddressTermsViewModel"/>; a página só grava o arquivo junto com os outros.</summary>
public sealed partial class GlossaryViewModel
{
    /// <summary>Como cada personagem chama os outros (<c>address-terms.json</c>).</summary>
    public AddressTermsViewModel AddressTerms { get; }

    private void SaveAddressTerms(ProjectFolder folder, List<AddressTerm> addressTerms)
    {
        if (!addressDirty)
        {
            return;
        }

        // Só linhas em branco e arquivo ausente: não cria um address-terms.json vazio no projeto.
        if (addressTerms.Count > 0 || File.Exists(folder.AddressTermsPath))
        {
            folder.SaveAddressTerms(addressTerms);
        }

        addressDirty = false;
    }

    private void MarkAddressDirty()
    {
        if (!loading)
        {
            addressDirty = true;
            Touch();
        }
    }
}
