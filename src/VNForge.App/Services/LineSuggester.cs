using VNForge.AI.Claude;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.App.Services;

/// <summary>A sugestão de tradução de uma linha.</summary>
/// <param name="Text">Texto sugerido, com os mesmos marcadores de controle do original.</param>
/// <param name="FromFakeProvider">Veio do provedor falso (app sem chave de API), não do Claude.</param>
public sealed record LineSuggestion(string Text, bool FromFakeProvider);

/// <summary>Pede à IA uma sugestão de tradução para uma linha. Atrás de uma interface para o painel rodar nos testes sem rede.</summary>
public interface ILineSuggester
{
    /// <exception cref="InvalidOperationException">O provedor não devolveu uma sugestão válida para a linha.</exception>
    Task<LineSuggestion> SuggestAsync(TextUnit unit, CancellationToken ct);
}

/// <summary>
/// Sugere a tradução de uma linha pelo mesmo <see cref="ITranslationProvider"/> do fluxo de tradução. Sem chave de API usa o
/// <see cref="FakeTranslationProvider"/>, para o Editor funcionar (e ser testado) sem rede.
/// </summary>
/// <param name="openProvider">Abre o provedor real a cada pedido (a chave pode mudar em Ajustes); nulo quando não há chave.</param>
public sealed class LineSuggester(Func<ITranslationProvider?> openProvider) : ILineSuggester
{
    private readonly Func<ITranslationProvider?> openProvider = openProvider ?? throw new ArgumentNullException(nameof(openProvider));

    /// <summary>Sugestor do app: Claude com a chave do ambiente ou do cofre e o modelo de <c>ai.json</c>; sem chave, o provedor falso.</summary>
    public static LineSuggester ForUser(string configDirectory) => new(() => OpenClaude(configDirectory));

    public async Task<LineSuggestion> SuggestAsync(TextUnit unit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(unit);

        var real = openProvider();
        var provider = new ValidatingTranslationProvider(real ?? new FakeTranslationProvider());
        var results = await provider.TranslateAsync(new TranslationBatch([unit]), ct).ConfigureAwait(false);
        var result = results.FirstOrDefault(r => r.Id == unit.Id);
        if (result is null || result.Refused || string.IsNullOrWhiteSpace(result.Translation))
        {
            throw new InvalidOperationException("O provedor não devolveu uma sugestão válida para esta linha.");
        }

        return new LineSuggestion(result.Translation, FromFakeProvider: real is null);
    }

    private static ITranslationProvider? OpenClaude(string configDirectory)
    {
        var key = ApiKeyStatus.Resolve(configDirectory).Key;
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var settingsFile = new AiUserSettingsFile(configDirectory);
        var model = AiUserSettings.Default.Model;
        try
        {
            if (File.Exists(settingsFile.Path))
            {
                model = settingsFile.Load().Model;
            }
        }
        catch (AiSettingsException)
        {
            // ai.json inválido: a sugestão usa o modelo padrão em vez de falhar; o erro aparece em vnf config.
        }

        return ClaudeProviderFactory.Create(key, new ClaudeOptions { Model = model, Effort = ClaudeEffort.Low });
    }
}
