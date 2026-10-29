using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Glossary;
using VNForge.App.Services;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>
/// A tabela "Quem chama quem" do Glossário (#470): como cada personagem chama os outros, no japonês e na tradução. Guarda as linhas e as sugestões da IA;
/// quem grava <c>address-terms.json</c> é o <see cref="GlossaryViewModel"/>, que escuta <see cref="Changed"/> e junta o salvamento ao dos outros arquivos.
/// A IA só sugere: a linha sugerida fica esmaecida, não vai para o arquivo e só entra com o aceite.
/// </summary>
public sealed partial class AddressTermsViewModel : ObservableObject
{
    private readonly ObservableCollection<CharacterRow> characters;
    private readonly Func<IGlossarySuggester?> suggester;
    private readonly Func<ExchangeRate> rate;
    private readonly Func<bool> canEdit;
    private IReadOnlyList<TextUnit> units = [];
    private bool textLoaded;
    private bool loading;
    private bool requestsStale;
    private List<AddressTermRequest> requests = [];
    private CancellationTokenSource? cts;

    /// <param name="characters">A tabela de personagens da página: de onde vêm os nomes para escolher e as falas a ler.</param>
    /// <param name="suggester">A IA das sugestões; nulo desliga o botão.</param>
    /// <param name="rate">A cotação do dólar da pessoa, para mostrar o preço em reais.</param>
    /// <param name="canEdit">Falso enquanto não há projeto aberto ou o arquivo está ilegível.</param>
    public AddressTermsViewModel(ObservableCollection<CharacterRow> characters, Func<IGlossarySuggester?> suggester, Func<ExchangeRate> rate, Func<bool> canEdit)
    {
        this.characters = characters;
        this.suggester = suggester;
        this.rate = rate;
        this.canEdit = canEdit;
        characters.CollectionChanged += (_, _) => OnCharactersChanged();
        Rows.CollectionChanged += OnRowsChanged;
    }

    public ObservableCollection<AddressTermRow> Rows { get; } = [];

    /// <summary>Os nomes originais dos personagens da tabela, as opções de "quem fala" e "com quem".</summary>
    public ObservableCollection<string> CharacterNames { get; } = [];

    /// <summary>Disparado quando a pessoa mudou o que vai para o arquivo (uma linha nova, editada, removida ou aceita).</summary>
    public event EventHandler? Changed;

    public bool CanEdit => canEdit();

    public bool NoRows => Rows.Count == 0;

    /// <summary>As linhas completas (par, original e tradução); a linha ainda incompleta e a sugestão pendente não contam. Repetida ou com o mesmo falante nos dois lados só é recusada ao salvar.</summary>
    public int PairCount => Rows.Count(r => !r.IsSuggestion && r.IsComplete);

    public string Summary => Summarize(PairCount, PendingCount);

    public string EmptyText { get; } = "Nenhum tratamento ainda. Peça à IA pelas falas ou adicione o primeiro.";

    private static string Summarize(int pairs, int pending)
    {
        if (pairs == 0 && pending == 0)
        {
            return "Nenhum tratamento ainda";
        }

        var pairsText = $"{ProjectLabels.Number(pairs)} {(pairs == 1 ? "tratamento" : "tratamentos")}";
        if (pending == 0)
        {
            return pairsText;
        }

        var pendingText = $"{ProjectLabels.Number(pending)} {(pending == 1 ? "sugestão" : "sugestões")}";
        return $"{pairsText} · {pendingText} da IA";
    }

    /// <summary>Quantas linhas ficaram de fora do arquivo (quem fala ou com quem fala em branco, ou par repetido).</summary>
    public int ProblemCount => Rows.Count(r => r.HasProblem);

    public int PendingCount => Rows.Count(r => r.IsSuggestion);

    public bool HasPending => PendingCount > 0;

    public string AcceptAllText => $"Aceitar as {ProjectLabels.Number(PendingCount)}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSuggest), nameof(SuggestingText))]
    public partial bool IsSuggesting { get; private set; }

    public string SuggestingText => IsSuggesting ? "Lendo as falas…" : string.Empty;

    /// <summary>A IA não respondeu; a frase explica o porquê em português.</summary>
    [ObservableProperty]
    public partial string? SuggestionError { get; private set; }

    /// <summary>O que a última sugestão fez, para a pessoa conferir.</summary>
    [ObservableProperty]
    public partial string? SuggestionNotice { get; private set; }

    public bool CanSuggest => CanEdit && textLoaded && !IsSuggesting && suggester() is not null && requests.Count > 0;

    /// <summary>O preço do pedido em reais; vazio enquanto não há falas para ler.</summary>
    public string PriceText
    {
        get
        {
            if (requests.Count == 0)
            {
                return string.Empty;
            }

            var estimate = GlossaryCost.AddressTerms(requests, Roster(), ClaudeModels.Haiku);
            var current = rate();
            var brl = CostEstimator.Brl(estimate, current);
            return Controls.Money.WithApprox(Controls.Money.BrlEstimate(brl), brl, current);
        }
    }

    public string SuggestHint
    {
        get
        {
            const string hint = "A IA lê as falas em que cada personagem chama alguém e sugere o tratamento de cada par. Nada entra no projeto até você aceitar.";
            return PriceText.StartsWith('≈') ? $"{hint} O preço usa uma {Controls.Money.ExampleRateMark}; informe a sua em Ajustes." : hint;
        }
    }

    /// <summary>Descarta as linhas e o texto lido: o projeto mudou ou a página vai reler os arquivos.</summary>
    public void Clear()
    {
        Release();
        loading = true;
        try
        {
            Rows.Clear();
        }
        finally
        {
            loading = false;
        }

        units = [];
        textLoaded = false;
        requests = [];
        IsSuggesting = false;
        SuggestionError = null;
        SuggestionNotice = null;
        RefreshNames();
        NotifyAll();
    }

    /// <summary>Põe na tabela os tratamentos que o arquivo traz, sem marcar a página como alterada.</summary>
    public void Load(IEnumerable<AddressTerm> terms)
    {
        loading = true;
        try
        {
            foreach (var term in terms)
            {
                Rows.Add(Watch(new AddressTermRow(term.From, term.To, term.Source, term.Target)));
            }
        }
        finally
        {
            loading = false;
        }

        NotifyAll();
    }

    /// <summary>O texto do projeto acabou de ser lido: dele saem as falas que a IA lê.</summary>
    public void SetText(IReadOnlyList<TextUnit> text)
    {
        units = text;
        textLoaded = true;
        RebuildRequests();
    }

    /// <summary>
    /// As linhas que podem ir para o arquivo, na ordem da tabela, com os espaços aparados. As sugestões ainda não aceitas ficam de fora, assim como as
    /// incompletas e as repetidas (da segunda em diante), que recebem o <see cref="AddressTermRow.Problem"/>; as outras seguem.
    /// </summary>
    public List<AddressTerm> ValidTerms()
    {
        var seen = new List<AddressTermRow>();
        var valid = new List<AddressTerm>();
        foreach (var row in Rows.Where(r => !r.IsSuggestion))
        {
            row.Problem = ProblemOf(row, seen);
            if (!row.IsBlank && row.Problem is null)
            {
                seen.Add(row);
                valid.Add(row.ToTerm());
            }
        }

        OnPropertyChanged(nameof(ProblemCount));
        return valid;
    }

    private static string? ProblemOf(AddressTermRow row, List<AddressTermRow> seen)
    {
        if (row.IsBlank)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(row.From) || string.IsNullOrWhiteSpace(row.To))
        {
            return "Escolha quem fala e com quem fala.";
        }

        if (!row.IsComplete)
        {
            return "Falta como o personagem o chama, no japonês e na tradução.";
        }

        if (CharacterBook.NameComparer.Equals(row.From, row.To))
        {
            return "Quem fala e com quem fala são a mesma pessoa.";
        }

        return seen.Any(s => SamePair(s, row.From, row.To)) ? "Este par já está na tabela." : null;
    }

    private static bool SamePair(AddressTermRow row, string from, string to) =>
        CharacterBook.NameComparer.Equals(row.From, from) && CharacterBook.NameComparer.Equals(row.To, to);

    /// <summary>Adiciona uma linha em branco.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    public void Add() => Rows.Add(Watch(new AddressTermRow()));

    [RelayCommand]
    private void Remove(AddressTermRow? row)
    {
        if (row is not null)
        {
            Rows.Remove(row);
        }
    }

    /// <summary>Passa a sugestão da IA para a tabela: a linha deixa de ser esmaecida e vai para o arquivo.</summary>
    [RelayCommand]
    private void Accept(AddressTermRow? row)
    {
        if (row is { IsSuggestion: true } && CanEdit)
        {
            row.IsSuggestion = false;
        }
    }

    [RelayCommand]
    private void Dismiss(AddressTermRow? row)
    {
        if (row is { IsSuggestion: true })
        {
            Rows.Remove(row);
        }
    }

    [RelayCommand(CanExecute = nameof(HasPending))]
    private void AcceptAll()
    {
        foreach (var row in Rows.Where(r => r.IsSuggestion).ToList())
        {
            Accept(row);
        }
    }

    /// <summary>Pede à IA os tratamentos que as falas mostram. A resposta entra como linhas esmaecidas, só dos pares que a tabela ainda não tem.</summary>
    [RelayCommand(CanExecute = nameof(CanSuggest))]
    private async Task SuggestAsync()
    {
        var roster = Roster();
        var token = Begin();
        try
        {
            // Editar um nome em Personagens só marca o pedido como velho: a leitura do texto inteiro roda aqui, fora da thread da tela, e só uma vez.
            var asked = requestsStale ? await RebuildAsync(roster, token).ConfigureAwait(true) : requests;
            var suggestions = await suggester()!.SuggestAddressTermsAsync(asked, roster, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            var added = Apply(suggestions);
            SuggestionNotice = SuggestionSummary(added);
        }
        catch (OperationCanceledException)
        {
            // A pessoa trocou de projeto no meio do pedido: nada a mostrar.
        }
        catch (Exception e)
        {
            SuggestionError = GlossaryViewModel.Explain(e);
        }
        finally
        {
            End(token);
        }
    }

    private static string SuggestionSummary(int added)
    {
        if (added == 0)
        {
            return "A IA não achou tratamento novo nas falas. Preencha a tabela à mão.";
        }

        var what = added == 1 ? "tratamento sugerido" : "tratamentos sugeridos";
        return $"{ProjectLabels.Number(added)} {what}. Aceite os que servirem.";
    }

    private int Apply(IReadOnlyList<AddressTermSuggestion> suggestions)
    {
        var added = 0;
        foreach (var s in suggestions)
        {
            // Um personagem removido durante o pedido não volta como par.
            if (!CharacterNames.Contains(s.From, CharacterBook.NameComparer) || !CharacterNames.Contains(s.To, CharacterBook.NameComparer)
                || Rows.Any(r => SamePair(r, s.From, s.To)))
            {
                continue;
            }

            Rows.Add(Watch(new AddressTermRow(s.From, s.To, s.Source, s.Target) { IsSuggestion = true }));
            added++;
        }

        return added;
    }

    private CancellationToken Begin()
    {
        Release();
        cts = new CancellationTokenSource();
        SuggestionError = null;
        SuggestionNotice = null;
        IsSuggesting = true;
        NotifySuggestionState();
        return cts.Token;
    }

    // Um pedido que perdeu para outro (ou para a troca de projeto) não mexe no estado do que está em andamento.
    private void End(CancellationToken token)
    {
        if (!token.IsCancellationRequested)
        {
            IsSuggesting = false;
            Release();
        }

        NotifySuggestionState();
    }

    private void Release()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    private List<AddressTermCharacter> Roster() =>
        [.. characters.Where(c => !string.IsNullOrWhiteSpace(c.Source)).Select(c => new AddressTermCharacter(c.Source.Trim(), string.IsNullOrWhiteSpace(c.Target) ? null : c.Target.Trim()))];

    private static List<AddressTermRequest> BuildRequests(IReadOnlyList<TextUnit> units, List<AddressTermCharacter> roster)
    {
        var people = roster.Select(c => new CharacterEntry(c.Name, c.Translation ?? string.Empty)).ToList();
        return people.Count > 1
            ? [.. AddressSamples.From(units, people).Select(p => new AddressTermRequest(p.Key, p.Value))]
            : [];
    }

    private void RebuildRequests()
    {
        requests = textLoaded ? BuildRequests(units, Roster()) : [];
        requestsStale = false;
        NotifySuggestionState();
    }

    private async Task<List<AddressTermRequest>> RebuildAsync(List<AddressTermCharacter> roster, CancellationToken token)
    {
        var text = units;
        var fresh = await Task.Run(() => BuildRequests(text, roster), token).ConfigureAwait(true);
        requests = fresh;
        requestsStale = false;
        return fresh;
    }

    private void OnCharactersChanged()
    {
        RefreshNames();
        if (textLoaded && !loading)
        {
            RebuildRequests();
        }
    }

    /// <summary>A pessoa editou o nome ou a tradução de um personagem: os seletores mostram o nome de agora e o pedido à IA é refeito quando for usado.</summary>
    public void CharactersEdited()
    {
        RefreshNames();
        requestsStale = true;
    }

    /// <summary>Refaz a lista de nomes para escolher, sem tirar o que já está escolhido de uma linha: só entra o que falta e sai o que sumiu.</summary>
    public void RefreshNames()
    {
        var names = characters.Select(c => c.Source.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        foreach (var gone in CharacterNames.Where(n => !names.Contains(n)).ToList())
        {
            CharacterNames.Remove(gone);
        }

        foreach (var name in names.Where(n => !CharacterNames.Contains(n)))
        {
            CharacterNames.Add(name);
        }
    }

    private AddressTermRow Watch(AddressTermRow row)
    {
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AddressTermRow.Problem) or nameof(AddressTermRow.HasProblem) or nameof(AddressTermRow.IsBlank))
            {
                return;
            }

            if (e.PropertyName == nameof(AddressTermRow.IsComplete))
            {
                OnPropertyChanged(nameof(PairCount));
                OnPropertyChanged(nameof(Summary));
                return;
            }

            if (e.PropertyName == nameof(AddressTermRow.IsSuggestion))
            {
                NotifyAll();
                Touch();
                return;
            }

            if (!row.IsSuggestion)
            {
                Touch();
            }
        };
        return row;
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NotifyAll();
        if (loading)
        {
            return;
        }

        var touched = (e.NewItems?.OfType<AddressTermRow>() ?? []).Concat(e.OldItems?.OfType<AddressTermRow>() ?? []).Any(r => !r.IsSuggestion);
        if (touched)
        {
            Touch();
        }
    }

    private void Touch()
    {
        if (!loading)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Refaz os preços em reais: a cotação mudou em Ajustes.</summary>
    public void RefreshPrices()
    {
        OnPropertyChanged(nameof(PriceText));
        OnPropertyChanged(nameof(SuggestHint));
    }

    /// <summary>A IA, o projeto ou a possibilidade de editar mudou: refaz o que os botões dependem.</summary>
    public void NotifySuggestionState()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanSuggest));
        OnPropertyChanged(nameof(PriceText));
        OnPropertyChanged(nameof(SuggestHint));
        SuggestCommand.NotifyCanExecuteChanged();
        AddCommand.NotifyCanExecuteChanged();
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(NoRows));
        OnPropertyChanged(nameof(PairCount));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(AcceptAllText));
        OnPropertyChanged(nameof(ProblemCount));
        AcceptAllCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>Uma linha da tabela de tratamentos: quem fala, com quem, o tratamento no original e na tradução.</summary>
public sealed partial class AddressTermRow : ObservableObject
{
    private string from;
    private string to;

    public AddressTermRow(string from = "", string to = "", string source = "", string target = "")
    {
        this.from = from;
        this.to = to;
        Source = source;
        Target = target;
    }

    /// <summary>Quem fala, pelo nome original do personagem. Um valor nulo (a lista de nomes mudou sob o seletor) não apaga a escolha.</summary>
    public string From
    {
        get => from;
        set
        {
            if (value is not null && SetProperty(ref from, value))
            {
                OnPropertyChanged(nameof(IsBlank));
                OnPropertyChanged(nameof(IsComplete));
            }
        }
    }

    /// <summary>Com quem fala, pelo nome original do personagem. Um valor nulo não apaga a escolha.</summary>
    public string To
    {
        get => to;
        set
        {
            if (value is not null && SetProperty(ref to, value))
            {
                OnPropertyChanged(nameof(IsBlank));
                OnPropertyChanged(nameof(IsComplete));
            }
        }
    }

    /// <summary>Como quem fala o chama no japonês (<c>美咲ちゃん</c>, <c>先輩</c>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBlank), nameof(IsComplete))]
    public partial string Source { get; set; }

    /// <summary>Como a tradução o chama (<c>Misaki-chan</c>, <c>senpai</c>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBlank), nameof(IsComplete))]
    public partial string Target { get; set; }

    /// <summary>Sugestão da IA esperando o aceite: aparece esmaecida e não vai para o arquivo.</summary>
    [ObservableProperty]
    public partial bool IsSuggestion { get; set; }

    /// <summary>Por que a linha ainda não foi para o arquivo; nulo quando está certa.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; set; }

    public bool HasProblem => Problem is not null;

    /// <summary>Linha sem nada escrito: não vai para o arquivo e não conta como problema.</summary>
    /// <summary>Tem o par e o tratamento nos dois idiomas: é o que vai para o arquivo.</summary>
    public bool IsComplete => !string.IsNullOrWhiteSpace(From) && !string.IsNullOrWhiteSpace(To) && !string.IsNullOrWhiteSpace(Source) && !string.IsNullOrWhiteSpace(Target);

    public bool IsBlank => string.IsNullOrWhiteSpace(From) && string.IsNullOrWhiteSpace(To) && string.IsNullOrWhiteSpace(Source) && string.IsNullOrWhiteSpace(Target);

    public AddressTerm ToTerm() => new(From.Trim(), To.Trim(), Source.Trim(), Target.Trim());
}
