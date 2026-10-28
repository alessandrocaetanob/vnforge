using System.Text;
using VNForge.Core.Glossary;

namespace VNForge.Core.Text;

/// <summary>
/// Como o falante de uma fala entra no texto que o build grava: qual nome vai para o jogo. Os delimitadores (<c>「」</c>) não passam por aqui: a engine os grava em Shift-JIS, como no original, seja qual for a estratégia.
/// É a única composição: a Conferência (<see cref="Qa.SpeakerEncodingRule"/>), o build (<see cref="Projects.ProjectBuilder"/>) e a engine usam estas regras,
/// então o que passa na Conferência compila.
/// </summary>
public static class SpeakerText
{
    /// <summary>
    /// O nome que vai para o jogo no lugar do <paramref name="speaker"/> extraído. Vale a tradução de <c>characters.json</c> (se a estratégia não a grava mas
    /// grava o nome original, como o Shift-JIS, fica o original); sem entrada, o nome original,
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
            if (WithWidthFallback(target, encoder) is { } written)
            {
                return new SpeakerName(written, null);
            }

            // O Shift-JIS grava o nome japonês mesmo quando não grava o acento da tradução: o build segue como antes, com o nome do original.
            return encoder.FindUnencodable(speaker).Count == 0
                ? new SpeakerName(speaker, null)
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
    /// O nome que o build entrega à engine para <paramref name="unit"/>: o <see cref="TextUnit.ChosenSpeaker"/> quando há, senão o <see cref="TextUnit.Speaker"/> extraído,
    /// cada um por <see cref="Resolve"/>. Nulo quando o build deixa a unidade como está: narração escolhida (vazio) ou sem falante.
    /// </summary>
    public static SpeakerName? ResolveUnit(TextUnit unit, CharacterBook characters, ITextEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(unit);

        if (unit.ChosenSpeaker is { Length: > 0 } chosen)
        {
            return Resolve(chosen, characters, encoder);
        }

        return unit.ChosenSpeaker is null && !string.IsNullOrWhiteSpace(unit.Speaker) ? Resolve(unit.Speaker, characters, encoder) : null;
    }

    /// <summary>
    /// O nome que o jogo mostra na caixa de nome da linha, o da placa da prévia: o que <see cref="ResolveUnit"/> dá ao build (a tradução do Glossário, o falante escolhido na linha),
    /// sem recusar o que a estratégia não grava (a Conferência é quem avisa). Nulo na narração. Sem <paramref name="encoder"/> (estratégia desconhecida), vale a tradução do cadastro ou o nome como está.
    /// </summary>
    public static string? WrittenName(TextUnit unit, CharacterBook characters, ITextEncoder? encoder)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(characters);

        string? name;
        if (encoder is not null)
        {
            name = ResolveUnit(unit, characters, encoder)?.Name ?? unit.EffectiveSpeaker;
        }
        else
        {
            var entry = string.IsNullOrWhiteSpace(unit.EffectiveSpeaker) ? null : characters.Find(unit.EffectiveSpeaker);
            name = entry is not null && !string.IsNullOrWhiteSpace(entry.Target) ? entry.Target.Trim() : unit.EffectiveSpeaker;
        }

        return string.IsNullOrEmpty(name) ? null : name;
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
