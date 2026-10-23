namespace VNForge.Core.Formatting;

/// <summary>
/// Posição de uma etapa no pipeline, na ordem do doc 02: as regras do usuário rodam antes e depois das do perfil (E4.5), e a
/// quebra de linha vem por último entre as do perfil, porque depende da largura final do texto.
/// </summary>
public enum FormattingPhase
{
    /// <summary>Substituições do usuário que rodam antes das regras do perfil (E4.5).</summary>
    UserRulesBefore,

    /// <summary>Unicode NFC, espaços invisíveis, NBSP (E4.2).</summary>
    Normalization,

    /// <summary>Espaços duplos, espaço antes e depois de pontuação (E4.2).</summary>
    Spacing,

    /// <summary>Aspas, reticências, travessão (E4.3).</summary>
    Typography,

    /// <summary>Meia largura e largura cheia (E4.3).</summary>
    Width,

    /// <summary>Quebra de linha por palavra (E4.4).</summary>
    LineBreak,

    /// <summary>Substituições do usuário que rodam depois das regras do perfil (E4.5).</summary>
    UserRulesAfter,
}

/// <summary>Canal por onde as etapas avisam o que o build deve mostrar sem recusar a linha.</summary>
public sealed class FormattingContext
{
    private readonly List<string> _warnings = [];

    public IReadOnlyList<string> Warnings => _warnings;

    public void Warn(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _warnings.Add(message);
    }
}

/// <summary>
/// Uma regra do pipeline de formatação. Recebe a tradução inteira, com os marcadores, e devolve o texto formatado.
/// O pipeline confere a saída e recusa a etapa que mexer em um marcador (ver <see cref="FormattingPipeline"/>); quem escreve uma regra
/// de texto comum herda <see cref="TextRunStage"/> e nunca vê os marcadores.
/// </summary>
public interface IFormattingStage
{
    /// <summary>Nome curto da etapa, citado nas mensagens de erro.</summary>
    string Id { get; }

    FormattingPhase Phase { get; }

    /// <summary>Verdadeiro para a etapa que pode inserir <c>\n</c> (a quebra de linha). As demais não podem criar nem tirar <c>\n</c>.</summary>
    bool MayChangeLineBreaks => false;

    string Apply(string text, FormattingContext context);
}

/// <summary>
/// Base das regras que só mexem em texto comum: o pipeline entrega a <see cref="ApplyToRun"/> cada trecho entre marcadores e remonta
/// o texto com os marcadores no lugar, então a regra não tem como alterá-los.
/// </summary>
public abstract class TextRunStage : IFormattingStage
{
    public abstract string Id { get; }

    public abstract FormattingPhase Phase { get; }

    public string Apply(string text, FormattingContext context)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(context);

        var result = new System.Text.StringBuilder(text.Length);
        foreach (var segment in ControlMarkers.Split(text))
        {
            result.Append(segment.IsMarker ? segment.Text : ApplyToRun(segment.Text, context));
        }

        return result.ToString();
    }

    /// <summary>Formata um trecho de texto comum, sem marcadores.</summary>
    protected abstract string ApplyToRun(string run, FormattingContext context);
}

/// <summary>Uma etapa alterou um marcador de controle, ou deixou o texto com um marcador inválido.</summary>
public sealed class FormattingException(string message) : VNForgeException(message);
