using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Spelling;

namespace VNForge.App.ViewModels;

/// <summary>As palavras do campo da tradução que o dicionário pt-BR não reconhece, para o campo sublinhá-las enquanto a pessoa digita.</summary>
public sealed partial class LineDetailViewModel
{
    private ProjectDictionary? spellWords;
    private bool spellWarmUpRequested;

    /// <summary>
    /// O dicionário do projeto (Glossário e personagens). Só com ele o painel confere a ortografia: sem ele <see cref="Misspellings"/> fica vazio.
    /// O Editor o entrega ao criar o painel e de novo quando o Glossário ou os personagens mudam; trocar o dicionário refaz os trechos.
    /// </summary>
    public ProjectDictionary? SpellWords
    {
        get => spellWords;
        set
        {
            spellWords = value;
            IgnoreWordCommand.NotifyCanExecuteChanged();
            UpdateMisspellings();
        }
    }

    /// <summary>Os trechos de <see cref="FieldText"/> a sublinhar, no índice do texto do campo. Vazio enquanto o dicionário ainda é lido.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<QaSpan> Misspellings { get; private set; } = [];

    /// <summary>Avisa o Editor que uma palavra entrou em <c>ignored-words.json</c>, para ele refazer o QA das outras linhas.</summary>
    public Action? WordIgnored { get; init; }

    /// <summary>O trecho sublinhado que cobre o <paramref name="offset"/> do texto do campo (a borda direita conta, onde o cursor para ao clicar), ou nulo.</summary>
    public QaSpan? MisspellingAt(int offset)
    {
        foreach (var span in Misspellings)
        {
            if (offset >= span.Start && offset <= span.Start + span.Length)
            {
                return span;
            }
        }

        return null;
    }

    /// <summary>
    /// "Ignorar esta palavra no projeto": grava <paramref name="word"/> em <c>ignored-words.json</c>, tira o sublinhado desta linha na hora e avisa o Editor,
    /// que refaz o resto. Se não deu para gravar, a palavra continua marcada e o motivo vai para <see cref="ErrorMessage"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanIgnoreWord))]
    private void IgnoreWord(string? word)
    {
        if (string.IsNullOrWhiteSpace(word) || ProjectRoot is not { } root)
        {
            return;
        }

        try
        {
            ProjectFolder.Open(root).AddIgnoredWord(word);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Não foi possível ignorar a palavra \"{word}\": {e.Message}";
            return;
        }

        SpellWords = (spellWords ?? ProjectDictionary.Empty).WithIgnoredWord(word);
        WordIgnored?.Invoke();
    }

    private bool CanIgnoreWord() => ProjectRoot is not null && spellWords is not null;

    private void UpdateMisspellings()
    {
        if (spellWords is null || disposed)
        {
            return;
        }

        if (!SpellChecker.IsPortugueseReady)
        {
            RequestSpellWarmUp();
            return;
        }

        var found = SpellChecker.Portuguese.Find(FieldText, spellWords);

        // Só avisa o campo quando os trechos mudam: cada tecla chega aqui e a maioria não muda nada.
        if (!found.SequenceEqual(Misspellings))
        {
            Misspellings = found;
        }
    }

    // A primeira leitura do dicionário leva cerca de um segundo: fora da thread da interface, e o sublinhado aparece quando ela termina.
    private void RequestSpellWarmUp()
    {
        if (spellWarmUpRequested)
        {
            return;
        }

        spellWarmUpRequested = true;
        _ = SpellChecker.WarmUpAsync().ContinueWith(
            task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    postToUi(UpdateMisspellings);
                }
                else
                {
                    // Dicionário ilegível: o sublinhado fica desligado, e a próxima edição tenta ler de novo.
                    spellWarmUpRequested = false;
                }
            },
            TaskScheduler.Default);
    }
}
