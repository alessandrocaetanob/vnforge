using System.Collections.Concurrent;
using VNForge.Core.Glossary;

namespace VNForge.AI.Glossary;

/// <summary>Sugestor determinístico para testes: sem rede e sem SDK. Por padrão devolve gênero <see cref="CharacterGender.Other"/> e a tradução igual ao original.</summary>
public sealed class FakeGlossarySuggester : IGlossarySuggester
{
    private readonly Func<CharacterVoiceRequest, CharacterVoiceSuggestion> voice;
    private readonly Func<TermTranslationRequest, TermTranslationSuggestion> term;
    private readonly ConcurrentQueue<IReadOnlyList<CharacterVoiceRequest>> receivedVoices = new();
    private readonly ConcurrentQueue<IReadOnlyList<TermTranslationRequest>> receivedTerms = new();
    private readonly Func<IReadOnlyList<AddressTermRequest>, IReadOnlyList<AddressTermCharacter>, IReadOnlyList<AddressTermSuggestion>> address;
    private readonly ConcurrentQueue<IReadOnlyList<AddressTermRequest>> receivedAddress = new();

    public FakeGlossarySuggester(
        Func<CharacterVoiceRequest, CharacterVoiceSuggestion>? voice = null,
        Func<TermTranslationRequest, TermTranslationSuggestion>? term = null,
        Func<IReadOnlyList<AddressTermRequest>, IReadOnlyList<AddressTermCharacter>, IReadOnlyList<AddressTermSuggestion>>? address = null)
    {
        this.address = address ?? ((_, _) => []);
        this.voice = voice ?? (c => new CharacterVoiceSuggestion(c.Name, CharacterGender.Other, "neutro"));
        this.term = term ?? (t => new TermTranslationSuggestion(t.Source, t.Source, "Igual ao original."));
    }

    /// <summary>Pedidos de voz e gênero recebidos, na ordem das chamadas.</summary>
    public IReadOnlyCollection<IReadOnlyList<CharacterVoiceRequest>> ReceivedVoices => receivedVoices;

    /// <summary>Pedidos de tradução de termos recebidos, na ordem das chamadas.</summary>
    public IReadOnlyCollection<IReadOnlyList<TermTranslationRequest>> ReceivedTerms => receivedTerms;

    /// <summary>Pedidos de tratamento recebidos, na ordem das chamadas.</summary>
    public IReadOnlyCollection<IReadOnlyList<AddressTermRequest>> ReceivedAddress => receivedAddress;

    public Task<IReadOnlyList<CharacterVoiceSuggestion>> SuggestVoicesAsync(IReadOnlyList<CharacterVoiceRequest> characters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(characters);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<IReadOnlyList<CharacterVoiceSuggestion>>(ct);
        }

        receivedVoices.Enqueue(characters);
        IReadOnlyList<CharacterVoiceSuggestion> result = [.. characters.Select(voice)];
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<TermTranslationSuggestion>> SuggestTermsAsync(IReadOnlyList<TermTranslationRequest> terms, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<IReadOnlyList<TermTranslationSuggestion>>(ct);
        }

        receivedTerms.Enqueue(terms);
        IReadOnlyList<TermTranslationSuggestion> result = [.. terms.Select(term)];
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<AddressTermSuggestion>> SuggestAddressTermsAsync(
        IReadOnlyList<AddressTermRequest> speakers, IReadOnlyList<AddressTermCharacter> characters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(speakers);
        ArgumentNullException.ThrowIfNull(characters);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<IReadOnlyList<AddressTermSuggestion>>(ct);
        }

        receivedAddress.Enqueue(speakers);
        return Task.FromResult(address(speakers, characters));
    }
}
