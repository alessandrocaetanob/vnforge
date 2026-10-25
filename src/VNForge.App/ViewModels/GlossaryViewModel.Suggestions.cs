using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Glossary;
using VNForge.App.Services;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>O que uma leitura do texto do projeto rende à página: a contagem por linha, as falas de cada personagem e os termos que se repetem.</summary>
internal sealed record ProjectScan(
    GlossaryUsage Usage,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Samples,
    IReadOnlyList<TermCandidate> Candidates,
    IReadOnlyList<string> Sources)
{
    public static ProjectScan Empty { get; } = new(GlossaryUsage.Empty, new Dictionary<string, IReadOnlyList<string>>(), [], []);

    public static ProjectScan From(IEnumerable<TextUnit> units)
    {
        var all = units.ToList();
        var sources = all.Select(u => u.Source).ToList();
        return new ProjectScan(GlossaryUsage.From(all), SpeakerSamples.From(all), RepeatedTerms.Count(sources), sources);
    }

    private const int ExampleContext = 30;
    private readonly Dictionary<string, string?> examples = new(StringComparer.Ordinal);

    /// <summary>
    /// Um trecho da primeira linha em que o termo aparece, com até <see cref="ExampleContext"/> caracteres de cada lado, para dar contexto à IA.
    /// Guarda a resposta por termo: a tela pergunta de novo a cada tecla e a busca varre o texto do projeto inteiro.
    /// </summary>
    public string? ExampleOf(string term)
    {
        if (!examples.TryGetValue(term, out var example))
        {
            example = Excerpt(Sources.FirstOrDefault(s => s.Contains(term, StringComparison.Ordinal)), term);
            examples[term] = example;
        }

        return example;
    }

    private static string? Excerpt(string? line, string term)
    {
        if (line is null)
        {
            return null;
        }

        var at = line.IndexOf(term, StringComparison.Ordinal);
        var from = Math.Max(0, at - ExampleContext);
        var to = Math.Min(line.Length, at + term.Length + ExampleContext);
        // Cortar no meio de um par substituto deixaria meio caractere no texto.
        if (from > 0 && char.IsLowSurrogate(line[from]))
        {
            from--;
        }

        if (to < line.Length && char.IsLowSurrogate(line[to]))
        {
            to++;
        }

        return line[from..to].Trim();
    }
}

/// <summary>Sugestões da IA na página Glossário: voz e gênero dos personagens pelas falas, e a tradução dos termos que se repetem.</summary>
public sealed partial class GlossaryViewModel
{
    /// <summary>Quantos termos que se repetem o painel mostra.</summary>
    public const int RepeatedShown = 30;

    private ProjectScan scan = ProjectScan.Empty;
    private readonly Dictionary<string, (string Target, string Reason)> translated = new(StringComparer.Ordinal);
    private List<CharacterVoiceRequest> voiceRequests = [];
    private CancellationTokenSource? suggestCts;

    /// <summary>A IA das sugestões; nulo desliga os botões. O app passa a que usa a chave da pessoa.</summary>
    [ObservableProperty]
    public partial IGlossarySuggester? Suggester { get; set; }

    /// <summary>A cotação do dólar da pessoa, para mostrar o preço em reais.</summary>
    public Func<ExchangeRate> Rate { get; set; } = () => ExchangeRate.Placeholder;

    /// <summary>Os termos do painel "Termos que se repetem": sequências que aparecem em 5 linhas ou mais e ainda não estão no glossário.</summary>
    public ObservableCollection<RepeatedTermRow> RepeatedTermRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSuggestVoices), nameof(CanSuggestTerms), nameof(SuggestingText))]
    public partial bool IsSuggesting { get; private set; }

    /// <summary>A IA não respondeu; a frase explica o porquê em português.</summary>
    [ObservableProperty]
    public partial string? SuggestionError { get; private set; }

    /// <summary>O que a última sugestão fez, para a pessoa conferir.</summary>
    [ObservableProperty]
    public partial string? SuggestionNotice { get; private set; }

    public string SuggestingText => (IsSuggesting, IsCharactersTab) switch
    {
        (false, _) => string.Empty,
        (true, true) => "Lendo as falas dos personagens…",
        _ => "Traduzindo os termos…",
    };

    public int MissingGenderCount => Characters.Count(c => c.NeedsGender);

    public bool HasMissingGender => MissingGenderCount > 0;

    public string MissingGenderText => MissingGenderCount switch
    {
        0 => string.Empty,
        1 => "1 personagem está sem gênero. A IA precisa dele para concordar o texto (ele ou ela, cansado ou cansada).",
        var n => $"{n} personagens estão sem gênero. A IA precisa dele para concordar o texto (ele ou ela, cansado ou cansada).",
    };

    /// <summary>Quantos personagens a sugestão cobriria: os sem gênero e os falantes do jogo que ainda não estão na tabela.</summary>
    public int VoiceRequestCount => voiceRequests.Count;

    /// <summary>O preço da sugestão de voz e gênero em reais; vazio enquanto não há o que sugerir ou o texto não foi lido.</summary>
    public string VoicePriceText => voiceRequests.Count == 0
        ? string.Empty
        : PriceText(GlossaryCost.Voices(voiceRequests, ClaudeModels.Haiku));

    public bool CanSuggestVoices => CanEdit && usageLoaded && !IsSuggesting && Suggester is not null && voiceRequests.Count > 0;

    public bool HasRepeatedTerms => RepeatedTermRows.Count > 0;

    public bool NoRepeatedTerms => RepeatedTermRows.Count == 0;

    public string RepeatedSummary
    {
        get
        {
            var count = RepeatedTermRows.Count;
            if (!usageLoaded)
            {
                return "Lendo o texto do jogo…";
            }

            return count == 0
                ? "Nenhum termo novo aparece 5 vezes ou mais."
                : $"{count} {Plural(count, "termo aparece", "termos aparecem")} 5 vezes ou mais e {Plural(count, "não está", "não estão")} no glossário.";
        }
    }

    private static string Plural(int count, string one, string many) => count == 1 ? one : many;

    private string PriceText(CostEstimate estimate)
    {
        var rate = Rate();
        return Controls.Money.BrlEstimate(CostEstimator.Brl(estimate, rate), rate);
    }

    private List<TermTranslationRequest> PendingTermRequests() =>
        [.. RepeatedTermRows.Where(r => !r.HasSuggestion).Select(r => new TermTranslationRequest(r.Source, r.Lines, scan.ExampleOf(r.Source)))];

    /// <summary>O preço de a IA traduzir os termos do painel que ainda não têm tradução; vazio quando todos já têm.</summary>
    public string TermsPriceText
    {
        get
        {
            var pending = PendingTermRequests();
            return pending.Count == 0 ? string.Empty : PriceText(GlossaryCost.Terms(pending, ClaudeModels.Haiku));
        }
    }

    public bool CanSuggestTerms => CanEdit && !IsSuggesting && Suggester is not null && RepeatedTermRows.Any(r => !r.HasSuggestion);

    partial void OnSuggesterChanged(IGlossarySuggester? value) => NotifySuggestionState();

    private void ResetSuggestions()
    {
        ReleaseSuggestionSource();
        scan = ProjectScan.Empty;
        translated.Clear();
        voiceRequests = [];
        RepeatedTermRows.Clear();
        IsSuggesting = false;
        SuggestionError = null;
        SuggestionNotice = null;
    }

    /// <summary>Refaz a lista de quem a sugestão de voz cobriria e avisa a tela: a tabela mudou ou o texto do projeto acabou de ser lido.</summary>
    private void NotifyVoices()
    {
        if (loading)
        {
            return;
        }

        voiceRequests = usageLoaded ? BuildVoiceRequests() : [];
        OnPropertyChanged(nameof(MissingGenderCount));
        OnPropertyChanged(nameof(HasMissingGender));
        OnPropertyChanged(nameof(MissingGenderText));
        OnPropertyChanged(nameof(VoiceRequestCount));
        OnPropertyChanged(nameof(VoicePriceText));
        NotifySuggestionState();
    }

    private void NotifySuggestionState()
    {
        OnPropertyChanged(nameof(CanSuggestVoices));
        OnPropertyChanged(nameof(CanSuggestTerms));
        SuggestVoicesCommand.NotifyCanExecuteChanged();
        SuggestTermsCommand.NotifyCanExecuteChanged();
    }

    private List<CharacterVoiceRequest> BuildVoiceRequests()
    {
        var known = Characters.GroupBy(c => c.Source.Trim(), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return
        [
            .. scan.Samples
                .Where(p => !known.TryGetValue(p.Key, out var row) || row.Gender == CharacterGender.Unknown)
                .Select(p => new CharacterVoiceRequest(p.Key, p.Value)),
        ];
    }

    private void RefreshRepeatedTerms()
    {
        if (loading)
        {
            return;
        }

        var known = Terms.Select(t => t.Source).Concat(Characters.Select(c => c.Source));
        var shown = RepeatedTerms.Excluding(scan.Candidates, known).Take(RepeatedShown).ToList();
        if (!RepeatedTermRows.Select(r => r.Source).SequenceEqual(shown.Select(c => c.Source)))
        {
            Replace(RepeatedTermRows, shown.Select(ToRow));
        }

        OnPropertyChanged(nameof(HasRepeatedTerms));
        OnPropertyChanged(nameof(NoRepeatedTerms));
        OnPropertyChanged(nameof(RepeatedSummary));
        OnPropertyChanged(nameof(TermsPriceText));
        NotifySuggestionState();
    }

    private RepeatedTermRow ToRow(TermCandidate candidate)
    {
        var row = new RepeatedTermRow(candidate.Source, candidate.Lines);
        if (translated.TryGetValue(candidate.Source, out var known))
        {
            row.Target = known.Target;
            row.Reason = known.Reason;
        }

        return row;
    }

    /// <summary>Pede à IA o gênero e o jeito de falar dos personagens sem gênero e dos falantes que ainda não estão na tabela. Só preenche o que está vazio.</summary>
    [RelayCommand(CanExecute = nameof(CanSuggestVoices))]
    private async Task SuggestVoicesAsync()
    {
        var requests = voiceRequests;
        var inTable = Characters.Select(c => c.Source.Trim()).ToHashSet(StringComparer.Ordinal);
        var token = BeginSuggesting();
        var version = usageVersion;
        try
        {
            var suggestions = await Suggester!.SuggestVoicesAsync(requests, token).ConfigureAwait(true);
            if (version == usageVersion)
            {
                var changed = ApplyVoices(suggestions, inTable);
                SuggestionNotice = changed == 0
                    ? "A IA não achou nada novo nas falas. Preencha o gênero à mão."
                    : $"{changed} {Plural(changed, "personagem recebeu", "personagens receberam")} gênero e jeito de falar. Confira e ajuste o que não bater.";
            }
        }
        catch (OperationCanceledException)
        {
            // A pessoa trocou de projeto no meio do pedido: nada a mostrar.
        }
        catch (Exception e)
        {
            SuggestionError = Explain(e);
        }
        finally
        {
            EndSuggesting(token);
            NotifyVoices();
        }
    }

    /// <param name="inTable">Os nomes que já estavam na tabela quando a sugestão foi pedida: um que sumiu no meio do pedido foi removido pela pessoa e não volta.</param>
    private int ApplyVoices(IReadOnlyList<CharacterVoiceSuggestion> suggestions, HashSet<string> inTable)
    {
        ClearSearch();
        var changed = 0;
        foreach (var suggestion in suggestions)
        {
            var row = Characters.FirstOrDefault(c => c.Source.Trim() == suggestion.Name);
            if (row is null)
            {
                if (inTable.Contains(suggestion.Name))
                {
                    continue;
                }

                Characters.Add(Watch(new CharacterRow(suggestion.Name, string.Empty, suggestion.Gender, suggestion.Voice), MarkCharactersDirty));
                changed++;
                continue;
            }

            var gender = row.Gender == CharacterGender.Unknown && suggestion.Gender != CharacterGender.Unknown;
            var tone = string.IsNullOrWhiteSpace(row.Tone) && suggestion.Voice.Length > 0;
            if (gender)
            {
                row.Gender = suggestion.Gender;
            }

            if (tone)
            {
                row.Tone = suggestion.Voice;
            }

            changed += gender || tone ? 1 : 0;
        }

        return changed;
    }

    /// <summary>Pede à IA a tradução dos termos do painel que ainda não têm uma, com o motivo em uma frase.</summary>
    [RelayCommand(CanExecute = nameof(CanSuggestTerms))]
    private async Task SuggestTermsAsync()
    {
        var requests = PendingTermRequests();
        var token = BeginSuggesting();
        var version = usageVersion;
        try
        {
            var suggestions = await Suggester!.SuggestTermsAsync(requests, token).ConfigureAwait(true);
            if (version == usageVersion)
            {
                ApplyTerms(suggestions);
                SuggestionNotice = $"{suggestions.Count} {Plural(suggestions.Count, "termo traduzido", "termos traduzidos")}. Adicione os que servirem.";
            }
        }
        catch (OperationCanceledException)
        {
            // A pessoa trocou de projeto no meio do pedido: nada a mostrar.
        }
        catch (Exception e)
        {
            SuggestionError = Explain(e);
        }
        finally
        {
            EndSuggesting(token);
            RefreshRepeatedTerms();
        }
    }

    private void ApplyTerms(IReadOnlyList<TermTranslationSuggestion> suggestions)
    {
        foreach (var suggestion in suggestions)
        {
            translated[suggestion.Source] = (suggestion.Target, suggestion.Reason);
            var row = RepeatedTermRows.FirstOrDefault(r => r.Source == suggestion.Source);
            if (row is not null)
            {
                row.Target = suggestion.Target;
                row.Reason = suggestion.Reason;
            }
        }
    }

    /// <summary>Põe o termo do painel na tabela de termos, com a tradução sugerida quando houver; a pessoa revisa na própria linha.</summary>
    [RelayCommand]
    private void AddRepeatedTerm(RepeatedTermRow? row)
    {
        if (row is null || !CanEdit)
        {
            return;
        }

        ClearSearch();
        Terms.Add(Watch(new GlossaryTermRow(row.Source, row.Target ?? string.Empty), MarkTermsDirty));
    }

    private CancellationToken BeginSuggesting()
    {
        ReleaseSuggestionSource();
        suggestCts = new CancellationTokenSource();
        SuggestionError = null;
        SuggestionNotice = null;
        IsSuggesting = true;
        NotifySuggestionState();
        return suggestCts.Token;
    }

    // Um pedido que perdeu para outro (ou para a troca de projeto) não mexe no estado do que está em andamento.
    private void EndSuggesting(CancellationToken token)
    {
        if (!token.IsCancellationRequested)
        {
            IsSuggesting = false;
            ReleaseSuggestionSource();
        }
    }

    // Cancela o pedido em andamento (se houver) e solta a fonte do token.
    private void ReleaseSuggestionSource()
    {
        suggestCts?.Cancel();
        suggestCts?.Dispose();
        suggestCts = null;
    }

    /// <summary>Refaz os preços em reais: a cotação mudou em Ajustes.</summary>
    public void RefreshPrices()
    {
        OnPropertyChanged(nameof(VoicePriceText));
        OnPropertyChanged(nameof(TermsPriceText));
    }

    // O detalhe técnico (stop_reason, o que faltou na resposta) vai para o log; a tela só recebe a frase em português.
    private static string Explain(Exception error)
    {
        var detail = error switch
        {
            ClaudeRefusalException refusal => refusal.Detail,
            ClaudeResponseException response => response.Detail,
            _ => null,
        };
        Debug.WriteLine(detail is null ? error.ToString() : $"{error.Message} [{detail}]");
        return error is GlossarySuggestionException ? error.Message : ClaudeErrorMessages.Describe(error);
    }
}
