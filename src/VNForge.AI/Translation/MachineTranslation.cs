using VNForge.Core.Text;

namespace VNForge.AI.Translation;

/// <summary>Como a tradução da IA vira linha de <c>text/*.json</c>: a mesma regra para a execução na hora (app e <c>vnf translate</c>) e para o lote noturno.</summary>
public static class MachineTranslation
{
    /// <summary>Provedor gravado na origem da linha.</summary>
    public const string ProviderName = "anthropic";

    /// <summary>A linha com a tradução gravada como <c>machine</c>; <see langword="null"/> para a linha recusada ou vazia, que fica como estava.</summary>
    public static TextUnit? Apply(TranslationOrigin origin, TranslationResult result, TextUnit unit)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(unit);
        if (result.Refused || string.IsNullOrWhiteSpace(result.Translation))
        {
            return null;
        }

        return unit with
        {
            Translation = result.Translation,
            Status = TranslationStatus.Machine,
            Origin = origin,
            PreviousSource = null,
            Doubt = result.DoubtFor(unit),
            AiTranslation = null,
        };
    }
}
