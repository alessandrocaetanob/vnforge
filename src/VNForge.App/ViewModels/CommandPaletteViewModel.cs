using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>O tipo de resultado da paleta; a ordem é a de exibição.</summary>
public enum PaletteGroup
{
    Command,
    Screen,
    Script,
    Line,
}

/// <summary>Um resultado da paleta: o que mostrar e o que fazer quando o usuário o escolhe. <see cref="ShowGroup"/> marca o primeiro de cada grupo, que leva o cabeçalho.</summary>
public sealed record PaletteEntry(PaletteGroup Group, string Title, string Subtitle, Action Execute)
{
    public bool ShowGroup { get; init; }

    public string GroupLabel => Group switch
    {
        PaletteGroup.Command => "Comandos",
        PaletteGroup.Screen => "Telas",
        PaletteGroup.Script => "Scripts",
        _ => "Linhas",
    };
}

/// <summary>
/// Paleta de comandos (<c>Ctrl+K</c>): acha comandos, telas e scripts pelo nome e linhas pelo texto, e executa o escolhido.
/// Sem texto, lista os comandos e as telas. Quem a cria entrega as fontes; a paleta só filtra, ordena e executa.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private const int CommandLimit = 5;
    private const int ScreenLimit = 5;
    private const int ScriptLimit = 6;
    private const int LineLimit = 8;

    private readonly Func<IReadOnlyList<PaletteEntry>> namedEntries;
    private readonly Func<string, int, IReadOnlyList<PaletteEntry>> lineSearch;

    /// <param name="namedEntries">Comandos, telas e scripts de agora; a paleta os filtra pelo título.</param>
    /// <param name="lineSearch">Procura linhas pelo texto e devolve até o limite dado.</param>
    public CommandPaletteViewModel(Func<IReadOnlyList<PaletteEntry>> namedEntries, Func<string, int, IReadOnlyList<PaletteEntry>> lineSearch)
    {
        this.namedEntries = namedEntries;
        this.lineSearch = lineSearch;
    }

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    /// <summary>Os resultados, trocados por inteiro a cada texto digitado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults), nameof(EmptyMessage), nameof(SelectedEntry))]
    public partial IReadOnlyList<PaletteEntry> Results { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedEntry))]
    public partial int SelectedIndex { get; set; } = -1;

    public PaletteEntry? SelectedEntry => SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;

    public bool HasResults => Results.Count > 0;

    public string EmptyMessage => string.IsNullOrWhiteSpace(Query) ? "Nada para mostrar." : $"Nada encontrado para \"{Query.Trim()}\".";

    /// <summary>Abre a paleta com o campo vazio.</summary>
    [RelayCommand]
    public void Open()
    {
        Query = string.Empty;
        IsOpen = true;
        Refresh();
    }

    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        Query = string.Empty;
        Results = [];
        SelectedIndex = -1;
    }

    [RelayCommand]
    public void SelectNext() => Move(+1);

    [RelayCommand]
    public void SelectPrevious() => Move(-1);

    /// <summary>Fecha a paleta e executa o resultado destacado; sem resultado só fecha.</summary>
    [RelayCommand]
    public void ExecuteSelected()
    {
        var entry = SelectedEntry;
        Close();
        entry?.Execute();
    }

    /// <summary>Fecha a paleta e executa <paramref name="entry"/>, como o clique no resultado.</summary>
    [RelayCommand]
    public void Execute(PaletteEntry? entry)
    {
        Close();
        entry?.Execute();
    }

    partial void OnQueryChanged(string value)
    {
        if (IsOpen)
        {
            Refresh();
        }
    }

    private void Move(int step)
    {
        if (Results.Count == 0)
        {
            return;
        }

        SelectedIndex = SelectedIndex < 0 ? (step > 0 ? 0 : Results.Count - 1) : (SelectedIndex + step + Results.Count) % Results.Count;
    }

    private void Refresh()
    {
        var query = Query.Trim();
        var named = namedEntries();
        var results = new List<PaletteEntry>();
        if (query.Length == 0)
        {
            results.AddRange(Take(named, PaletteGroup.Command, string.Empty, CommandLimit));
            results.AddRange(Take(named, PaletteGroup.Screen, string.Empty, ScreenLimit));
        }
        else
        {
            results.AddRange(Take(named, PaletteGroup.Command, query, CommandLimit));
            results.AddRange(Take(named, PaletteGroup.Screen, query, ScreenLimit));
            results.AddRange(Take(named, PaletteGroup.Script, query, ScriptLimit));
            results.AddRange(lineSearch(query, LineLimit).Take(LineLimit));
        }

        Results = MarkGroups(results);
        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    private static List<PaletteEntry> MarkGroups(List<PaletteEntry> results)
    {
        var marked = new List<PaletteEntry>(results.Count);
        PaletteGroup? previous = null;
        foreach (var entry in results)
        {
            marked.Add(entry with { ShowGroup = entry.Group != previous });
            previous = entry.Group;
        }

        return marked;
    }

    /// <summary>Os resultados do grupo cujo título contém o texto, sem acento nem maiúsculas; os que começam pelo texto vêm antes.</summary>
    private static IEnumerable<PaletteEntry> Take(IReadOnlyList<PaletteEntry> entries, PaletteGroup group, string query, int limit)
    {
        var needle = Fold(query);
        return entries
            .Where(e => e.Group == group)
            .Select(e => (Entry: e, Position: Fold(e.Title).IndexOf(needle, StringComparison.Ordinal)))
            .Where(m => m.Position >= 0)
            .OrderBy(m => m.Position == 0 ? 0 : 1)
            .Select(m => m.Entry)
            .Take(limit);
    }

    /// <summary>Minúsculas e sem acento, para "conferencia" achar "Conferência".</summary>
    public static string Fold(string text) => SearchText.Fold(text);
}
