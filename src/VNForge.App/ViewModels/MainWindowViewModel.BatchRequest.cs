using VNForge.AI.BatchRequest;
using VNForge.AI.Claude;
using VNForge.App.Services;

namespace VNForge.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>Quantas palavras tem um texto da paleta para valer como pedido à IA; "trocar você por tu" já tem quatro.</summary>
    private const int MinRequestWords = 3;

    private readonly BatchRequestService batchRequests;

    /// <summary>Monta o provedor da IA do pedido em lote para (modelo, esforço). O app passa o Claude com a chave do usuário; os testes passam o provedor falso.</summary>
    public Func<string, ClaudeEffort, IBatchRequestProvider> BatchRequestProviderFactory { get; init; } =
        static (_, _) => throw new InvalidOperationException("Nenhum provedor de IA configurado.");

    private BatchRequestService NewBatchRequestService() => new(
        (model, effort) => BatchRequestProviderFactory(model, effort),
        () => new BatchRequestSettings(Settings.Rate, Settings.Budget, Settings.DefaultQuality, Settings.Concurrency, ApiKeyConfigured));

    /// <summary>Um texto de três palavras ou mais, com projeto aberto, vira o comando "Pedir à IA" no alto dos comandos.</summary>
    private PaletteEntry? PaletteRequest(string query)
    {
        if (CurrentProject is null || query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < MinRequestWords)
        {
            return null;
        }

        return new PaletteEntry(PaletteGroup.Command, $"Pedir à IA: {query}", "Acha as falas, mostra o custo e a prévia antes de gravar", () => StartBatchRequest(query));
    }

    private void StartBatchRequest(string request)
    {
        if (!GoTo(editorItem))
        {
            return;
        }

        if (!Editor.OpenBatchRequest(request))
        {
            Toasts.Show("Saia do modo revisão para pedir à IA em lote.", ToastKind.Warning, key: "batch-request");
        }
    }
}
