using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Explain;

namespace VNForge.App.Services;

/// <summary>Não foi possível pedir a explicação por um motivo que a pessoa resolve (falta a chave da IA); a mensagem, em português, vai direto a ela.</summary>
public sealed class LineExplanationException(string message) : Exception(message);

/// <summary>O "Entender o japonês" do inspetor: pede a explicação de uma fala. Atrás de uma interface para o painel rodar nos testes sem rede.</summary>
public interface ILineExplanations
{
    /// <summary>Há chave de API para pedir a explicação.</summary>
    bool IsAvailable { get; }

    /// <summary>Quanto custa uma explicação, em reais, para o botão ("menos de R$ 0,01").</summary>
    string PriceText { get; }

    /// <exception cref="LineExplanationException">Sem chave de API.</exception>
    Task<LineExplanation> ExplainAsync(string projectRoot, string source, CancellationToken ct);
}

/// <summary>
/// Explica falas com o <see cref="LineExplainer"/> do projeto aberto (cache em <c>.cache/explain.json</c>), com a chave do ambiente ou do cofre lida a cada pedido
/// porque ela pode mudar em Ajustes. O explicador é reaproveitado enquanto o projeto e a chave não mudam.
/// </summary>
public sealed class LineExplanations : ILineExplanations
{
    private readonly Func<string?> readKey;
    private readonly Func<ExchangeRate> rate;
    private readonly Func<string, string, ILineExplainer> create;
    private readonly Lock gate = new();
    private (string Root, string Key, ILineExplainer Explainer)? cached;

    /// <param name="readKey">Lê a chave de API; nulo ou em branco quando não há.</param>
    /// <param name="rate">A cotação do usuário, para o preço em reais.</param>
    /// <param name="create">Monta o explicador para (pasta do projeto, chave); o padrão usa o Claude.</param>
    public LineExplanations(Func<string?> readKey, Func<ExchangeRate> rate, Func<string, string, ILineExplainer>? create = null)
    {
        this.readKey = readKey ?? throw new ArgumentNullException(nameof(readKey));
        this.rate = rate ?? throw new ArgumentNullException(nameof(rate));
        this.create = create ?? CreateClaude;
    }

    /// <summary>O serviço do app: a chave do ambiente ou do cofre em <paramref name="configDirectory"/>.</summary>
    public static LineExplanations ForUser(string configDirectory, Func<ExchangeRate> rate) =>
        new(() => ApiKeyStatus.Resolve(configDirectory).Key, rate);

    public bool IsAvailable => !string.IsNullOrWhiteSpace(readKey());

    /// <summary>O preço em reais; com a cotação de exemplo (a pessoa ainda não informou a dela) leva a marca "cotação de exemplo", como o gasto das tarefas.</summary>
    public string PriceText
    {
        get
        {
            var current = rate();
            var text = Controls.Money.BrlEstimate(current.ToBrl(LineExplainer.EstimateDollars()));
            return current.IsInformed ? text : $"{text} (cotação de exemplo)";
        }
    }

    public async Task<LineExplanation> ExplainAsync(string projectRoot, string source, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(source);

        var key = readKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new LineExplanationException("Para entender o japonês, informe a chave da IA em Ajustes.");
        }

        ILineExplainer explainer;
        lock (gate)
        {
            if (cached is not { } hit || hit.Root != projectRoot || hit.Key != key)
            {
                cached = (projectRoot, key, create(projectRoot, key));
            }

            explainer = cached.Value.Explainer;
        }

        return await explainer.ExplainAsync(source, ct).ConfigureAwait(false);
    }

    private static LineExplainer CreateClaude(string projectRoot, string key) =>
        new(ClaudeProviderFactory.CreateClient(key), Path.Combine(projectRoot, ".cache", "explain.json"));
}
