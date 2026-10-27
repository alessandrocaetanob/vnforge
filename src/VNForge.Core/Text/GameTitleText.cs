using System.Text;

namespace VNForge.Core.Text;

/// <summary>
/// Como o título traduzido do jogo entra no arquivo de configuração: o texto que vai para o jogo, ou o motivo de a estratégia de codificação
/// do projeto não poder gravá-lo. É a única composição: a Conferência (<see cref="Qa.GameTitleEncodingRule"/>) e o build
/// (<see cref="Projects.ProjectBuilder"/>) usam estas regras, então o que passa na Conferência compila.
/// </summary>
public static class GameTitleText
{
    /// <summary>Id e chave de script do título nos relatórios de QA: ele não é uma linha de <c>text/*.json</c>, mas aparece na Conferência como uma.</summary>
    public const string UnitId = "titulo-do-jogo";

    /// <summary>A tradução sem espaços nas pontas, ou nulo quando não há (nula ou em branco). É o que entra na impressão digital do build.</summary>
    public static string? Normalize(string? translation) => string.IsNullOrWhiteSpace(translation) ? null : translation.Trim();

    /// <summary>
    /// O título que vai para o jogo. Sem tradução (nula ou em branco) não há nada a gravar: o jogo fica com o título original. Com tradução, vale o
    /// texto sem espaços nas pontas e, só quando o <paramref name="encoder"/> não o grava, o texto com a largura normalizada (NFKC: <c>ＡＢＣ</c> vira
    /// <c>ABC</c>). O que ainda sobra sem representação volta como erro.
    /// </summary>
    public static GameTitleName Resolve(string? translation, ITextEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);

        if (string.IsNullOrWhiteSpace(translation))
        {
            return new GameTitleName(null, null);
        }

        var title = translation.Trim();
        if (encoder.FindUnencodable(title).Count == 0)
        {
            return new GameTitleName(title, null);
        }

        var normalized = title.Normalize(NormalizationForm.FormKC);
        if (encoder.FindUnencodable(normalized).Count == 0)
        {
            return new GameTitleName(normalized, null);
        }

        var refused = EncodingMessages.Character(normalized, encoder.FindUnencodable(normalized)[0]);
        return new GameTitleName(
            title,
            $"A tradução do título do jogo ('{title}') tem {refused}, que a codificação do jogo ({encoder.Id}) não grava. Troque o título em Ajustes.");
    }
}

/// <summary>O título que o build grava no jogo, ou o motivo de não poder gravá-lo.</summary>
/// <param name="Text">O título a gravar; nulo quando não há tradução (o jogo fica com o original). Com <paramref name="Error"/>, o texto de partida.</param>
/// <param name="Error">Mensagem em português com o que fazer; nulo quando o título é gravável.</param>
public sealed record GameTitleName(string? Text, string? Error)
{
    /// <summary>Verdadeiro quando não há erro: ou não há o que gravar, ou <see cref="Text"/> pode ser gravado.</summary>
    public bool IsWritable => Error is null;
}
