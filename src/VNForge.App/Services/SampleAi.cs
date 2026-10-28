using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Demo;

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
