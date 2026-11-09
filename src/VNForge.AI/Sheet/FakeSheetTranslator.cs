using System.Collections.Concurrent;
using VNForge.Core.Factsheet;

namespace VNForge.AI.Sheet;

/// <summary>
/// Tradutor da ficha determinístico para testes e para o app sem chave: sem rede e sem SDK. Por padrão escreve <see cref="Prefix"/> na frente do original, o que mantém os
/// marcadores de spoiler onde estavam. Quem quiser outra resposta (ou uma falha) passa <c>translate</c>; devolver <c>null</c> deixa o item fora da resposta, como uma IA que o esqueceu.
/// </summary>
public sealed class FakeSheetTranslator : ISheetTranslator
{
    public const string Prefix = "[pt-BR] ";

    private readonly Func<SheetTranslationItem, string?> _translate;
    private readonly ConcurrentQueue<SheetTranslationRequest> _requests = new();

    public FakeSheetTranslator(Func<SheetTranslationItem, string?>? translate = null)
    {
        _translate = translate ?? (item => Prefix + item.Source);
    }

    /// <summary>Os pedidos recebidos, na ordem das chamadas.</summary>
    public IReadOnlyCollection<SheetTranslationRequest> Requests => _requests;

    public Task<IReadOnlyList<SheetTranslation>> TranslateAsync(SheetTranslationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<IReadOnlyList<SheetTranslation>>(ct);
        }

        _requests.Enqueue(request);
        try
        {
            IReadOnlyList<SheetTranslation> reply =
            [
                .. request.Items.Select(i => (i.Key, Text: _translate(i))).Where(t => t.Text is not null).Select(t => new SheetTranslation(t.Key, t.Text!)),
            ];
            return Task.FromResult(reply);
        }
        catch (Exception e)
        {
            return Task.FromException<IReadOnlyList<SheetTranslation>>(e);
        }
    }
}
