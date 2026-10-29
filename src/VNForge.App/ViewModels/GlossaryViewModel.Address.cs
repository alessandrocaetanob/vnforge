using VNForge.Core.Glossary;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>A tabela "Quem chama quem" (#470): as linhas e a IA ficam no <see cref="AddressTermsViewModel"/>; a página só grava o arquivo junto com os outros.</summary>
public sealed partial class GlossaryViewModel
{
    /// <summary>Como cada personagem chama os outros (<c>address-terms.json</c>).</summary>
    public AddressTermsViewModel AddressTerms { get; }

    /// <summary>
    /// Os tratamentos guardam o nome original do personagem: quem renomeou uma linha de Personagens leva os pares que a citam para o nome novo (#514).
    /// Roda ao salvar, não a cada tecla, e deixa de fora o nome que outra linha também usa: dois personagens com o mesmo nome fundiriam os pares sem a pessoa ter pedido.
    /// </summary>
    private void FollowCharacterRenames()
    {
        var taken = Characters.Select(c => c.Source.Trim()).Where(n => n.Length > 0).GroupBy(n => n, CharacterBook.NameComparer).ToDictionary(g => g.Key, g => g.Count(), CharacterBook.NameComparer);
        var renames = new Dictionary<string, string>(CharacterBook.NameComparer);
        foreach (var row in Characters)
        {
            var name = row.Source.Trim();
            if (name.Length == 0 || taken[name] != 1)
            {
                continue;
            }

            if (row.CommittedSource.Length > 0 && !CharacterBook.NameComparer.Equals(row.CommittedSource, name))
            {
                renames[row.CommittedSource] = name;
            }

            row.CommittedSource = name;
        }

        if (renames.Count > 0 && AddressTerms.ApplyRenames(renames))
        {
            addressDirty = true;
        }
    }

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
