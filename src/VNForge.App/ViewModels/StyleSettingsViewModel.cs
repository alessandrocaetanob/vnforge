using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>Um cartão de escolha da aba Estilo: nome, um exemplo curto e a explicação. A base não depende do tipo da escolha, para o cartão ter um só modelo de tela.</summary>
public abstract partial class StyleOption(string name, string sample, string description) : ObservableObject
{
    public string Name { get; } = name;

    /// <summary>Como o texto fica com a escolha ("Misaki-chan, senpai").</summary>
    public string Sample { get; } = sample;

    public string Description { get; } = description;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>Um cartão de escolha que carrega o valor da escolha.</summary>
public sealed class StyleOption<T>(T value, string name, string sample, string description) : StyleOption(name, sample, description)
{
    public T Value { get; } = value;
}

/// <summary>
/// A aba Estilo dos Ajustes: honoríficos, tratamento, narrador, onomatopeias, ordem dos nomes e delimitador da fala (<see cref="StyleConfig"/>, em
/// <c>vnforge.json</c>) e o exemplo que muda conforme as escolhas. O guia de estilo em texto livre (<c>style.md</c>) continua no
/// <see cref="SettingsViewModel"/>, que grava os dois arquivos.
/// </summary>
public sealed partial class StyleSettingsViewModel : ObservableObject
{
    /// <summary>A fala do exemplo, no original; não muda com as escolhas.</summary>
    public const string ExampleSource = "「美咲ちゃん、先輩が呼んでるよ。ドキドキしない？」";

    /// <summary>De onde sai a fala do exemplo, para a pessoa reconhecer a cena.</summary>
    public const string ExampleWhere = "Sala de aula · linha 412 · Misaki";

    private bool loading;

    public StyleSettingsViewModel()
    {
        Honorifics =
        [
            new(HonorificsPolicy.Keep, "Manter", "Misaki-chan, senpai", "Para quem conhece anime. Fica estranho para quem não conhece."),
            new(HonorificsPolicy.Remove, "Remover", "Misaki, veterano", "Some tudo, inclusive o respeito que -san carrega."),
            new(HonorificsPolicy.Adapt, "Adaptar", "Misaki; \"senhor\" só se for formal", "-chan e -kun saem; -san vira \"senhor\" ou \"senhora\" em fala formal; senpai segue o glossário."),
        ];
        Addresses =
        [
            new(AddressForm.Voce, "Você", "Você vai se molhar", "Padrão do Brasil inteiro em diálogo."),
            new(AddressForm.Tu, "Tu", "Tu vais te molhar", "Sotaque regional; combine com a conjugação no guia de estilo."),
        ];
        NarratorPersons = [new(NarratorPerson.First, "Primeira"), new(NarratorPerson.Third, "Terceira")];
        NarratorGenders = [new(NarratorGender.Male, "Masculino"), new(NarratorGender.Female, "Feminino"), new(NarratorGender.Neutral, "Neutro")];
        SpeakerDelimiters = [new(SpeakerDelimiterStyle.None, "Sem sinal"), new(SpeakerDelimiterStyle.Guillemets, "« »")];

        Watch(Honorifics, o => SelectedHonorific = o);
        Watch(Addresses, o => SelectedAddress = o);
        Load(StyleConfig.Default);
    }

    /// <summary>Uma escolha mudou por ação da pessoa (não ao carregar o projeto): os Ajustes gravam o <c>vnforge.json</c>.</summary>
    public event EventHandler? Changed;

    public ReadOnlyCollection<StyleOption<HonorificsPolicy>> Honorifics { get; }

    public ReadOnlyCollection<StyleOption<AddressForm>> Addresses { get; }

    public ReadOnlyCollection<ChoiceItem<NarratorPerson>> NarratorPersons { get; }

    public ReadOnlyCollection<ChoiceItem<NarratorGender>> NarratorGenders { get; }

    /// <summary>Como a fala com falante aparece na caixa de texto com a acentuação <c>cp1252</c> (#453).</summary>
    public ReadOnlyCollection<ChoiceItem<SpeakerDelimiterStyle>> SpeakerDelimiters { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExampleTranslation))]
    public partial StyleOption<HonorificsPolicy>? SelectedHonorific { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExampleTranslation))]
    public partial StyleOption<AddressForm>? SelectedAddress { get; set; }

    /// <summary>Quem narra, em texto livre; vazio quando a pessoa não disse.</summary>
    [ObservableProperty]
    public partial string NarratorWho { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ChoiceItem<NarratorPerson>? SelectedNarratorPerson { get; set; }

    [ObservableProperty]
    public partial ChoiceItem<NarratorGender>? SelectedNarratorGender { get; set; }

    [ObservableProperty]
    public partial ChoiceItem<SpeakerDelimiterStyle>? SelectedSpeakerDelimiter { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExampleTranslation))]
    public partial bool TranslateOnomatopoeia { get; set; }

    [ObservableProperty]
    public partial bool WesternNameOrder { get; set; }

    /// <summary>Trocar a pontuação japonesa pela do teclado na tradução da IA e na que a pessoa grava.</summary>
    [ObservableProperty]
    public partial bool KeyboardPunctuation { get; set; }

    /// <summary>A fala do exemplo como a IA vai traduzir com as escolhas de hoje.</summary>
    public string ExampleTranslation => ComposeExample(ToConfig());

    /// <summary>Põe na tela as escolhas do projeto, sem avisar <see cref="Changed"/>.</summary>
    public void Load(StyleConfig style)
    {
        ArgumentNullException.ThrowIfNull(style);
        loading = true;
        try
        {
            SelectedHonorific = Honorifics.First(o => o.Value == style.Honorifics);
            SelectedAddress = Addresses.First(o => o.Value == style.Address);
            NarratorWho = style.Narrator.Who ?? string.Empty;
            SelectedNarratorPerson = NarratorPersons.First(o => o.Value == style.Narrator.Person);
            SelectedNarratorGender = NarratorGenders.First(o => o.Value == style.Narrator.Gender);
            TranslateOnomatopoeia = style.TranslateOnomatopoeia;
            WesternNameOrder = style.WesternNameOrder;
            KeyboardPunctuation = style.KeyboardPunctuation;
            SelectedSpeakerDelimiter = SpeakerDelimiters.First(o => o.Value == style.SpeakerDelimiter);
        }
        finally
        {
            loading = false;
        }

        OnPropertyChanged(nameof(ExampleTranslation));
    }

    /// <summary>As escolhas da tela como <see cref="StyleConfig"/>.</summary>
    public StyleConfig ToConfig() => new()
    {
        Honorifics = SelectedHonorific?.Value ?? HonorificsPolicy.Adapt,
        Address = SelectedAddress?.Value ?? AddressForm.Voce,
        Narrator = new NarratorStyle
        {
            Who = string.IsNullOrWhiteSpace(NarratorWho) ? null : NarratorWho,
            Person = SelectedNarratorPerson?.Value ?? NarratorPerson.First,
            Gender = SelectedNarratorGender?.Value ?? NarratorGender.Neutral,
        },
        TranslateOnomatopoeia = TranslateOnomatopoeia,
        WesternNameOrder = WesternNameOrder,
        KeyboardPunctuation = KeyboardPunctuation,
        SpeakerDelimiter = SelectedSpeakerDelimiter?.Value ?? StyleConfig.Default.SpeakerDelimiter,
    };

    /// <summary>
    /// A fala de <see cref="ExampleSource"/> traduzida com as escolhas de <paramref name="style"/>: o honorífico (ou o veterano), o
    /// tratamento e a onomatopeia mudam; o resto da frase é fixo.
    /// </summary>
    public static string ComposeExample(StyleConfig style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var keep = style.Honorifics == HonorificsPolicy.Keep;
        var name = keep ? "Misaki-chan" : "Misaki";
        var senpai = keep ? "o senpai" : "o veterano";
        var beat = style.TranslateOnomatopoeia ? "O coração não dispara?" : "Não fica doki doki?";
        var nervous = style.Address == AddressForm.Tu ? " Não ficas nervosa?" : string.Empty;
        return $"{name}, {senpai} está te chamando. {beat}{nervous}";
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (loading || e.PropertyName is nameof(ExampleTranslation) or null)
        {
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Um clique no cartão (<c>IsSelected</c>) escolhe a opção, como nos cartões de modelo e de acentuação.</summary>
    private static void Watch<T>(ReadOnlyCollection<StyleOption<T>> options, Action<StyleOption<T>> select)
    {
        foreach (var option in options)
        {
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(StyleOption.IsSelected) && option.IsSelected)
                {
                    select(option);
                }
            };
        }
    }

    partial void OnSelectedHonorificChanged(StyleOption<HonorificsPolicy>? value) => Mark(Honorifics, value);

    partial void OnSelectedAddressChanged(StyleOption<AddressForm>? value) => Mark(Addresses, value);

    private static void Mark<T>(ReadOnlyCollection<StyleOption<T>> options, StyleOption<T>? selected)
    {
        foreach (var option in options)
        {
            option.IsSelected = option == selected;
        }
    }
}
