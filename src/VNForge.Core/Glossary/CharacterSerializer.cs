namespace VNForge.Core.Glossary;

/// <summary>Lê e grava <c>characters.json</c>: o envelope <c>{ "version": 1, "entries": [...] }</c> com um <see cref="CharacterEntry"/> por linha, na ordem recebida.</summary>
public static class CharacterSerializer
{
    /// <exception cref="GlossaryFileException">Nome original vazio ou repetido, ou texto com UTF-16 inválido.</exception>
    public static string Serialize(IReadOnlyList<CharacterEntry> characters, string filePath = "(memória)") =>
        EntryFile.Serialize(characters, filePath);

    /// <exception cref="GlossaryFileException">JSON malformado, envelope fora do formato, campo desconhecido ou ausente, gênero inválido, versão desconhecida, nome original vazio ou repetido.</exception>
    public static IReadOnlyList<CharacterEntry> Parse(string json, string filePath) =>
        EntryFile.Parse<CharacterEntry>(json, filePath);
}
