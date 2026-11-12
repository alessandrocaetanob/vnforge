namespace VNForge.Core.Vndb;

/// <summary>
/// O crédito que a licença dos dados do VNDB pede: o banco está sob a Open Database License e o conteúdo individual sob a Database Contents License
/// (vndb.org/d17, seção 4). Toda tela, relatório ou exportação que mostre dado do VNDB (título, capa, sinopse, tag, personagem, lista) usa estes textos.
/// </summary>
public static class VndbCredit
{
    /// <summary>A página do VNDB que descreve a licença dos dados.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded", Justification="Endereço fixo da página de licença dos dados do VNDB, citado na atribuição.")]
    public const string LicenseUrl = "https://vndb.org/d17#4";

    /// <summary>Linha curta, para rodapé de tela.</summary>
    public const string ShortText = "Dados do VNDB (vndb.org), sob a Open Database License.";

    /// <summary>Crédito completo, para a tela Sobre, o catálogo exportado e os relatórios.</summary>
    public const string Text =
        "Dados do VNDB (vndb.org), disponíveis sob a Open Database License; o conteúdo individual do banco está sob a Database Contents License. "
        + "Detalhes em " + LicenseUrl + ".";
}
