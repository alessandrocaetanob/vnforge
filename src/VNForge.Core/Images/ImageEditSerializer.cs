using System.Text.RegularExpressions;
using VNForge.Core.Glossary;

namespace VNForge.Core.Images;

/// <summary>Lê e grava o <c>image-edits.json</c> (#535), no mesmo envelope <c>version</c> e <c>entries</c> do <c>images.json</c>.</summary>
public static partial class ImageEditSerializer
{
    public static string Serialize(IReadOnlyList<ImageEdit> edits, string filePath = "(memória)") =>
        EntryFile.Serialize(edits, filePath, Check, e => $"{e.Archive}/{e.Path}");

    public static IReadOnlyList<ImageEdit> Parse(string json, string filePath) =>
        EntryFile.Parse<ImageEdit>(json, filePath, Check);

    /// <summary>Verdadeiro para <c>#rrggbb</c>, a única forma de cor que o arquivo aceita.</summary>
    public static bool IsColor(string? value) => value is not null && HexColor().IsMatch(value);

    private static void Check(IReadOnlyList<ImageEdit> edits, string filePath)
    {
        var seen = new HashSet<(string, string)>();
        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i] ?? throw new GlossaryFileException(filePath, $"$.entries[{i}]: a edição é nula.");
            var at = $"$.entries[{i}]";
            Require(!string.IsNullOrWhiteSpace(edit.Archive), filePath, $"{at}.archive: o pacote não pode ser vazio.");
            Require(!string.IsNullOrWhiteSpace(edit.Path), filePath, $"{at}.path: o caminho da imagem não pode ser vazio.");
            Require(!string.IsNullOrWhiteSpace(edit.Hash), filePath, $"{at}.hash: o hash não pode ser vazio.");
            Require(edit.Width > 0 && edit.Height > 0, filePath, $"{at}: largura e altura precisam ser maiores que zero.");
            Require(edit.Region is not null, filePath, $"{at}.region: a área não pode ser nula.");
            Require(edit.Region!.Width > 0 && edit.Region.Height > 0, filePath, $"{at}.region: largura e altura da área precisam ser maiores que zero.");
            Require(ImageEraseModes.IsValid(edit.Erase), filePath, $"{at}.erase: use \"{ImageEraseModes.Surrounding}\" ou \"{ImageEraseModes.Solid}\".");
            Require(edit.Erase != ImageEraseModes.Solid || IsColor(edit.EraseColor), filePath, $"{at}.eraseColor: a cor lisa precisa estar no formato #rrggbb.");
            Require(edit.Text is not null, filePath, $"{at}.text: o texto não pode ser nulo (use \"\" para só apagar a área).");
            Require(!string.IsNullOrWhiteSpace(edit.Font), filePath, $"{at}.font: a fonte não pode ser vazia.");
            Require(edit.FontSize is > 0 and <= 1000, filePath, $"{at}.fontSize: o tamanho precisa estar entre 0 e 1000 pixels.");
            Require(IsColor(edit.Color), filePath, $"{at}.color: a cor do texto precisa estar no formato #rrggbb.");
            Require(edit.Outline is null || IsColor(edit.Outline), filePath, $"{at}.outline: a cor do contorno precisa estar no formato #rrggbb.");
            Require(seen.Add((edit.Archive, edit.Path)), filePath, $"'{edit.Archive}/{edit.Path}' aparece mais de uma vez no arquivo.");
        }
    }

    private static void Require(bool condition, string filePath, string message)
    {
        if (!condition)
        {
            throw new GlossaryFileException(filePath, message);
        }
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();
}
