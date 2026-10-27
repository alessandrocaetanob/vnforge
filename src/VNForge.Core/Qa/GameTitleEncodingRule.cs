using VNForge.Core.Text;

namespace VNForge.Core.Qa;

/// <summary>
/// Título do jogo que o build não consegue gravar na estratégia de codificação do projeto. Só confere quando há tradução do título
/// (<see cref="QaContext.GameTitle"/>). É erro, e "marcar como ok" não o silencia, porque o build recusaria o título. Usa o mesmo
/// <see cref="GameTitleText.Resolve"/> do build: o que passa aqui compila. Sem encoder no contexto, não confere nada.
/// </summary>
/// <remarks>A ocorrência usa <see cref="GameTitleText.UnitId"/> como id da linha e como chave do script: o título não é uma linha de <c>text/*.json</c>.</remarks>
public sealed class GameTitleEncodingRule : IQaRule
{
    public const string RuleId = "titulo-nao-codificavel";

    /// <inheritdoc />
    public string Id => RuleId;

    /// <inheritdoc />
    public string Description => "Título do jogo que a estratégia de codificação do projeto não grava.";

    /// <inheritdoc />
    public IEnumerable<QaIssue> Check(QaContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Encoder is not { } encoder || context.GameTitle is not { } title)
        {
            yield break;
        }

        if (GameTitleText.Resolve(title.Translation, encoder).Error is { } error)
        {
            yield return new QaIssue(RuleId, QaSeverity.Error, GameTitleText.UnitId, GameTitleText.UnitId, error);
        }
    }
}
