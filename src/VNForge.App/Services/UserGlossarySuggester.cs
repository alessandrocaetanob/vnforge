using VNForge.AI.Claude;
using VNForge.AI.Glossary;

namespace VNForge.App.Services;

/// <summary>As sugestões do Glossário não podem rodar; a mensagem, em português, vai direto à pessoa.</summary>
public sealed class GlossarySuggestionException(string message) : Exception(message);

/// <summary>
/// As sugestões do Glossário com a chave de API da pessoa (ambiente ou cofre). Sem chave não há provedor falso que sirva, porque um gênero
/// inventado seria pior que nenhum: a pessoa recebe o aviso para informar a chave em Ajustes.
/// </summary>
/// <param name="openSuggester">Abre o sugestor real a cada pedido (a chave pode mudar em Ajustes); nulo quando não há chave.</param>
public sealed class UserGlossarySuggester(Func<IGlossarySuggester?> openSuggester) : IGlossarySuggester
{
    private readonly Func<IGlossarySuggester?> openSuggester = openSuggester ?? throw new ArgumentNullException(nameof(openSuggester));

    /// <param name="configDirectory">Pasta de configuração do usuário (cofre da chave).</param>
    /// <param name="readKey">Lê a chave a cada pedido; o padrão é o ambiente e o cofre. Os testes passam a deles.</param>
    /// <param name="create">Monta o sugestor para uma chave; o padrão é <see cref="CreateClaude"/>.</param>
    public static UserGlossarySuggester ForUser(string configDirectory, Func<string, string?>? readKey = null, Func<string, IGlossarySuggester>? create = null)
    {
        readKey ??= directory => ApiKeyStatus.Resolve(directory).Key;
        create ??= CreateClaude;

        // O cliente da Anthropic prende um HttpClient: reaproveita o sugestor enquanto a chave não muda.
        (string Key, IGlossarySuggester Suggester)? cached = null;
        var gate = new object();
        return new(() =>
        {
            var key = readKey(configDirectory);
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            lock (gate)
            {
                if (cached is { } hit && hit.Key == key)
                {
                    return hit.Suggester;
                }

                cached = (key, create(key));
                return cached.Value.Suggester;
            }
        });
    }

    /// <summary>Sugestor Claude no modelo mais barato: a tarefa é curta e a pessoa confere o resultado.</summary>
    public static IGlossarySuggester CreateClaude(string apiKey) => new ClaudeGlossarySuggester(ClaudeProviderFactory.CreateClient(apiKey));

    public async Task<IReadOnlyList<CharacterVoiceSuggestion>> SuggestVoicesAsync(IReadOnlyList<CharacterVoiceRequest> characters, CancellationToken ct) =>
        await (await OpenAsync(ct).ConfigureAwait(false)).SuggestVoicesAsync(characters, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<TermTranslationSuggestion>> SuggestTermsAsync(IReadOnlyList<TermTranslationRequest> terms, CancellationToken ct) =>
        await (await OpenAsync(ct).ConfigureAwait(false)).SuggestTermsAsync(terms, ct).ConfigureAwait(false);

    private async Task<IGlossarySuggester> OpenAsync(CancellationToken ct) =>
        await Task.Run(openSuggester, ct).ConfigureAwait(false)
        ?? throw new GlossarySuggestionException("Informe a chave de API em Ajustes para pedir sugestões à IA.");
}
