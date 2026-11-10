using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

/// <summary>Um termo do filtro por tag como a barra o desenha: o termo do Core (<c>minha:favorito</c>), o nome e de quem é. <paramref name="Excluded"/> é o termo de exclusão (<c>--not</c>), que a CLI grava no filtro salvo.</summary>
public sealed record LibraryActiveTag(string Term, string Text, bool Mine, bool Excluded = false);

/// <summary>Uma linha do popover "Tags": a caixa marcada quando o termo está no filtro, a tag e em quantos jogos ela está.</summary>
public sealed partial class LibraryTagOptionViewModel(string term, string text, int count, bool mine, bool isChecked) : ObservableObject
{
    public string Term { get; } = term;

    public string Text { get; } = text;

    public int Count { get; } = count;

    public bool Mine { get; } = mine;

    public string CountText => Count.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = isChecked;
}

/// <summary>Uma tag sua no diálogo "Suas tags": o nome, quantos jogos a usam e o que a linha está fazendo (renomear, confirmar a exclusão).</summary>
public sealed partial class LibraryTagRowViewModel(string name, int games) : ObservableObject
{
    public string Name { get; } = name;

    public int Games { get; } = games;

    public string GamesText => Games == 1 ? "1 jogo" : $"{Games.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} jogos";

    public string DeleteTitle => $"Apagar “{Name}”?";

    public string DeleteText => Games == 0
        ? "A tag sai da lista. As tags do VNDB não mudam."
        : $"A tag sai {(Games == 1 ? "do jogo que a usa" : $"dos {Games} jogos que a usam")}. As tags do VNDB não mudam.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsConfirmingDelete { get; set; }

    [ObservableProperty]
    public partial string EditText { get; set; } = name;

    public bool IsIdle => !IsEditing && !IsConfirmingDelete;
}

/// <summary>
/// O filtro por tag da Biblioteca e o diálogo que gerencia as tags da pessoa. O filtro combina as tags dela com as do VNDB (<see cref="LibraryTagFilter"/>) e grava a cada mudança
/// no <c>library-tags.json</c>, então "Filtro salvo" é o que a barra diz o tempo todo. O diálogo cria, renomeia e apaga; o Core atualiza os jogos e o filtro salvo juntos.
/// </summary>
public sealed partial class LibraryTagsViewModel(LibraryViewModel library) : ObservableObject
{
    private const int VndbRowsShown = 10;

    private readonly Dictionary<string, string> vndbNames = new(StringComparer.Ordinal);

    internal LibraryTagFilter Filter { get; private set; } = LibraryTagFilter.Empty;

    public ObservableCollection<LibraryActiveTag> Active { get; } = [];

    public ObservableCollection<LibraryTagOptionViewModel> OwnOptions { get; } = [];

    public ObservableCollection<LibraryTagOptionViewModel> VndbOptions { get; } = [];

    public ObservableCollection<LibraryTagRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ShownText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsManaging { get; set; }

    [ObservableProperty]
    public partial string NewName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Message { get; private set; }

    public bool HasActive => Active.Count > 0;

    public bool HasMode => Filter.Include.Count > 1;

    public string ModeText => Filter.Any ? "Tem uma das tags" : Filter.Include.Count == 2 ? "Tem as duas tags" : "Tem todas as tags";

    public bool HasOwn => OwnOptions.Count > 0;

    public bool HasVndb => VndbOptions.Count > 0;

    partial void OnSearchChanged(string value) => RebuildOptions();

    partial void OnIsOpenChanged(bool value)
    {
        if (value)
        {
            Search = string.Empty;
            RebuildOptions();
        }
    }

    /// <summary>Uma edição que não valeu: o aviso vai para o diálogo, ou para a Biblioteca quando o diálogo está fechado (o filtro não gravou).</summary>
    internal void Fail(string text)
    {
        Message = text;
        if (!IsManaging)
        {
            library.ReportProblem(text);
        }
    }

    /// <summary>Adota o filtro salvo no arquivo, só na primeira leitura: depois disso o filtro é o que a pessoa montou.</summary>
    internal void Adopt(LibraryTagFilter filter) => SetFilter(filter, save: false);

    /// <summary>Redesenha o que depende dos jogos e das tags (contagens, linhas do diálogo) depois de uma leitura ou de uma edição.</summary>
    internal void Refresh()
    {
        RebuildOptions();
        RebuildRows();
    }

    [RelayCommand]
    private void ToggleOpen() => IsOpen = !IsOpen;

    [RelayCommand]
    private void ToggleOption(LibraryTagOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        var include = Filter.Include.Where(t => !Names(t, option)).ToList();
        if (include.Count == Filter.Include.Count)
        {
            include.Add(option.Term);
        }

        SetFilter(LibraryTagFilter.Create(include, Filter.Any, Filter.Exclude), save: true);
    }

    [RelayCommand]
    private void RemoveActive(LibraryActiveTag? tag)
    {
        if (tag is not null)
        {
            var include = tag.Excluded ? Filter.Include : [.. Filter.Include.Where(t => t != tag.Term)];
            var exclude = tag.Excluded ? [.. Filter.Exclude.Where(t => t != tag.Term)] : Filter.Exclude;
            SetFilter(LibraryTagFilter.Create(include, Filter.Any, exclude), save: true);
        }
    }

    [RelayCommand]
    private void ToggleMode() => SetFilter(LibraryTagFilter.Create(Filter.Include, !Filter.Any, Filter.Exclude), save: true);

    [RelayCommand]
    private void OpenManage()
    {
        IsOpen = false;
        Message = null;
        NewName = string.Empty;
        RebuildRows();
        IsManaging = true;
    }

    [RelayCommand]
    private void CloseManage() => IsManaging = false;

    [RelayCommand]
    private void AddTag()
    {
        var name = NewName;
        if (library.EditTags(tags => tags.Define(name)) is null)
        {
            return;
        }

        NewName = string.Empty;
        Message = null;
        Refresh();
    }

    [RelayCommand]
    private void StartRename(LibraryTagRowViewModel? row)
    {
        if (row is not null)
        {
            row.EditText = row.Name;
            row.IsConfirmingDelete = false;
            row.IsEditing = true;
        }
    }

    [RelayCommand]
    private void CancelRename(LibraryTagRowViewModel? row)
    {
        if (row is not null)
        {
            row.IsEditing = false;
        }
    }

    [RelayCommand]
    private void SaveRename(LibraryTagRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var to = row.EditText;
        var edit = library.EditTags(tags =>
        {
            var target = UserTagName.Normalize(to);
            if (target != row.Name && tags.TryGet(target, out _))
            {
                throw new LibraryException($"Já existe uma tag chamada '{target}'.");
            }

            return tags.Rename(row.Name, target);
        });
        if (edit is not null)
        {
            Finish(edit);
        }
    }

    [RelayCommand]
    private void AskDelete(LibraryTagRowViewModel? row)
    {
        if (row is not null)
        {
            row.IsEditing = false;
            row.IsConfirmingDelete = true;
        }
    }

    [RelayCommand]
    private void CancelDelete(LibraryTagRowViewModel? row)
    {
        if (row is not null)
        {
            row.IsConfirmingDelete = false;
        }
    }

    [RelayCommand]
    private void ConfirmDelete(LibraryTagRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var edit = library.EditTags(tags => tags.Delete(row.Name, library.Games));
        if (edit is not null)
        {
            Finish(edit);
        }
    }

    /// <summary>Depois de renomear ou apagar: o filtro da barra é o salvo, já reescrito pelo Core, e os avisos sobre ele aparecem no diálogo.</summary>
    private void Finish(UserTagEdit edit)
    {
        Message = edit.Notices.Count == 0 ? null : string.Join(" ", edit.Notices);
        SetFilter(library.UserTags.SavedFilter, save: false);
        Refresh();
    }

    /// <summary>Os chips de tag de um cartão com o filtro ligado: as tags do jogo que o filtro procura, as suas primeiro, e o "+N" do resto.</summary>
    internal IReadOnlyList<LibraryDrawerChip> CardChips(LibraryGame game)
    {
        if (Filter.IsEmpty)
        {
            return [];
        }

        var own = library.UserTags.TagsOf(game.Id);
        var vndb = LibraryVisibility.VisibleTags(game, library.Privacy);
        var terms = Filter.Include.Select(LibraryTagTerm.Parse).ToList();
        var chips = new List<LibraryDrawerChip>();
        foreach (var name in own.Where(n => terms.Any(t => t.Scope != LibraryTagScope.Vndb && t.Name == n)))
        {
            chips.Add(new LibraryDrawerChip(name, LibraryDrawerChipKind.Mine));
        }

        foreach (var tag in vndb.Where(g => terms.Any(t => t.Scope != LibraryTagScope.Own && (string.Equals(g.Id, t.Name, StringComparison.OrdinalIgnoreCase) || UserTagName.Collapse(g.Name) == t.Name))))
        {
            chips.Add(new LibraryDrawerChip(tag.Name, LibraryDrawerChipKind.Vndb));
        }

        if (chips.Count == 0)
        {
            return [];
        }

        chips = [.. chips.Take(2)];
        var rest = own.Count + vndb.Count - chips.Count;
        if (rest > 0)
        {
            chips.Add(new LibraryDrawerChip("+" + rest, LibraryDrawerChipKind.More));
        }

        return chips;
    }

    private void SetFilter(LibraryTagFilter filter, bool save)
    {
        Filter = filter;
        if (save)
        {
            library.SaveTagFilter(filter);
        }

        RebuildOptions();
        OnPropertyChanged(nameof(HasMode));
        OnPropertyChanged(nameof(ModeText));
        library.TagFilterChanged();
    }

    private void RebuildActive()
    {
        Active.Clear();
        foreach (var text in Filter.Include)
        {
            var term = LibraryTagTerm.Parse(text);
            var mine = term.Scope == LibraryTagScope.Own || (term.Scope == LibraryTagScope.Any && library.UserTags.TryGet(term.Name, out _));
            Active.Add(new LibraryActiveTag(text, mine ? term.Name : vndbNames.GetValueOrDefault(term.Name, term.Name), mine));
        }

        foreach (var text in Filter.Exclude)
        {
            var term = LibraryTagTerm.Parse(text);
            Active.Add(new LibraryActiveTag(text, "sem " + vndbNames.GetValueOrDefault(term.Name, term.Name), false, true));
        }

        OnPropertyChanged(nameof(HasActive));
    }

    private void RebuildOptions()
    {
        var present = library.Games.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var query = UserTagName.Collapse(Search);
        var ownAll = library.UserTags.Tags.Select(t => (t.Name, Count: library.UserTags.GamesWith(t.Name).Count(present.Contains))).ToList();

        var perName = new Dictionary<string, (string Display, int Count)>(StringComparer.Ordinal);
        foreach (var game in library.Games)
        {
            foreach (var tag in LibraryVisibility.VisibleTags(game, library.Privacy).DistinctBy(t => UserTagName.Collapse(t.Name)))
            {
                var key = UserTagName.Collapse(tag.Name);
                perName[key] = perName.TryGetValue(key, out var seen) ? (seen.Display, seen.Count + 1) : (tag.Name, 1);
            }
        }

        vndbNames.Clear();
        foreach (var pair in perName)
        {
            vndbNames[pair.Key] = pair.Value.Display;
        }

        OwnOptions.Clear();
        foreach (var (name, count) in ownAll.Where(t => query.Length == 0 || t.Name.Contains(query, StringComparison.Ordinal)))
        {
            var term = LibraryTagTerm.OwnPrefix + name;
            OwnOptions.Add(new LibraryTagOptionViewModel(term, name, count, true, IsIncluded(term)));
        }

        VndbOptions.Clear();
        var candidates = perName
            .Where(p => query.Length == 0 || p.Key.Contains(query, StringComparison.Ordinal))
            .Select(p => new LibraryTagOptionViewModel(LibraryTagTerm.VndbPrefix + p.Key, p.Value.Display, p.Value.Count, false, IsIncluded(LibraryTagTerm.VndbPrefix + p.Key)))
            .OrderByDescending(o => o.IsChecked)
            .ThenByDescending(o => o.Count)
            .ThenBy(o => o.Text, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        foreach (var option in candidates.Take(Math.Max(VndbRowsShown, candidates.Count(o => o.IsChecked))))
        {
            VndbOptions.Add(option);
        }

        var shown = OwnOptions.Count + VndbOptions.Count;
        var total = ownAll.Count + perName.Count;
        ShownText = $"Mostrando {shown.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} de {total.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))}";
        OnPropertyChanged(nameof(HasOwn));
        OnPropertyChanged(nameof(HasVndb));
        RebuildActive();
    }

    private void RebuildRows()
    {
        var present = library.Games.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        Rows.Clear();
        foreach (var tag in library.UserTags.Tags)
        {
            Rows.Add(new LibraryTagRowViewModel(tag.Name, library.UserTags.GamesWith(tag.Name).Count(present.Contains)));
        }
    }

    private bool IsIncluded(string term)
    {
        var parsed = LibraryTagTerm.Parse(term);
        return Filter.Include.Any(t => Same(LibraryTagTerm.Parse(t), parsed));
    }

    private static bool Names(string text, LibraryTagOptionViewModel option) => Same(LibraryTagTerm.Parse(text), LibraryTagTerm.Parse(option.Term));

    private static bool Same(LibraryTagTerm filterTerm, LibraryTagTerm option) =>
        filterTerm.Name == option.Name && (filterTerm.Scope == LibraryTagScope.Any || filterTerm.Scope == option.Scope);
}
