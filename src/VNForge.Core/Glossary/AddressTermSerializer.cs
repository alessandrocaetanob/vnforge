namespace VNForge.Core.Glossary;

/// <summary>Lê e grava <c>address-terms.json</c>: o envelope <c>{ "version": 1, "entries": [...] }</c> com um <see cref="AddressTerm"/> por linha, na ordem recebida.</summary>
public static class AddressTermSerializer
{
    /// <exception cref="GlossaryFileException">Quem fala ou com quem fala vazio, par repetido, ou texto com UTF-16 inválido.</exception>
    public static string Serialize(IReadOnlyList<AddressTerm> terms, string filePath = "(memória)") =>
        EntryFile.Serialize(terms, filePath, Check, t => $"{t.From} → {t.To}");

    /// <exception cref="GlossaryFileException">JSON malformado, envelope fora do formato, campo desconhecido ou ausente, versão desconhecida, quem fala ou com quem fala vazio, par repetido.</exception>
    public static IReadOnlyList<AddressTerm> Parse(string json, string filePath) =>
        EntryFile.Parse<AddressTerm>(json, filePath, Check);

    // O par é a chave, comparado como o CharacterBook compara nomes (espaços nas pontas e largura): dois pares que só diferem por isso colidiriam em silêncio.
    private static void Check(IReadOnlyList<AddressTerm> terms, string filePath)
    {
        var seen = new HashSet<(string, string)>();
        for (var i = 0; i < terms.Count; i++)
        {
            var term = terms[i] ?? throw new GlossaryFileException(filePath, $"$.entries[{i}]: a entrada é nula.");
            if (string.IsNullOrWhiteSpace(term.From))
            {
                throw new GlossaryFileException(filePath, $"$.entries[{i}].from: o nome de quem fala não pode ser vazio.");
            }

            if (string.IsNullOrWhiteSpace(term.To))
            {
                throw new GlossaryFileException(filePath, $"$.entries[{i}].to: o nome de com quem fala não pode ser vazio.");
            }

            if (!seen.Add(AddressBook.PairKey(term.From, term.To)))
            {
                throw new GlossaryFileException(filePath, $"O par '{term.From.Trim()} → {term.To.Trim()}' aparece mais de uma vez no arquivo.");
            }
        }
    }
}
