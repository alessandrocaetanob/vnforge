using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>Uma opção do seletor de falante da linha. <see cref="Value"/> é o <see cref="TextUnit.ChosenSpeaker"/> que ela grava: nulo volta ao falante do jogo, vazio é Narração.</summary>
public sealed record SpeakerChoice(string Label, string? Value);

public sealed partial class LineDetailViewModel
{
    private IReadOnlyList<SpeakerChoice>? speakerChoices;

    /// <summary>Os personagens do Glossário, que o seletor oferece; vazio quando o Editor não os carregou.</summary>
    public IReadOnlyCollection<CharacterEntry> Characters { get; init; } = [];

    /// <summary>Refaz o contexto da linha (nome em português, voz, gênero) para um falante; o Editor o fornece. Sem ele o contexto fica como estava.</summary>
    public Func<string?, LineContext?>? ContextForSpeaker { get; init; }

    /// <summary>O que o seletor oferece: o falante do jogo, Narração e cada personagem do Glossário.</summary>
    public IReadOnlyList<SpeakerChoice> SpeakerChoices => speakerChoices ??= BuildSpeakerChoices();

    /// <summary>A opção que vale para a linha; nula quando o falante é um nome digitado que não é de nenhum personagem do Glossário.</summary>
    public SpeakerChoice? SelectedSpeakerChoice
    {
        get => SpeakerChoices.FirstOrDefault(choice => SameChoice(choice.Value, Saved.ChosenSpeaker));
        set
        {
            if (value is not null && !SameChoice(value.Value, Saved.ChosenSpeaker))
            {
                ChooseSpeaker(value.Value);
            }
        }
    }

    /// <summary>O nome que o texto extraído traz como falante; "Narração" quando não há.</summary>
    public string GameSpeakerLabel => string.IsNullOrEmpty(Saved.Speaker) ? NarrationLabel : Saved.Speaker;

    /// <summary>Verdadeiro quando a linha tem o falante trocado por quem traduz.</summary>
    public bool IsSpeakerChanged => Saved.ChosenSpeaker is not null;

    /// <summary>"Falante: 美咲 (do jogo)" ou "Falante trocado: Misaki (no jogo: 美咲)".</summary>
    public string SpeakerSummary => Saved.ChosenSpeaker switch
    {
        null => $"Falante: {GameSpeakerLabel} (do jogo)",
        { Length: 0 } => $"Falante trocado: Narração (no jogo: {GameSpeakerLabel})",
        var name => $"Falante trocado: {name} (no jogo: {GameSpeakerLabel})",
    };

    /// <summary>O nome digitado no seletor, para um falante que não está no Glossário.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseSpeakerDraftCommand))]
    public partial string SpeakerDraft { get; set; } = string.Empty;

    [RelayCommand(CanExecute = nameof(CanUseSpeakerDraft))]
    private void UseSpeakerDraft()
    {
        if (ChooseSpeaker(SpeakerDraft.Trim()))
        {
            SpeakerDraft = string.Empty;
        }
    }

    private bool CanUseSpeakerDraft() => !string.IsNullOrWhiteSpace(SpeakerDraft);

    /// <summary>Volta a linha ao falante do jogo.</summary>
    [RelayCommand]
    private void UseGameSpeaker() => ChooseSpeaker(null);

    /// <summary>
    /// Grava o falante escolhido da linha (nulo volta ao do jogo, vazio é Narração) e atualiza o contexto, a prévia e a grade. Grava antes o que estiver pendente
    /// na tradução. Falso (com <see cref="ErrorMessage"/>) quando não deu para gravar; a linha fica como estava.
    /// </summary>
    private bool ChooseSpeaker(string? speaker)
    {
        if (!Flush())
        {
            NotifySpeakerChanged();
            return false;
        }

        var saved = TrySave(() =>
        {
            Saved = editor.SetChosenSpeaker(scriptKey, unit.Id, speaker);
            Context = ContextForSpeaker?.Invoke(Saved.EffectiveSpeaker) ?? Context;
        });

        if (saved && Preview is { } preview)
        {
            Preview = new TextboxPreviewViewModel(Saved, preview.Profile, Translation, preview.Marks, preview.Naming);
        }

        NotifySpeakerChanged();
        return saved;
    }

    private void NotifySpeakerChanged()
    {
        foreach (var name in new[]
        {
            nameof(Speaker), nameof(HasSpeaker), nameof(SpeakerTarget), nameof(SpeakerColor), nameof(SpeakerPair), nameof(Voice), nameof(HasVoice), nameof(SpeakerName), nameof(VoiceText),
            nameof(GameSpeakerLabel), nameof(IsSpeakerChanged), nameof(SpeakerSummary), nameof(SelectedSpeakerChoice),
        })
        {
            OnPropertyChanged(name);
        }
    }

    private List<SpeakerChoice> BuildSpeakerChoices()
    {
        var choices = new List<SpeakerChoice> { new($"Falante do jogo ({GameSpeakerLabel})", null), new("Narração", string.Empty) };
        choices.AddRange(Characters.Select(c => new SpeakerChoice(string.IsNullOrWhiteSpace(c.Target) ? c.Source : $"{c.Target} · {c.Source}", c.Source)));
        return choices;
    }

    private static bool SameChoice(string? a, string? b) =>
        a is null || b is null ? a is null && b is null : CharacterBook.NameComparer.Equals(a, b);

    private const string NarrationLabel = "Narração";
}
