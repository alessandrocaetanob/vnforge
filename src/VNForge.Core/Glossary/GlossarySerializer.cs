namespace VNForge.Core.Glossary;

/// <summary>Lê e grava <c>glossary.json</c>: o envelope <c>{ "version": 1, "entries": [...] }</c> com um <see cref="GlossaryTerm"/> por linha, na ordem recebida.</summary>
public static class GlossarySerializer
{
    /// <exception cref="GlossaryFileException">Termo original vazio ou repetido, ou texto com UTF-16 inválido.</exception>
    public static string Serialize(IReadOnlyList<GlossaryTerm> terms, string filePath = "(memória)") =>
        EntryFile.Serialize(terms, filePath);

    /// <exception cref="GlossaryFileException">JSON malformado, envelope fora do formato, campo desconhecido ou ausente, versão desconhecida, termo original vazio ou repetido.</exception>
    public static IReadOnlyList<GlossaryTerm> Parse(string json, string filePath) =>
        EntryFile.Parse<GlossaryTerm>(json, filePath);
}
