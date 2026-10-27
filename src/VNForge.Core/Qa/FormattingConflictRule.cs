using System.Text;
using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Ajuste de formatação que produz um caractere que a estratégia de codificação não grava (Reticências = "Caractere …" na <c>cp1252</c>). O build recusa
/// a linha, então é erro. Vem no lugar de <see cref="UnencodableCharacterRule"/> para o caractere que o ajuste cria: a tradução gravada está certa, quem
/// precisa mudar é o ajuste, e a lista de linhas sai com uma ocorrência por linha em vez de um aviso que manda trocar o que já está trocado.
/// </summary>
/// <remarks>
/// A correção é no <c>formatting.json</c>, não no texto: o <see cref="QaFixer"/> a faz em <see cref="QaFixer.Apply"/>. Por isso <see cref="Fix(string)"/> não
/// muda o texto; ele só deixa o id valer em <c>--rule</c>. Sem encoder no contexto, a regra não confere nada.
/// </remarks>
public sealed class FormattingConflictRule : UnitQaRule, IQaFixer
{
    public const string RuleId = "ajuste-nao-codificavel";

    public override string Id => RuleId;

    public override string Description => "Ajuste de formatação que gera um caractere que a estratégia de codificação do projeto não grava.";

    public override QaSeverity Severity => QaSeverity.Error;

    /// <summary>A correção desta regra é no perfil de formatação (<see cref="QaFixer.Apply"/>); o texto da tradução fica como veio.</summary>
    string IQaFixer.Fix(string translation) => translation;

    protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
    {
        if (context.Encoder is not { } encoder)
        {
            yield break;
        }

        foreach (var (conflict, created) in Affecting(unit, context))
        {
            yield return $"O ajuste {conflict.Setting} ({conflict.CurrentLabel}) gera '{created[0]}', que a estratégia {encoder.Id} não grava.";
        }
    }

    /// <summary>
    /// Os ajustes em conflito que mudam o texto de <paramref name="unit"/> de verdade, cada um com os caracteres que ele cria na linha: o texto
    /// gravado com o perfil atual tem mais daquele caractere do que o gravado com o valor seguro. O caractere que a pessoa digitou e o ajuste não
    /// toca fica de fora, e continua com <see cref="UnencodableCharacterRule"/>.
    /// </summary>
    internal static IReadOnlyList<(FormattingConflict Conflict, IReadOnlyList<Rune> Created)> Affecting(TextUnit unit, QaContext context)
    {
        if (context.Encoder is not { } encoder)
        {
            return [];
        }

        var conflicts = FormattingConflicts.Find(context.Formatting, encoder);
        if (conflicts.Count == 0 || BuiltText.For(unit, context.Formatting) is not { } current)
        {
            return [];
        }

        var safe = BuiltText.For(unit, FormattingConflicts.Resolve(context.Formatting, conflicts)) ?? string.Empty;
        var affecting = new List<(FormattingConflict, IReadOnlyList<Rune>)>();
        foreach (var conflict in conflicts)
        {
            var created = conflict.Characters.EnumerateRunes().Where(r => Count(current, r) > Count(safe, r)).ToList();
            if (created.Count > 0)
            {
                affecting.Add((conflict, created));
            }
        }

        return affecting;
    }

    private static int Count(string text, Rune rune) => text.EnumerateRunes().Count(r => r == rune);
}
