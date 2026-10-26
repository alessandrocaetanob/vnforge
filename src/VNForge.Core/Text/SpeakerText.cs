using System.Text;
using VNForge.Core.Glossary;

namespace VNForge.Core.Text;

/// <summary>
/// Como o falante de uma fala entra no texto que o build grava: qual nome vai para o jogo e quais delimitadores (<c>「」</c>) envolvem a fala.
/// É a única composição: a Conferência (<see cref="Qa.SpeakerEncodingRule"/>), o build (<see cref="Projects.ProjectBuilder"/>) e a engine usam estas regras,
/// então o que passa na Conferência compila.
/// </summary>
public static class SpeakerText
{
    /// <summary>Delimitador japonês e a aspa curva que o toma quando a estratégia de codificação não grava o original: <c>Inui“Bom dia!”</c>.</summary>
    private static readonly Dictionary<string, string> CurlyQuotes = new(StringComparer.Ordinal)
    {
        ["「"] = "“",
        ["」"] = "”",
        ["『"] = "‘",
        ["』"] = "’",
    };

    /// <summary>
    /// O nome que vai para o jogo no lugar do <paramref name="speaker"/> extraído. Vale a tradução de <c>characters.json</c>; sem entrada, o nome original,
    /// e só quando o <paramref name="encoder"/> não o grava é que a largura é normalizada (NFKC: <c>？？？</c> vira <c>???</c>). O nome que ainda sobra sem
    /// representação (japonês sem cadastro) volta como erro, com o que fazer.
    /// </summary>
    /// <param name="speaker">O <see cref="TextUnit.Speaker"/> da unidade; vazio devolve nome vazio e nenhum erro (a engine usa o nome do próprio script).</param>
    public static SpeakerName Resolve(string? speaker, CharacterBook characters, ITextEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(encoder);

        if (string.IsNullOrWhiteSpace(speaker))
        {
            return new SpeakerName(string.Empty, null);
        }

        if (characters.Find(speaker) is { } entry && !string.IsNullOrWhiteSpace(entry.Target))
        {
            var target = entry.Target.Trim();
            return WithWidthFallback(target, encoder) is { } written
                ? new SpeakerName(written, null)
                : new SpeakerName(
                    target,
                    $"A tradução do nome do personagem {speaker.Trim()} ('{target}') tem {FirstRefused(target, encoder)}, que a codificação do jogo ({encoder.Id}) não grava. Troque a tradução do nome no Glossário > Personagens.");
        }

        return WithWidthFallback(speaker, encoder) is { } name
            ? new SpeakerName(name, null)
            : new SpeakerName(
                speaker,
                $"O nome do personagem {speaker.Trim()} não existe na codificação do jogo ({encoder.Id}). Cadastre a tradução do nome no Glossário > Personagens.");
    }

    /// <summary>
    /// O delimitador que a fala leva no jogo: o próprio <paramref name="delimiter"/> quando o <paramref name="encoder"/> o grava, a aspa curva correspondente
    /// quando não (e ela é gravável). Sem substituto gravável, devolve o original e quem codifica recusa a linha.
    /// </summary>
    public static string Delimiter(string delimiter, ITextEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(delimiter);
        ArgumentNullException.ThrowIfNull(encoder);

        if (delimiter.Length == 0 || encoder.FindUnencodable(delimiter).Count == 0)
        {
            return delimiter;
        }

        return CurlyQuotes.TryGetValue(delimiter, out var curly) && encoder.FindUnencodable(curly).Count == 0 ? curly : delimiter;
    }

    private static string FirstRefused(string text, ITextEncoder encoder) => EncodingMessages.Character(text, encoder.FindUnencodable(text)[0]);

    /// <summary>O <paramref name="name"/> como está, ou normalizado em largura (NFKC) quando só assim o <paramref name="encoder"/> o grava; nulo se nem assim.</summary>
    private static string? WithWidthFallback(string name, ITextEncoder encoder)
    {
        if (encoder.FindUnencodable(name).Count == 0)
        {
            return name;
        }

        var normalized = name.Normalize(NormalizationForm.FormKC);
        return encoder.FindUnencodable(normalized).Count == 0 ? normalized : null;
    }
}

/// <summary>O nome de falante que o build grava, ou o motivo de não poder gravá-lo.</summary>
/// <param name="Name">O nome a gravar; vazio quando a unidade não tem falante (a engine usa o do script). Com <paramref name="Error"/>, o nome de partida.</param>
/// <param name="Error">Mensagem em português com o que fazer; nulo quando o nome é gravável.</param>
public sealed record SpeakerName(string Name, string? Error)
{
    /// <summary>Verdadeiro quando <see cref="Name"/> pode ser gravado.</summary>
    public bool IsWritable => Error is null;
}
