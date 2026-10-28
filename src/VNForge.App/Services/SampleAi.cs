using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Demo;
using VNForge.Core.Text;

namespace VNForge.App.Services;

/// <summary>
/// A IA falsa da cena de exemplo: responde na hora, sem rede, sem chave e sem gasto. Devolve o português escrito para cada fala do exemplo e, para
/// qualquer outra, o texto do <see cref="FakeTranslationProvider"/> padrão. Fica no app porque o Core não depende de <c>VNForge.AI</c>.
/// </summary>
public static class SampleAi
{
    /// <summary>Nenhum token gasto: a execução entra no registro de uso do projeto com custo zero, em vez de "custo desconhecido".</summary>
    public static ClaudeUsage NoUsage { get; } = new(0, 0, 0, 0);

    public static ITranslationProvider CreateProvider() =>
        new FakeTranslationProvider(unit => SampleProject.TranslationOf(unit.Source) ?? $"[PT] {unit.Source}");
}

/// <summary>A sugestão de uma linha da cena de exemplo: sempre pela IA de demonstração, com ou sem chave de API, para o exemplo nunca cobrar.</summary>
public sealed class SampleLineSuggester : ILineSuggester
{
    public static SampleLineSuggester Instance { get; } = new();

    public async Task<LineSuggestion> SuggestAsync(TextUnit unit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var results = await new ValidatingTranslationProvider(SampleAi.CreateProvider()).TranslateAsync(new TranslationBatch([unit]), ct).ConfigureAwait(false);
        return results.FirstOrDefault(r => r.Id == unit.Id) is { Refused: false, Translation: { } text } && !string.IsNullOrWhiteSpace(text)
            ? new LineSuggestion(text, FromFakeProvider: false, IsDemo: true)
            : throw new LineSuggestionException("A IA de demonstração não devolveu uma sugestão para esta linha.");
    }
}
