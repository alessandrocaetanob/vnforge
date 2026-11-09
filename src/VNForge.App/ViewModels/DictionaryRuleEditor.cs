using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core.Dictionaries;

namespace VNForge.App.ViewModels;

/// <summary>Uma condição no diálogo da regra: o que olhar (falante, cena, trecho do original) e o valor.</summary>
public sealed partial class DictionaryConditionEditor : ObservableObject
{
    private readonly Action<DictionaryConditionEditor> remove;

    public DictionaryConditionEditor(DictionaryCondition condition, Action<DictionaryConditionEditor> remove, Action changed)
    {
        this.remove = remove;
        this.changed = changed;
        kindIndex = Array.IndexOf(Kinds, condition.Kind);
        value = condition.Value;
    }

    private readonly Action changed;
    private int kindIndex;
    private string value;

    public static DictionaryConditionKind[] Kinds { get; } = [DictionaryConditionKind.Speaker, DictionaryConditionKind.Scene, DictionaryConditionKind.SourceContains];

    /// <summary>Os nomes do seletor de tipo, na ordem de <see cref="Kinds"/>.</summary>
    public static IReadOnlyList<string> KindLabels { get; } = ["Falante", "Cena", "Trecho do original"];

    public int KindIndex
    {
        get => kindIndex;
        set
        {
            if (SetProperty(ref kindIndex, Math.Clamp(value, 0, Kinds.Length - 1)))
            {
                OnPropertyChanged(nameof(OperatorLabel));
                changed();
            }
        }
    }

    public string Value
    {
        get => value;
        set
        {
            if (SetProperty(ref this.value, value ?? string.Empty))
            {
                changed();
            }
        }
    }

    /// <summary>"é", "está em" ou "contém": só existe um operador por tipo, então é um rótulo.</summary>
    public string OperatorLabel => Kinds[KindIndex] switch
    {
        DictionaryConditionKind.Speaker => "é",
        DictionaryConditionKind.Scene => "está em",
        _ => "contém",
    };

    public DictionaryCondition ToCondition() => new(Kinds[KindIndex], Value.Trim());

    [RelayCommand]
    private void Remove() => remove(this);
}

/// <summary>
/// O diálogo "Nova regra" do pré ou do pós-dicionário: trocar e por, a lista de condições e quantas linhas a regra alcança, atualizado a cada mudança.
/// Salvar só vale com padrão válido e condições com valor.
/// </summary>
public sealed partial class DictionaryRuleEditor : ObservableObject
{
    private readonly Action<DictionaryRuleEditor> impactRequested;
    private readonly Action<DictionaryRuleEditor> saved;

    public DictionaryRuleEditor(DictionarySide side, DictionaryRule? rule, Action<DictionaryRuleEditor> impactRequested, Action<DictionaryRuleEditor> saved)
    {
        Side = side;
        Original = rule;
        this.impactRequested = impactRequested;
        this.saved = saved;
        pattern = rule?.Pattern ?? string.Empty;
        with = rule?.With ?? string.Empty;
        isRegex = rule?.Regex ?? false;
        Conditions = [.. (rule?.Conditions ?? []).Select(c => new DictionaryConditionEditor(c, RemoveCondition, Refresh))];
        Refresh();
    }

    private string pattern;
    private string with;
    private bool isRegex;

    public DictionarySide Side { get; }

    /// <summary>A regra que está sendo editada; nula na regra nova.</summary>
    public DictionaryRule? Original { get; }

    public string Title => $"{(Original is null ? "Nova regra" : "Editar regra")} do {(Side == DictionarySide.Pre ? "pré" : "pós")}-dicionário";

    public ObservableCollection<DictionaryConditionEditor> Conditions { get; }

    public string Pattern
    {
        get => pattern;
        set
        {
            if (SetProperty(ref pattern, value ?? string.Empty))
            {
                Refresh();
            }
        }
    }

    public string With
    {
        get => with;
        set
        {
            if (SetProperty(ref with, value ?? string.Empty))
            {
                Refresh();
            }
        }
    }

    public bool IsRegex
    {
        get => isRegex;
        set
        {
            if (SetProperty(ref isRegex, value))
            {
                Refresh();
            }
        }
    }

    /// <summary>Por que a regra não pode ser salva; nulo quando pode.</summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    /// <summary>"Aplica em 64 linhas", ou "..." enquanto conta.</summary>
    [ObservableProperty]
    public partial string ImpactTitle { get; set; } = "…";

    /// <summary>"2 linhas do Ren fora do Capítulo 1 não mudam": o que a condição deixa de fora.</summary>
    [ObservableProperty]
    public partial string ImpactDetail { get; set; } = string.Empty;

    public bool HasImpactDetail => ImpactDetail.Length > 0;

    partial void OnImpactDetailChanged(string value) => OnPropertyChanged(nameof(HasImpactDetail));

    public DictionaryRule ToRule() =>
        new(Pattern, With, IsRegex) { Enabled = Original?.Enabled ?? true, Conditions = [.. Conditions.Select(c => c.ToCondition())] };

    [RelayCommand]
    private void AddCondition()
    {
        Conditions.Add(new DictionaryConditionEditor(new DictionaryCondition(DictionaryConditionKind.Speaker, string.Empty), RemoveCondition, Refresh));
        Refresh();
    }

    [RelayCommand]
    private void Save()
    {
        if (Problem is null)
        {
            saved(this);
        }
    }

    private void RemoveCondition(DictionaryConditionEditor condition)
    {
        Conditions.Remove(condition);
        Refresh();
    }

    private void Refresh()
    {
        Problem = ProblemOf();
        impactRequested(this);
    }

    private string? ProblemOf()
    {
        if (Pattern.Length == 0)
        {
            return "Escreva o texto a trocar.";
        }

        return DictionaryStage.Validate(ToRule()) is { } error ? Capitalize(error) : null;
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
