using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Jmdict;

namespace VNForge.App.ViewModels;

/// <summary>O que o cartão do dicionário mostra (#538): a entrada, o aviso de download, o andamento ou o erro.</summary>
public enum WordCardMode
{
    None,
    Entry,
    Offer,
    Installing,
    Failed,
}

/// <summary>Um sentido numerado do cartão: "1" e "atraso, chegar tarde".</summary>
public sealed record WordSenseRow(int Number, string Text);

/// <summary>Outra entrada para a mesma grafia (homógrafo), mostrada quando o cartão se abre inteiro.</summary>
public sealed record WordOtherEntryRow(string Title, string Tag, IReadOnlyList<WordSenseRow> Senses);

public sealed partial class LineDetailViewModel
{
    /// <summary>Quantos sentidos o cartão mostra antes do "Mais N sentidos".</summary>
    public const int WordSensesShown = 4;


    private OfflineDictionary? dictionary;
    private JmdictMatch? shownWord;
    private string wordSurface = string.Empty;
    private int hoverIndex = -1;
    private int probedIndex = -1;
    private JmdictMatch? probedMatch;

    /// <summary>Onde está a licença do JMdict, para a atribuição que a licença exige.</summary>
    public const string JmdictLicenseUrl = JmdictSource.LicenseUrl;

    /// <summary>O dicionário offline do app; nulo quando o Editor não o montou (projeto de exemplo, testes). Sem ele o mouse sobre o original não faz nada.</summary>
    public OfflineDictionary? Dictionary
    {
        get => dictionary;
        init
        {
            dictionary = value;
            if (value is not null)
            {
                value.Changed += OnDictionaryChanged;
            }
        }
    }

    /// <summary>"Adicionar ao glossário": leva a palavra (como está no original) ao Glossário, numa linha nova para a pessoa só escrever a tradução. Nulo fora do app.</summary>
    public Action<string>? GlossaryTermStarter { get; init; }

    /// <summary>O original em texto corrido, na mesma indexação do que a tela desenha: cada marcador do jogo (chip) ocupa uma posição, que não é palavra.</summary>
    public string DictionarySource
    {
        get
        {
            var text = new StringBuilder();
            foreach (var segment in SourceSegments)
            {
                text.Append(segment.IsMarker ? "￼" : segment.Text);
            }

            return text.ToString();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWordCard), nameof(IsEntryCard), nameof(IsOfferCard), nameof(IsInstallingCard), nameof(IsFailedCard))]
    [NotifyCanExecuteChangedFor(nameof(CloseWordCardCommand))]
    public partial WordCardMode WordCardMode { get; private set; }

    public bool ShowWordCard => WordCardMode != WordCardMode.None;

    public bool IsEntryCard => WordCardMode == WordCardMode.Entry;

    public bool IsOfferCard => WordCardMode == WordCardMode.Offer;

    public bool IsInstallingCard => WordCardMode == WordCardMode.Installing;

    public bool IsFailedCard => WordCardMode == WordCardMode.Failed;

    /// <summary>Onde a palavra começa no <see cref="DictionarySource"/>; -1 sem palavra sob o mouse. A tela pinta o fundo e o traço da palavra.</summary>
    [ObservableProperty]
    public partial int WordStart { get; private set; } = -1;

    [ObservableProperty]
    public partial int WordLength { get; private set; }

    /// <summary>A forma de dicionário: 行く quando o texto traz 行こう.</summary>
    [ObservableProperty]
    public partial string WordText { get; private set; } = string.Empty;

    /// <summary>As leituras separadas por " · " ("いく · ゆく"); vazio quando a palavra já está escrita só em kana.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWordReading))]
    public partial string WordReading { get; private set; } = string.Empty;

    public bool HasWordReading => WordReading.Length > 0;

    /// <summary>As classes gramaticais do JMdict em selo ("n, vs", "v5k-s, vi").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWordTag))]
    public partial string WordTag { get; private set; } = string.Empty;

    public bool HasWordTag => WordTag.Length > 0;

    /// <summary>"行こう é a forma volitiva de 行く"; nulo quando o trecho já é a forma de dicionário.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWordNote))]
    public partial string? WordNote { get; private set; }

    public bool HasWordNote => !string.IsNullOrEmpty(WordNote);

    public ObservableCollection<WordSenseRow> WordSenses { get; } = [];

    public ObservableCollection<WordOtherEntryRow> WordOthers { get; } = [];

    /// <summary>"Mais 6 sentidos · 2 outras entradas": o que ficou de fora do cartão fechado. Clicar abre tudo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWordMore))]
    public partial string? WordMore { get; private set; }

    public bool HasWordMore => !IsWordExpanded && !string.IsNullOrEmpty(WordMore);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWordMore), nameof(HasWordOthers))]
    public partial bool IsWordExpanded { get; private set; }

    public bool HasWordOthers => IsWordExpanded && WordOthers.Count > 0;

    /// <summary>A tradução fixada no glossário do projeto para esta palavra; nulo quando o termo não está lá.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InGlossary), nameof(CanAddWordToGlossary), nameof(GlossaryChipText))]
    [NotifyCanExecuteChangedFor(nameof(AddWordToGlossaryCommand))]
    public partial string? WordGlossaryTarget { get; private set; }

    public bool InGlossary => WordGlossaryTarget is not null;

    public string GlossaryChipText => $"No glossário: {WordGlossaryTarget}";

    public bool CanAddWordToGlossary => GlossaryTermStarter is not null && !InGlossary;

    /// <summary>O rodapé do cartão: o nome do dicionário e da entidade que o mantém (a licença exige a atribuição à vista).</summary>
    public const string WordFootnote = "JMdict · offline · EDRDG";

    /// <summary>A licença e a versão, na dica do rodapé.</summary>
    public string WordFootnoteTip => $"JMdict, de {Dictionary?.Version}. © Electronic Dictionary Research and Development Group (EDRDG), licença CC BY-SA. {JmdictLicenseUrl}";

    /// <summary>O dicionário passou do prazo de atualização da licença: o rodapé oferece baixar de novo.</summary>
    public bool WordNeedsUpdate => IsEntryCard && Dictionary is { IsStale: true };

    /// <summary>"Baixando o dicionário · 42%" ou, sem o tamanho, "Montando o dicionário...".</summary>
    public string InstallStatusText => Dictionary is { Progress: >= 0 and < 1 } d ? $"Baixando o dicionário · {(int)(d.Progress * 100)}%" : "Montando o dicionário...";

    public double InstallProgress => Dictionary is { Progress: >= 0 } d ? d.Progress * 100 : 0;

    public bool InstallIsIndeterminate => Dictionary is not { Progress: >= 0 };

    public string? InstallProblem => Dictionary?.Problem;

    public static string OfferText => $"Consulta qualquer palavra do original, sem rede e sem custo. O VNForge baixa o JMdict (cerca de {JmdictInstaller.ApproximateSize}, do EDRDG) uma vez, na sua pasta de configuração.";

    /// <summary>
    /// Chamado pela tela quando o mouse está sobre o <paramref name="index"/>-ésimo caractere do original (ou -1 ao sair). Com o dicionário pronto abre a entrada da palavra
    /// que cobre o caractere; sem o dicionário instalado oferece o download uma vez. Sobre pontuação ou o que o JMdict não tem, nada muda.
    /// </summary>
    public void HoverSource(int index)
    {
        hoverIndex = index;
        if (disposed || dictionary is not { } d || index < 0 || WordCardMode is WordCardMode.Installing)
        {
            return;
        }

        switch (d.State)
        {
            case OfflineDictionaryState.Ready:
                ShowEntryAt(d, index);
                break;
            case OfflineDictionaryState.NotInstalled when !d.OfferDismissed:
                OfferAt(index, WordCardMode.Offer);
                break;
            case OfflineDictionaryState.Failed when WordCardMode == WordCardMode.None:
                OfferAt(index, WordCardMode.Failed);
                break;
            default:
                break;
        }
    }

    private void OfferAt(int index, WordCardMode mode)
    {
        if (!IsJapaneseLetter(DictionarySource, index))
        {
            return;
        }

        ClearWord();
        WordCardMode = mode;
        OnPropertyChanged(nameof(InstallProblem));
    }

    private static bool IsJapaneseLetter(string text, int index) =>
        index < text.Length && text[index] is (>= '぀' and <= 'ヿ') or (>= '㐀' and <= '鿿') or (>= '豈' and <= '﫿') or (>= 'ｦ' and <= 'ﾟ');

    private void ShowEntryAt(OfflineDictionary d, int index)
    {
        var text = DictionarySource;
        if (IsEntryCard && index >= WordStart && index < WordStart + WordLength)
        {
            return;
        }

        // O mouse manda dezenas de movimentos por segundo sobre o mesmo caractere: o último resultado, achado ou não, vale até o caractere mudar.
        var match = index == probedIndex ? probedMatch : d.FindWordAt(text, index);
        probedIndex = index;
        probedMatch = match;
        if (match is null)
        {
            return;
        }

        shownWord = match;
        wordSurface = text.Substring(match.Start, match.Length);
        Fill(match);
        WordStart = match.Start;
        WordLength = match.Length;
        WordCardMode = WordCardMode.Entry;
        OnPropertyChanged(nameof(WordNeedsUpdate));
        OnPropertyChanged(nameof(WordFootnoteTip));
    }

    private void Fill(JmdictMatch match)
    {
        var entry = match.Main;
        WordText = match.Word;
        WordReading = ReadingsOf(entry, match.Word);
        WordTag = string.Join(", ", entry.PartsOfSpeech);
        WordNote = match.IsInflected ? $"{wordSurface} é a forma {JoinReasons(match.Inflection)} de {match.Word}" : null;
        IsWordExpanded = false;
        WordSenses.Clear();
        foreach (var row in entry.Senses.Take(WordSensesShown).Select((s, i) => new WordSenseRow(i + 1, s.GlossText)))
        {
            WordSenses.Add(row);
        }

        WordOthers.Clear();
        foreach (var other in match.Entries.Skip(1))
        {
            WordOthers.Add(new WordOtherEntryRow(TitleOf(other), string.Join(", ", other.PartsOfSpeech), [.. other.Senses.Select((s, i) => new WordSenseRow(i + 1, s.GlossText))]));
        }

        WordMore = MoreText(entry.Senses.Count - WordSensesShown, WordOthers.Count);
        WordGlossaryTarget = (Context?.Glossary ?? []).FirstOrDefault(t => t.Source == wordSurface || t.Source == match.Word)?.Target;
    }

    private static string ReadingsOf(JmdictEntry entry, string word)
    {
        var readings = entry.Readings.Where(r => r != word).ToList();
        return string.Join(" · ", readings);
    }

    private static string TitleOf(JmdictEntry entry)
    {
        var reading = entry.Readings.Count > 0 ? entry.Readings[0] : string.Empty;
        if (entry.Kanji.Count == 0)
        {
            return reading;
        }

        return reading.Length > 0 ? $"{entry.Kanji[0]} · {reading}" : entry.Kanji[0];
    }

    private static string? MoreText(int hiddenSenses, int others)
    {
        var parts = new List<string>();
        if (hiddenSenses > 0)
        {
            parts.Add(hiddenSenses == 1 ? "Mais 1 sentido" : $"Mais {hiddenSenses} sentidos");
        }

        if (others > 0)
        {
            parts.Add(others == 1 ? "1 outra entrada" : $"{others} outras entradas");
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string JoinReasons(IReadOnlyList<string> reasons) =>
        reasons.Count == 1 ? reasons[0] : $"{string.Join(", ", reasons.Take(reasons.Count - 1))} e {reasons[^1]}";

    private void ClearWord()
    {
        shownWord = null;
        WordStart = -1;
        WordLength = 0;
        WordSenses.Clear();
        WordOthers.Clear();
        WordMore = null;
        WordNote = null;
        WordGlossaryTarget = null;
    }

    /// <summary>Fecha o cartão e tira o realce da palavra. O mouse saindo do original e do cartão, o Esc e o clique em outro lugar chegam aqui.</summary>
    [RelayCommand(CanExecute = nameof(ShowWordCard))]
    public void CloseWordCard()
    {
        if (WordCardMode is WordCardMode.Installing)
        {
            // Com o download em andamento o Esc cancela, porque o cartão é a única porta para ele.
            dictionary?.CancelInstall();
            return;
        }

        WordCardMode = WordCardMode.None;
        ClearWord();
    }

    /// <summary>O mouse saiu do original e do cartão: fecha, menos durante o download, que só termina, falha ou é cancelado.</summary>
    public void LeaveWordCard()
    {
        if (WordCardMode is not WordCardMode.Installing)
        {
            CloseWordCard();
        }
    }

    /// <summary>"Cancelar" do cartão em andamento: interrompe o download; o dicionário que já havia continua valendo.</summary>
    [RelayCommand]
    private void CancelDictionaryInstall() => dictionary?.CancelInstall();

    /// <summary>"Mais N sentidos": abre todos os sentidos da entrada e as outras entradas da mesma grafia.</summary>
    [RelayCommand]
    private void ExpandWord()
    {
        if (shownWord is not { } match)
        {
            return;
        }

        WordSenses.Clear();
        foreach (var row in match.Main.Senses.Select((s, i) => new WordSenseRow(i + 1, s.GlossText)))
        {
            WordSenses.Add(row);
        }

        IsWordExpanded = true;
    }

    [RelayCommand(CanExecute = nameof(CanAddWordToGlossary))]
    private void AddWordToGlossary()
    {
        if (!string.IsNullOrEmpty(wordSurface))
        {
            GlossaryTermStarter?.Invoke(wordSurface);
        }
    }

    /// <summary>"Baixar o dicionário" do aviso, e "Atualizar" do rodapé quando o dicionário passou do prazo.</summary>
    [RelayCommand]
    private async Task InstallDictionaryAsync()
    {
        if (dictionary is not { } d)
        {
            return;
        }

        ClearWord();
        WordCardMode = WordCardMode.Installing;
        try
        {
            await d.InstallAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // O serviço já trata o cancelamento; isto só garante que um cancelamento perdido nunca chegue à interface como exceção.
            OnDictionaryChanged(d, EventArgs.Empty);
        }
    }

    /// <summary>"Agora não": o aviso some e não volta até o app abrir outra vez.</summary>
    [RelayCommand]
    private void DismissDictionaryOffer()
    {
        dictionary?.DismissOffer();
        CloseWordCard();
    }

    private void OnDictionaryChanged(object? sender, EventArgs e)
    {
        if (disposed)
        {
            return;
        }

        OnPropertyChanged(nameof(InstallStatusText));
        OnPropertyChanged(nameof(InstallProgress));
        OnPropertyChanged(nameof(InstallIsIndeterminate));
        OnPropertyChanged(nameof(InstallProblem));
        OnPropertyChanged(nameof(WordNeedsUpdate));
        if (dictionary is not { } d || WordCardMode is not (WordCardMode.Installing or WordCardMode.Offer or WordCardMode.Failed))
        {
            return;
        }

        if (d.State == OfflineDictionaryState.Installing)
        {
            WordCardMode = WordCardMode.Installing;
        }
        else if (WordCardMode is WordCardMode.Installing)
        {
            FinishInstall(d);
        }
    }

    /// <summary>
    /// O download acabou (ou falhou, ou foi cancelado): com erro o cartão o mostra, mesmo que o dicionário velho continue valendo; sem erro e com o dicionário pronto abre a entrada
    /// da palavra sob o mouse; cancelado sem dicionário nenhum, o cartão fecha.
    /// </summary>
    private void FinishInstall(OfflineDictionary d)
    {
        probedIndex = -1;
        if (d.Problem is not null)
        {
            WordCardMode = WordCardMode.Failed;
        }
        else if (d.State == OfflineDictionaryState.Ready)
        {
            WordCardMode = WordCardMode.None;
            HoverSource(hoverIndex);
        }
        else
        {
            WordCardMode = WordCardMode.None;
        }
    }

    private void DisposeDictionary()
    {
        if (dictionary is not null)
        {
            dictionary.Changed -= OnDictionaryChanged;
        }
    }
}
