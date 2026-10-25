using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>
/// A frase de teste de acentos do Novo projeto, montada para a caixa de texto do jogo do jeito que a estratégia a grava: sem acentos, ela passa
/// pelo mesmo <see cref="CharacterFolder"/> do build (<c>fold</c>); com acentos, sai como foi escrita.
/// </summary>
public static class AccentPreview
{
    public const string Sentence = "Você não vai acreditar no que aconteceu hoje à tarde!";

    private const int Columns = 28;

    private const int MaxLines = 3;

    private static readonly string[] Lines = ["Você não vai acreditar no", "que aconteceu hoje à tarde!"];

    /// <summary>A caixa de <paramref name="strategy"/> (<c>fold</c>, <c>remap</c> ou <c>cp1252</c>) com a frase de teste em duas linhas.</summary>
    public static TextboxLayout Layout(string strategy)
    {
        var shown = Lines.Select(line => strategy == "fold" ? CharacterFolder.Fold(line).Text : line).ToArray();
        var box = new TextboxBox([.. shown.Select(text => new TextboxLine(text, text.Length, false, [new LineSegment(text, 0, text.Length, false)]))]);
        return new TextboxLayout(null, Columns, MaxLines, [box]);
    }
}
