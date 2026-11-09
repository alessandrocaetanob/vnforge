using VNForge.Core.Glossary;

namespace VNForge.Core.Images;

/// <summary>Lê e grava <c>images.json</c>: o envelope <c>{ "version": 1, "entries": [...] }</c> com um <see cref="ImageTextEntry"/> por linha, na ordem recebida.</summary>
public static class ImageInventorySerializer
{
    /// <exception cref="GlossaryFileException">Pacote, caminho ou hash vazio, tamanho inválido, ou o mesmo lugar duas vezes.</exception>
    public static string Serialize(IReadOnlyList<ImageTextEntry> entries, string filePath = "(memória)") =>
        EntryFile.Serialize(entries, filePath, Check, e => $"{e.Archive}/{e.Path}");

    /// <exception cref="GlossaryFileException">JSON malformado, envelope fora do formato, campo desconhecido ou ausente, versão desconhecida, ou entrada inválida.</exception>
    public static IReadOnlyList<ImageTextEntry> Parse(string json, string filePath) =>
        EntryFile.Parse<ImageTextEntry>(json, filePath, Check);

    private static void Check(IReadOnlyList<ImageTextEntry> entries, string filePath)
    {
        var seen = new HashSet<(string, string)>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i] ?? throw new GlossaryFileException(filePath, $"$.entries[{i}]: a entrada é nula.");
            if (string.IsNullOrWhiteSpace(entry.Archive))
            {
                throw new GlossaryFileException(filePath, $"$.entries[{i}].archive: o pacote não pode ser vazio.");
            }

            if (string.IsNullOrWhiteSpace(entry.Path))
            {
                throw new GlossaryFileException(filePath, $"$.entries[{i}].path: o caminho da imagem não pode ser vazio.");
            }

            if (string.IsNullOrWhiteSpace(entry.Hash))
            {
                throw new GlossaryFileException(filePath, $"$.entries[{i}].hash: o hash não pode ser vazio.");
            }

            if (entry.Width <= 0 || entry.Height <= 0)
            {
                throw new GlossaryFileException(filePath, $"$.entries[{i}]: largura e altura precisam ser maiores que zero.");
            }

            if (entry.Text is null)
            {
                throw new GlossaryFileException(filePath, $"$.entries[{i}].text: o texto não pode ser nulo (use \"\" para imagem sem texto).");
            }

            if (!seen.Add((entry.Archive, entry.Path)))
            {
                throw new GlossaryFileException(filePath, $"'{entry.Archive}/{entry.Path}' aparece mais de uma vez no arquivo.");
            }
        }
    }
}
