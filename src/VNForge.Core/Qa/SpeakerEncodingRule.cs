using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Falante que o build não consegue gravar na estratégia de codificação do projeto: nome japonês sem cadastro em <c>characters.json</c> (ou com tradução
/// que a estratégia não grava). Só linhas que o build grava (<see cref="TextUnit.NeedsRewrite"/>) entram, com o falante que vale (<see cref="TextUnit.EffectiveSpeaker"/>: o escolhido, quando há). É erro, e "marcar como ok" não o silencia,
/// porque o build recusaria a linha. Usa o mesmo <see cref="SpeakerText.Resolve"/> do build: o que passa aqui compila. Sem encoder no contexto, não confere nada.
/// </summary>
public sealed class SpeakerEncodingRule : IQaRule
{
    public const string RuleId = "falante-nao-codificavel";

    /// <inheritdoc />
    public string Id => RuleId;

    /// <inheritdoc />
    public string Description => "Nome de falante que a estratégia de codificação do projeto não grava.";

    /// <inheritdoc />
    public IEnumerable<QaIssue> Check(QaContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CheckSpeakers(context);
    }

    private static IEnumerable<QaIssue> CheckSpeakers(QaContext context)
    {
        if (context.Encoder is not { } encoder)
        {
            yield break;
        }

        var book = new CharacterBook(context.Characters);
        foreach (var (key, units) in context.Texts)
        {
            foreach (var unit in units)
            {
                if (!unit.NeedsRewrite || string.IsNullOrWhiteSpace(unit.EffectiveSpeaker))
                {
                    continue;
                }

                if (SpeakerText.Resolve(unit.EffectiveSpeaker, book, encoder).Error is { } error)
                {
                    yield return new QaIssue(RuleId, QaSeverity.Error, unit.Id, key, error);
                }
            }
        }
    }
}
