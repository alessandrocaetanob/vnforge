using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core.Dictionaries;

namespace VNForge.App.ViewModels;

/// <summary>Um selo de condição: a chave do ícone (<c>Icon.Person</c>) e o texto.</summary>
public sealed record RuleChip(string Icon, string Text);

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
        ChipItems = rule.Conditions.Count == 0 ? [new RuleChip("Icon.Check", "Sempre")] : [.. rule.Conditions.Select(c => new RuleChip(ChipIcon(c), ChipText(c)))];
        Chips = [.. ChipItems.Select(c => c.Text)];
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

    /// <summary>Os mesmos selos com o ícone de cada um (pessoa, cena, trecho, "Sempre" com o visto), como no quadro da #539.</summary>
    public IReadOnlyList<RuleChip> ChipItems { get; }

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

    /// <summary>Quem a lista chama para abrir o diálogo desta regra e para apagá-la; o menu da linha os usa (o quadro da #539 não tem botões Editar e Apagar).</summary>
    internal Action<DictionaryRuleRow>? EditRequested { get; init; }

    internal Action<DictionaryRuleRow>? DeleteRequested { get; init; }

    [RelayCommand]
    private void Select() => selected?.Invoke(this);

    [RelayCommand]
    private void Edit()
    {
        selected?.Invoke(this);
        EditRequested?.Invoke(this);
    }

    [RelayCommand]
    private void Delete()
    {
        selected?.Invoke(this);
        DeleteRequested?.Invoke(this);
    }

    public static string ChipText(DictionaryCondition condition) => condition.Kind switch
    {
        DictionaryConditionKind.Speaker => $"Falante: {condition.Value}",
        DictionaryConditionKind.Scene => $"Cena: {condition.Value}",
        _ => $"Original contém {condition.Value}",
    };

    public static string ChipIcon(DictionaryCondition condition) => condition.Kind switch
    {
        DictionaryConditionKind.Speaker => "Icon.Person",
        DictionaryConditionKind.Scene => "Icon.Board",
        _ => "Icon.Search",
    };

    public static string LinesLabel(int count) => count == 1 ? "1 linha" : $"{count.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))} linhas";
}
