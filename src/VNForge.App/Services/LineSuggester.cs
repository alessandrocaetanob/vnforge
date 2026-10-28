using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.App.Services;

/// <summary>A sugestão de tradução de uma linha.</summary>
/// <param name="Text">Texto sugerido, com os mesmos marcadores de controle do original.</param>
/// <param name="FromFakeProvider">Veio do provedor falso (app sem chave de API), não do Claude.</param>
/// <param name="Model">O id do modelo que respondeu (<c>claude-opus-5-5</c>); nulo no provedor falso.</param>
/// <param name="Effort">O esforço pedido ao modelo, em português ("baixo"); nulo no provedor falso.</param>
public sealed record LineSuggestion(string Text, bool FromFakeProvider, string? Model = null, string? Effort = null);

/// <summary>A IA respondeu, mas sem uma sugestão aproveitável; a mensagem, em português, vai direto ao usuário.</summary>
public sealed class LineSuggestionException(string message) : Exception(message);

/// <summary>Pede à IA uma sugestão de tradução para uma linha. Atrás de uma interface para o painel rodar nos testes sem rede.</summary>
public interface ILineSuggester
{
    /// <exception cref="LineSuggestionException">O provedor não devolveu uma sugestão válida para a linha.</exception>
    Task<LineSuggestion> SuggestAsync(TextUnit unit, CancellationToken ct);
}

/// <summary>Pede à IA uma versão mais curta da tradução de uma linha que não cabe na caixa do jogo. Atrás de uma interface para os painéis rodarem nos testes sem rede.</summary>
public interface ILineShortener
{
    /// <summary>A tradução <paramref name="current"/> da linha em uma versão que a IA diz caber na caixa <paramref name="box"/>; quem chama confere se coube.</summary>
    /// <exception cref="LineSuggestionException">Sem chave de API, recusa do modelo ou resposta sem uma versão aproveitável.</exception>
    Task<string> ShortenAsync(TextUnit unit, string current, WrapRule box, CancellationToken ct);
}

/// <summary>
/// Sugere a tradução de uma linha pelo mesmo <see cref="ITranslationProvider"/> do fluxo de tradução. Sem chave de API usa o
/// <see cref="FakeTranslationProvider"/>, para o Editor funcionar (e ser testado) sem rede.
/// </summary>
/// <param name="openProvider">Abre o provedor real a cada pedido (a chave pode mudar em Ajustes); nulo quando não há chave.</param>
/// <param name="currentModel">Diz qual modelo o provedor real usa, para o cartão da sugestão; nulo não informa o modelo.</param>
public sealed partial class LineSuggester(Func<ITranslationProvider?> openProvider, Func<string>? currentModel = null) : ILineSuggester, ILineShortener
{
    private readonly Func<ITranslationProvider?> openProvider = openProvider ?? throw new ArgumentNullException(nameof(openProvider));

    /// <summary>Sugestor do app: Claude com a chave do ambiente ou do cofre e o modelo de <c>ai.json</c>; sem chave, o provedor falso.</summary>
    /// <param name="configDirectory">Pasta de configuração do usuário (<c>ai.json</c> e cofre).</param>
    /// <param name="readKey">Lê a chave a cada pedido; o padrão é o ambiente e o cofre. Os testes passam a deles.</param>
    /// <param name="createProvider">Monta o provedor para (chave, modelo); o padrão é <see cref="CreateClaude"/>.</param>
    public static LineSuggester ForUser(string configDirectory, Func<string, string?>? readKey = null, Func<string, string, ITranslationProvider>? createProvider = null)
    {
        readKey ??= directory => ApiKeyStatus.Resolve(directory).Key;
        createProvider ??= CreateClaude;

        // O cliente da Anthropic prende um HttpClient: reaproveita o provedor enquanto a chave e o modelo não mudam.
        (string Key, string Model, ITranslationProvider Provider)? cached = null;
        string? lastModel = null;
        var gate = new object();
        return new(
            () =>
            {
                var key = readKey(configDirectory);
                if (string.IsNullOrWhiteSpace(key))
                {
                    return null;
                }

                var model = ReadModel(configDirectory);
                lock (gate)
                {
                    lastModel = model;
                    if (cached is { } hit && hit.Key == key && hit.Model == model)
                    {
                        return hit.Provider;
                    }

                    (cached?.Provider as IDisposable)?.Dispose();
                    var provider = createProvider(key, model);
                    cached = (key, model, provider);
                    return provider;
                }
            },
            () => lastModel ?? string.Empty);
    }

    /// <summary>Provedor Claude da sugestão: esforço baixo, porque é uma linha só e o usuário espera a resposta.</summary>
    public static ITranslationProvider CreateClaude(string apiKey, string model) =>
        ClaudeProviderFactory.Create(apiKey, new ClaudeOptions { Model = model, Effort = ClaudeEffort.Low });

    public async Task<LineSuggestion> SuggestAsync(TextUnit unit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(unit);

        var real = await Task.Run(openProvider, ct).ConfigureAwait(false);
        var provider = new ValidatingTranslationProvider(real ?? new FakeTranslationProvider());
        var results = await provider.TranslateAsync(new TranslationBatch([unit]), ct).ConfigureAwait(false);
        var result = results.FirstOrDefault(r => r.Id == unit.Id);
        if (result is null || result.Refused || string.IsNullOrWhiteSpace(result.Translation))
        {
            throw new LineSuggestionException("O provedor não devolveu uma sugestão válida para esta linha.");
        }

        return real is null
            ? new LineSuggestion(result.Translation, FromFakeProvider: true)
            : new LineSuggestion(result.Translation, FromFakeProvider: false, currentModel?.Invoke() is { Length: > 0 } model ? model : null, Effort: "baixo");
    }

    /// <summary>Quanto custa pedir outra sugestão para a linha, em reais pela cotação da pessoa, com a marca de cotação de exemplo enquanto ela não informou a dela.</summary>
    public static string PriceText(TextUnit unit, string model, ExchangeRate rate)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(rate);
        return Controls.Money.BrlEstimate(rate.ToBrl(CostEstimator.Estimate([unit], model).Dollars), rate);
    }

    internal static string ReadModel(string configDirectory)
    {
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

        return model;
    }
}
