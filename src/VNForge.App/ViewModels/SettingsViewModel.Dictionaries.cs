using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core;
using VNForge.Core.Dictionaries;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>A aba Dicionários: o pré-dicionário (antes da IA) e o pós-dicionário (depois da IA), as regras com condição e a prévia da regra escolhida (#539).</summary>
public sealed partial class SettingsViewModel
{
    private readonly Dictionary<(DictionarySide Side, int Index), DictionaryImpact?> dictionaryImpacts = [];
    private Task<DictionaryData?> dictionaryData = Task.FromResult<DictionaryData?>(null);
    private CancellationTokenSource? editorImpactCancel;
    private int dictionaryVersion;

    public ObservableCollection<DictionaryRuleRow> PreRules { get; } = [];

    public ObservableCollection<DictionaryRuleRow> PostRules { get; } = [];

    /// <summary>Termina quando o projeto foi contado e as regras mostram quantas linhas alcançam.</summary>
    public Task DictionaryCounting { get; private set; } = Task.CompletedTask;

    /// <summary>Termina quando o alcance da regra do diálogo aberto foi calculado.</summary>
    public Task EditorImpactLoading { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial DictionaryRuleRow? SelectedRule { get; private set; }

    /// <summary>O diálogo da regra; nulo com o diálogo fechado.</summary>
    [ObservableProperty]
    public partial DictionaryRuleEditor? Editor { get; private set; }

    public bool IsEditorOpen
    {
        get => Editor is not null;
        set
        {
            if (!value)
            {
                CloseEditor();
            }
        }
    }

    public bool HasPreRules => PreRules.Count > 0;

    public bool HasPostRules => PostRules.Count > 0;

    public bool HasSelectedRule => SelectedRule is not null;

    /// <summary>"você, Ren, Capítulo 1": o resumo da regra escolhida.</summary>
    public string PreviewSubtitle => SelectedRule is { } row
        ? string.Join(", ", new[] { row.Pattern }.Concat(row.Rule.Conditions.Select(c => c.Value)))
        : string.Empty;

    /// <summary>"Linha 0301 · Ren": a linha de exemplo em que a regra troca.</summary>
    public string? AppliedLine { get; private set; }

    public string? AppliedBefore { get; private set; }

    public string? AppliedAfter { get; private set; }

    public bool HasApplied => AppliedLine is not null;

    /// <summary>"Linha 0214 · Aoi (a condição não bate: não troca)": a linha em que o padrão aparece e a regra não troca.</summary>
    public string? BlockedLine { get; private set; }

    public string? BlockedText { get; private set; }

    public bool HasBlocked => BlockedLine is not null;

    public string PreviewEmpty => SelectedRule is null
        ? "Escolha uma regra para ver onde ela troca."
        : HasApplied || HasBlocked ? string.Empty : "Nenhuma linha do projeto tem este texto.";

    public bool HasPreviewEmpty => PreviewEmpty.Length > 0;

    /// <summary>"Mudar uma regra do pré pede nova tradução das 50 linhas afetadas: o pré entra na chave do cache."</summary>
    public string CacheNotice { get; private set; } = string.Empty;

    private void LoadDictionary()
    {
        dictionaryVersion++;
        dictionaryImpacts.Clear();
        SelectedRule = null;
        PreRules.Clear();
        PostRules.Clear();
        var opened = folder;
        if (opened is null)
        {
            dictionaryData = Task.FromResult<DictionaryData?>(null);
            DictionaryCounting = Task.CompletedTask;
            RefreshDictionaryState();
            return;
        }

        FillRows(baseProfile.Dictionary);
        var version = dictionaryVersion;
        dictionaryData = Task.Run(() => ReadDictionaryData(opened));
        DictionaryCounting = CountDictionaryAsync(version);
        RefreshDictionaryState();
    }

    private static DictionaryData? ReadDictionaryData(ProjectFolder opened)
    {
        try
        {
            return new DictionaryData([.. opened.LoadAllText().Values.SelectMany(units => units)], DictionaryContext.For(opened), opened.LoadFormatting().Kinds);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void FillRows(DictionaryRules rules)
    {
        PreRules.Clear();
        PostRules.Clear();
        for (var i = 0; i < rules.Pre.Count; i++)
        {
            PreRules.Add(NewRow(DictionarySide.Pre, i, rules.Pre[i]));
        }

        for (var i = 0; i < rules.Post.Count; i++)
        {
            PostRules.Add(NewRow(DictionarySide.Post, i, rules.Post[i]));
        }
    }

    private DictionaryRuleRow NewRow(DictionarySide side, int index, DictionaryRule rule)
    {
        var row = new DictionaryRuleRow(side, index, rule, ToggleRule, SelectRule);
        if (dictionaryImpacts.TryGetValue((side, index), out var impact))
        {
            row.LinesText = impact is null ? "sem contagem" : DictionaryRuleRow.LinesLabel(impact.Applies);
        }

        return row;
    }

    private async Task CountDictionaryAsync(int version)
    {
        var data = await dictionaryData.ConfigureAwait(true);
        if (version != dictionaryVersion)
        {
            return;
        }

        var rules = baseProfile.Dictionary;
        var counted = await Task.Run(() => Count(rules, data)).ConfigureAwait(true);
        if (version != dictionaryVersion)
        {
            return;
        }

        foreach (var (key, impact) in counted)
        {
            dictionaryImpacts[key] = impact;
        }

        foreach (var row in PreRules.Concat(PostRules))
        {
            row.LinesText = dictionaryImpacts.TryGetValue((row.Side, row.Index), out var impact) && impact is not null ? DictionaryRuleRow.LinesLabel(impact.Applies) : "sem contagem";
        }

        SelectRule(SelectedRule ?? PostRules.FirstOrDefault() ?? PreRules.FirstOrDefault());
        RefreshDictionaryState();
    }

    private static Dictionary<(DictionarySide Side, int Index), DictionaryImpact?> Count(DictionaryRules rules, DictionaryData? data)
    {
        var result = new Dictionary<(DictionarySide Side, int Index), DictionaryImpact?>();
        foreach (var (side, list) in new[] { (DictionarySide.Pre, rules.Pre), (DictionarySide.Post, rules.Post) })
        {
            for (var i = 0; i < list.Count; i++)
            {
                result[(side, i)] = data is null ? null : TryImpact(list[i], side, data);
            }
        }

        return result;
    }

    private static DictionaryImpact? TryImpact(DictionaryRule rule, DictionarySide side, DictionaryData data)
    {
        try
        {
            return DictionaryImpact.Of(rule, side, data.Units, data.Context, data.Kinds);
        }
        catch (VNForgeException)
        {
            return null;
        }
    }

    private void SelectRule(DictionaryRuleRow? row)
    {
        foreach (var other in PreRules.Concat(PostRules))
        {
            other.IsSelected = ReferenceEquals(other, row);
        }

        SelectedRule = row;
        RefreshDictionaryState();
    }

    private void RefreshDictionaryState()
    {
        AppliedLine = AppliedBefore = AppliedAfter = BlockedLine = BlockedText = null;
        if (SelectedRule is { } row && dictionaryImpacts.TryGetValue((row.Side, row.Index), out var impact) && impact is not null)
        {
            var context = dictionaryData.IsCompletedSuccessfully ? dictionaryData.Result?.Context : null;
            if (impact.FirstApplied is { } applied)
            {
                AppliedLine = LineLabel(applied.Unit, context);
                AppliedBefore = applied.Before;
                AppliedAfter = applied.After;
            }

            if (impact.FirstBlocked is { } blocked)
            {
                BlockedLine = $"{LineLabel(blocked.Unit, context)} (a condição não bate: não troca)";
                BlockedText = blocked.Before;
            }
        }

        var affected = PreRules.Where(r => r.Enabled)
            .Sum(r => dictionaryImpacts.TryGetValue((r.Side, r.Index), out var i) && i is not null ? i.Applies : 0);
        CacheNotice = $"Mudar uma regra do pré pede nova tradução das {DictionaryRuleRow.LinesLabel(affected)} afetadas: o pré entra na chave do cache.";

        OnPropertyChanged(nameof(HasPreRules));
        OnPropertyChanged(nameof(HasPostRules));
        OnPropertyChanged(nameof(HasSelectedRule));
        OnPropertyChanged(nameof(PreviewSubtitle));
        OnPropertyChanged(nameof(AppliedLine));
        OnPropertyChanged(nameof(AppliedBefore));
        OnPropertyChanged(nameof(AppliedAfter));
        OnPropertyChanged(nameof(HasApplied));
        OnPropertyChanged(nameof(BlockedLine));
        OnPropertyChanged(nameof(BlockedText));
        OnPropertyChanged(nameof(HasBlocked));
        OnPropertyChanged(nameof(PreviewEmpty));
        OnPropertyChanged(nameof(HasPreviewEmpty));
        OnPropertyChanged(nameof(CacheNotice));
    }

    private static string LineLabel(TextUnit unit, DictionaryContext? context)
    {
        var colon = unit.Id.LastIndexOf(':');
        var number = colon < 0 ? unit.Id : unit.Id[(colon + 1)..].TrimStart('0').PadLeft(4, '0');
        var speaker = context?.Line(unit).Speakers.LastOrDefault();
        return speaker is null ? $"Linha {number}" : $"Linha {number} · {speaker}";
    }

    private void ToggleRule(DictionaryRuleRow row)
    {
        var rules = baseProfile.Dictionary;
        var list = (row.Side == DictionarySide.Pre ? rules.Pre : rules.Post).ToList();
        if (row.Index >= list.Count)
        {
            return;
        }

        list[row.Index] = list[row.Index] with { Enabled = row.Enabled };
        row.UpdateRule(list[row.Index]);
        SaveDictionaryRules(WithList(rules, row.Side, list));
        RefreshDictionaryState();
    }

    private static DictionaryRules WithList(DictionaryRules rules, DictionarySide side, List<DictionaryRule> list) =>
        side == DictionarySide.Pre ? rules with { Pre = list } : rules with { Post = list };

    private void SaveDictionaryRules(DictionaryRules rules)
    {
        if (folder is null)
        {
            return;
        }

        var opened = folder;
        Persist(() =>
        {
            var profile = baseProfile with { Dictionary = rules };
            opened.SaveFormatting(profile);
            baseProfile = profile;
        });
    }

    [RelayCommand]
    private void AddPreRule() => OpenEditor(DictionarySide.Pre, null);

    [RelayCommand]
    private void AddPostRule() => OpenEditor(DictionarySide.Post, null);

    [RelayCommand]
    private void EditSelectedRule()
    {
        if (SelectedRule is { } row)
        {
            OpenEditor(row.Side, row);
        }
    }

    [RelayCommand]
    private void DeleteSelectedRule()
    {
        if (SelectedRule is not { } row)
        {
            return;
        }

        var rules = baseProfile.Dictionary;
        var list = (row.Side == DictionarySide.Pre ? rules.Pre : rules.Post).ToList();
        if (row.Index >= list.Count)
        {
            return;
        }

        list.RemoveAt(row.Index);
        ApplyDictionaryChange(WithList(rules, row.Side, list));
    }

    private DictionaryRuleRow? editing;

    private void OpenEditor(DictionarySide side, DictionaryRuleRow? row)
    {
        if (folder is null)
        {
            return;
        }

        editing = row;
        Editor = new DictionaryRuleEditor(side, row?.Rule, RequestEditorImpact, SaveEditor);
        OnPropertyChanged(nameof(IsEditorOpen));
    }

    private void CloseEditor()
    {
        editorImpactCancel?.Cancel();
        editing = null;
        if (Editor is not null)
        {
            Editor = null;
            OnPropertyChanged(nameof(IsEditorOpen));
        }
    }

    [RelayCommand]
    private void CancelEditor() => CloseEditor();

    private void SaveEditor(DictionaryRuleEditor editor)
    {
        var rules = baseProfile.Dictionary;
        var list = (editor.Side == DictionarySide.Pre ? rules.Pre : rules.Post).ToList();
        var rule = editor.ToRule();
        if (editing is { } row && row.Index < list.Count)
        {
            list[row.Index] = rule;
        }
        else
        {
            list.Add(rule);
        }

        CloseEditor();
        ApplyDictionaryChange(WithList(rules, editor.Side, list));
    }

    /// <summary>Grava as regras, refaz as linhas da lista e conta de novo o alcance de cada uma.</summary>
    private void ApplyDictionaryChange(DictionaryRules rules)
    {
        SaveDictionaryRules(rules);
        dictionaryVersion++;
        dictionaryImpacts.Clear();
        var keep = SelectedRule is { } selected ? (selected.Side, selected.Index) : default((DictionarySide, int)?);
        FillRows(baseProfile.Dictionary);
        SelectedRule = null;
        var version = dictionaryVersion;
        DictionaryCounting = CountDictionaryAsync(version);
        if (keep is { } k)
        {
            SelectRule((k.Item1 == DictionarySide.Pre ? PreRules : PostRules).ElementAtOrDefault(k.Item2));
        }

        RefreshDictionaryState();
    }

    private void RequestEditorImpact(DictionaryRuleEditor editor)
    {
        editorImpactCancel?.Cancel();
        var cancel = editorImpactCancel = new CancellationTokenSource();
        var token = cancel.Token;
        if (editor.Problem is not null)
        {
            editor.ImpactTitle = "Sem contagem";
            editor.ImpactDetail = string.Empty;
            EditorImpactLoading = Task.CompletedTask;
            return;
        }

        var rule = editor.ToRule();
        var side = editor.Side;
        var loading = dictionaryData;
        EditorImpactLoading = ComputeEditorImpactAsync(editor, rule, side, loading, token);
    }

    private static async Task ComputeEditorImpactAsync(DictionaryRuleEditor editor, DictionaryRule rule, DictionarySide side, Task<DictionaryData?> loading, CancellationToken token)
    {
        var data = await loading.ConfigureAwait(true);
        if (data is null || token.IsCancellationRequested)
        {
            return;
        }

        var impact = await Task.Run(() => TryImpact(rule, side, data), token).ConfigureAwait(true);
        if (token.IsCancellationRequested)
        {
            return;
        }

        editor.ImpactTitle = impact is null ? "sem contagem" : $"Aplica em {DictionaryRuleRow.LinesLabel(impact.Applies)}";
        editor.ImpactDetail = impact is { Blocked: > 0 }
            ? $"{DictionaryRuleRow.LinesLabel(impact.Blocked)} com este texto não mudam por causa das condições"
            : string.Empty;
    }

    private sealed record DictionaryData(IReadOnlyList<TextUnit> Units, DictionaryContext Context, IReadOnlyList<TextUnitKind> Kinds);
}
