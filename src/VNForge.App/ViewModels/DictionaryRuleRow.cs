using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core.Dictionaries;

namespace VNForge.App.ViewModels;

/// <summary>Uma regra na lista do pré ou do pós-dicionário: de e para, as condições como selos e quantas linhas ela alcança.</summary>
public sealed partial class DictionaryRuleRow : ObservableObject
{
    private readonly Action<DictionaryRuleRow>? changed;

    public DictionaryRuleRow(DictionarySide side, int index, DictionaryRule rule, Action<DictionaryRuleRow>? changed = null, Action<DictionaryRuleRow>? selected = null)
    {
        Side = side;
        Index = index;
        Rule = rule;
        this.changed = changed;
        this.selected = selected;
        enabled = rule.Enabled;
        Chips = rule.Conditions.Count == 0 ? ["Sempre"] : [.. rule.Conditions.Select(ChipText)];
    }

    private readonly Action<DictionaryRuleRow>? selected;

    private bool enabled;

    public DictionarySide Side { get; }

    /// <summary>Posição da regra na lista do lado dela, a partir de zero.</summary>
    public int Index { get; }

    public DictionaryRule Rule { get; private set; }

    /// <summary>Troca a regra por uma cópia (liga/desliga) sem refazer a linha da lista.</summary>
    internal void UpdateRule(DictionaryRule rule) => Rule = rule;

    public string Pattern => Rule.Pattern;

    public string With => Rule.With;

    /// <summary>Os selos: "Sempre" sem condição, senão um por condição ("Falante: Ren", "Cena: Capítulo 1", "Original contém 先生").</summary>
    public IReadOnlyList<string> Chips { get; }

    /// <summary>Ligada ou desligada; mudar grava o <c>formatting.json</c> na hora.</summary>
    public bool Enabled
    {
        get => enabled;
        set
        {
            if (SetProperty(ref enabled, value))
            {
                changed?.Invoke(this);
            }
        }
    }

    /// <summary>"38 linhas", ou "..." enquanto o projeto é contado.</summary>
    [ObservableProperty]
    public partial string LinesText { get; set; } = "…";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => selected?.Invoke(this);

    public static string ChipText(DictionaryCondition condition) => condition.Kind switch
    {
        DictionaryConditionKind.Speaker => $"Falante: {condition.Value}",
        DictionaryConditionKind.Scene => $"Cena: {condition.Value}",
        _ => $"Original contém {condition.Value}",
    };

    public static string LinesLabel(int count) => count == 1 ? "1 linha" : $"{count.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))} linhas";
}
