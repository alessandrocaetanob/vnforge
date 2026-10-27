using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Caractere que a estratégia de codificação atual não grava (<c>encoding.strategy</c>). O build recusa a linha, então é erro e "marcar como ok" não
/// o silencia. Caractere que a estratégia troca por um substituto (<c>fold</c>) não entra: isso é decisão da estratégia, listada pelo relatório de codificação.
/// O caractere que um ajuste de formatação cria (<see cref="FormattingConflictRule"/>) também não entra: a linha sai uma vez só, como ajuste.
/// Sem encoder no contexto, a regra não confere nada.
/// </summary>
/// <remarks>
/// O corretor (<see cref="UnwritableCharacters"/>) troca o caractere por um substituto só ASCII da tabela (aspas curvas, <c>…</c>, <c>♪</c>, <c>。 ～ （</c>,
/// letra acentuada fora da página), só quando a estratégia grava o substituto; o resto fica para a pessoa. Sem encoder ele não sabe o que a estratégia
/// grava e deixa o texto como está.
/// </remarks>
public sealed class UnencodableCharacterRule : UnitQaRule, IQaFixer
{
    public const string RuleId = "caractere-nao-codificavel";

    public override string Id => RuleId;

    public override string Description => "Caractere da tradução que a estratégia de codificação do projeto não grava.";

    public override QaSeverity Severity => QaSeverity.Error;

    /// <summary>Sem o encoder do projeto não há o que corrigir: devolve o texto como veio. A correção de verdade é <see cref="Fix(string, QaContext)"/>.</summary>
    string IQaFixer.Fix(string translation) => translation;

    /// <summary>A <paramref name="translation"/> com os caracteres que o encoder do <paramref name="context"/> não grava trocados pelo substituto da tabela.</summary>
    public string Fix(string translation, QaContext context)
    {
        ArgumentNullException.ThrowIfNull(translation);
        ArgumentNullException.ThrowIfNull(context);
        return context.Encoder is { } encoder ? UnwritableCharacters.Fix(translation, encoder) : translation;
    }

    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        if (context.Encoder is not { } encoder || BuiltText.For(unit, context.Formatting) is not { } built)
        {
            yield break;
        }

        var fromSettings = FormattingConflictRule.Affecting(unit, context).SelectMany(a => a.Created).Select(r => r.Value).ToHashSet();
        foreach (var text in BuiltText.PlainSegments(built))
        {
            foreach (var index in encoder.FindUnencodable(text))
            {
                var length = index + 1 < text.Length && char.IsSurrogatePair(text[index], text[index + 1]) ? 2 : 1;
                var codePoint = length == 2 ? char.ConvertToUtf32(text[index], text[index + 1]) : text[index];
                if (fromSettings.Contains(codePoint))
                {
                    continue;
                }

                yield return $"{EncodingMessages.Character(text, index)} em «{EncodingMessages.Excerpt(text, index)}» não existe na estratégia '{encoder.Id}'.{Explanation(encoder, codePoint)}{Suggestion(encoder, codePoint)}";
            }
        }
    }

    /// <summary>" Na cp1252 ele vira o byte 0x93, ..." para o caractere que a página grava mas o jogo lê errado; vazio para o resto.</summary>
    private static string Explanation(ITextEncoder encoder, int codePoint) =>
        encoder is IExplainingTextEncoder explaining && explaining.ExplainRefusal(codePoint) is { } text ? $" {text}" : string.Empty;

    /// <summary>" Nome: til japonês. Use '~'." com o nome (quando a tabela de pontuação japonesa o tem) e o substituto que o corretor grava; vazio para o resto.</summary>
    private static string Suggestion(ITextEncoder encoder, int codePoint)
    {
        var name = JapanesePunctuation.NameOf(codePoint) is { } found ? $" Nome: {found}." : string.Empty;
        var use = UnwritableCharacters.SubstituteFor(codePoint, encoder) switch
        {
            null => string.Empty,
            " " => " Use um espaço.",
            { } substitute => $" Use '{substitute}'.",
        };
        return name + use;
    }
}
