namespace VNForge.App.ViewModels;

public sealed partial class EditorViewModel
{
    /// <summary>O último pedido em lote aberto pela paleta; os testes o aguardam.</summary>
    public Task BatchRequest { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Abre o painel de busca e substituição com o pedido escrito em português: ele acha as falas e mostra o custo, e a IA só é chamada quando a pessoa confirma.
    /// Falso, sem abrir nada, quando o modo revisão está aberto.
    /// </summary>
    public bool OpenBatchRequest(string request)
    {
        if (IsReviewing)
        {
            return false;
        }

        // O leitor de cena cobre a grade e o painel de busca: fecha o leitor, como o modo revisão faz ao abrir (StartReviewOf).
        EndReading();
        IsSearchOpen = true;
        IsReplaceVisible = true;
        SearchOpened?.Invoke(this, true);
        BatchRequest = Search.BeginRequestAsync(request);
        return true;
    }
}
