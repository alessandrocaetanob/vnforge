using CommunityToolkit.Mvvm.ComponentModel;
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
            UpdateMisspellings();
        }
    }

    /// <summary>Os trechos de <see cref="FieldText"/> a sublinhar, no índice do texto do campo. Vazio enquanto o dicionário ainda é lido.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<QaSpan> Misspellings { get; private set; } = [];

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
