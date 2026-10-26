namespace VNForge.Core.Glossary;

/// <summary>Regras sobre o que serve como nome de falante.</summary>
public static class SpeakerNames
{
    /// <summary>
    /// Verdadeiro quando o falante tem texto mas nenhuma letra nem dígito, como <c>？？？</c>, <c>…</c> ou <c>・・・</c>: é o jeito do jogo
    /// de dizer "alguém que ainda não se apresentou", e vale para várias pessoas. Não é personagem e não ganha entrada no glossário.
    /// Nulo, vazio ou só espaços devolvem falso (é falta de falante, não um falante de sinais).
    /// </summary>
    public static bool IsSignsOnly(string? speaker)
    {
        var name = speaker?.Trim();
        return !string.IsNullOrEmpty(name) && !name.Any(char.IsLetterOrDigit);
    }
}
