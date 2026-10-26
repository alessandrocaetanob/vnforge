using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Caractere que a estratégia de codificação atual não grava (<c>encoding.strategy</c>). O build recusa a linha, então é erro e "marcar como ok" não
/// o silencia. Caractere que a estratégia troca por um substituto (<c>fold</c>) não entra: isso é decisão da estratégia, listada pelo relatório de codificação.
/// Sem encoder no contexto, a regra não confere nada.
/// </summary>
/// <remarks>
/// O corretor troca a pontuação japonesa que a IA copia do original (<c>。 ～ （</c>) pela do teclado (<see cref="JapanesePunctuation"/>), só quando a
/// estratégia grava o substituto; o resto fica para a pessoa. Sem encoder ele não sabe o que a estratégia grava e deixa o texto como está.
/// </remarks>
public sealed class UnencodableCharacterRule : UnitQaRule, IQaFixer
{
    public const string RuleId = "caractere-nao-codificavel";

    public override string Id => RuleId;

    public override string Description => "Caractere da tradução que a estratégia de codificação do projeto não grava.";

    public override QaSeverity Severity => QaSeverity.Error;

    /// <summary>Sem o encoder do projeto não há o que corrigir: devolve o texto como veio. A correção de verdade é <see cref="Fix(string, QaContext)"/>.</summary>
    string IQaFixer.Fix(string translation) => translation;

    /// <summary>A <paramref name="translation"/> com a pontuação japonesa que o encoder do <paramref name="context"/> não grava trocada pela do teclado.</summary>
    public string Fix(string translation, QaContext context)
    {
        ArgumentNullException.ThrowIfNull(translation);
        ArgumentNullException.ThrowIfNull(context);
        return context.Encoder is { } encoder ? JapanesePunctuation.FixUnencodable(translation, encoder) : translation;
    }

    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        if (context.Encoder is not { } encoder || BuiltText.For(unit, context.Formatting) is not { } built)
        {
            yield break;
        }

        foreach (var text in BuiltText.PlainSegments(built))
        {
            foreach (var index in encoder.FindUnencodable(text))
            {
                // Substituto solto não forma um Rune: usa o valor do próprio char.
                var length = index + 1 < text.Length && char.IsSurrogatePair(text[index], text[index + 1]) ? 2 : 1;
                var character = text.Substring(index, length);
                var codePoint = length == 2 ? char.ConvertToUtf32(text[index], text[index + 1]) : text[index];
                yield return $"o caractere '{character}' (U+{codePoint:X4}) não existe na estratégia '{encoder.Id}'.{Suggestion(codePoint)}";
            }
        }
    }

    /// <summary>" Nome: til japonês. Use '~'." para o que a tabela cobre; vazio para o resto.</summary>
    private static string Suggestion(int codePoint) =>
        (JapanesePunctuation.NameOf(codePoint), JapanesePunctuation.KeyboardFor(codePoint)) switch
        {
            ({ } name, { } keyboard) => $" Nome: {name}. Use '{keyboard}'.",
            ({ } name, null) => $" Nome: {name}.",
            _ => string.Empty,
        };
}
