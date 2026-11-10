using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

/// <summary>A etapa da primeira varredura: lendo as pastas, casando com o VNDB, parada pela pessoa ou terminada.</summary>
public enum LibraryScanPhase
{
    Idle,
    Scanning,
    Matching,
    Paused,
    Done,
}

/// <summary>Uma linha da tabela da primeira varredura, com o texto e o tom de cada pílula.</summary>
public sealed partial class LibraryScanRowViewModel(LibraryScanRow row, bool matching) : ObservableObject
{
    public string Id { get; } = row.Id;

    public string Folder { get; } = row.Folder;

    public string Engine { get; } = row.Engine;

    public LibraryScanSupport Support { get; } = row.Support;

    public string SupportText => Support switch
    {
        LibraryScanSupport.Translates => "Traduz",
        LibraryScanSupport.NoSupportYet => "Sem suporte ainda",
        _ => "Sem engine",
    };

    /// <summary>A classe da pílula: <c>ok</c> verde, <c>warn</c> âmbar, <c>err</c> rosa, <c>pend</c> neutra, <c>tr</c> azul.</summary>
    public string SupportTone => Support switch
    {
        LibraryScanSupport.Translates => "ok",
        LibraryScanSupport.NoSupportYet => "warn",
        _ => "err",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VndbText), nameof(VndbTone))]
    public partial LibraryScanVndb Vndb { get; set; } = row.Vndb;

    /// <summary>Sem o casamento ligado, o jogo não entra na fila do VNDB: "Não buscado" no lugar de "Na fila".</summary>
    public bool Matching { get; } = matching;

    public string VndbText => Vndb switch
    {
        LibraryScanVndb.Matched => "Casado",
        LibraryScanVndb.Confirm => "Confirmar",
        LibraryScanVndb.NoMatch => "Sem casamento",
        LibraryScanVndb.Searching => "Procurando no VNDB",
        _ => Matching ? "Na fila" : "Não buscado",
    };

    public string VndbTone => Vndb switch
    {
        LibraryScanVndb.Matched => "ok",
        LibraryScanVndb.NoMatch => "err",
        LibraryScanVndb.Searching => "tr",
        _ => "pend",
    };
}

/// <summary>
/// A página "Primeira varredura": a pasta e as três escolhas, o andamento (pastas olhadas, depois jogos casados), a tabela com os últimos jogos achados e o "Até agora".
/// Quem anda é a <see cref="LibraryViewModel"/>; aqui só mora o que a página mostra.
/// </summary>
public sealed partial class LibraryScanViewModel : ObservableObject
{
    /// <summary>Quantas linhas a tabela mostra: as últimas achadas.</summary>
    public const int MaxRows = 7;

    private readonly List<LibraryGame> found = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolder))]
    public partial string Folder { get; set; } = string.Empty;

    public bool HasFolder => Folder.Length > 0;

    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; } = true;

    [ObservableProperty]
    public partial bool MatchVndb { get; set; } = true;

    [ObservableProperty]
    public partial bool RescanOnOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(ActionText), nameof(IsRunning), nameof(IsDone), nameof(ShowTable))]
    public partial LibraryScanPhase Phase { get; private set; }

    [ObservableProperty]
    public partial string Detail { get; private set; } = string.Empty;

    /// <summary>O andamento, de 0 a 1.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    public ObservableCollection<LibraryScanRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    public partial LibraryScanTally Tally { get; private set; } = new(0, 0, 0, 0);

    public bool IsRunning => Phase is LibraryScanPhase.Scanning or LibraryScanPhase.Matching;

    public bool IsDone => Phase == LibraryScanPhase.Done;

    public bool ShowTable => Phase != LibraryScanPhase.Idle;

    public string Title => Phase switch
    {
        LibraryScanPhase.Matching => "Casando com o VNDB",
        LibraryScanPhase.Paused => "Varredura pausada",
        LibraryScanPhase.Done => "Varredura concluída",
        _ => "Varrendo",
    };

    /// <summary>O botão do cartão de andamento: "Pausar" enquanto anda, "Continuar" parada, "Ver a Biblioteca" no fim.</summary>
    public string ActionText => Phase switch
    {
        LibraryScanPhase.Paused => "Continuar",
        LibraryScanPhase.Done => "Ver a Biblioteca",
        _ => "Pausar",
    };

    /// <summary>"Varrendo D:\Jogos\VN" na barra de status; vazio fora da varredura.</summary>
    public string StatusText => IsRunning ? $"Varrendo {Folder}" : string.Empty;

    public int Recognized => Tally.Recognized;

    public int NoSupportYet => Tally.NoSupportYet;

    public int Unrecognized => Tally.Unrecognized;

    public int ToConfirm => Tally.ToConfirm;

    partial void OnTallyChanged(LibraryScanTally value)
    {
        OnPropertyChanged(nameof(Recognized));
        OnPropertyChanged(nameof(NoSupportYet));
        OnPropertyChanged(nameof(Unrecognized));
        OnPropertyChanged(nameof(ToConfirm));
    }

    partial void OnFolderChanged(string value) => OnPropertyChanged(nameof(StatusText));

    partial void OnPhaseChanged(LibraryScanPhase value) => OnPropertyChanged(nameof(StatusText));

    public void Begin(string folder)
    {
        Folder = folder;
        found.Clear();
        Rows.Clear();
        Tally = new LibraryScanTally(0, 0, 0, 0);
        Progress = 0;
        Detail = "Lendo as pastas";
        Phase = LibraryScanPhase.Scanning;
    }

    public void OnFolder(LibraryScanProgress report)
    {
        if (report.Found is { } game)
        {
            found.Add(game);
            Rows.Add(new LibraryScanRowViewModel(LibraryScanReport.Row(game), MatchVndb));
            while (Rows.Count > MaxRows)
            {
                Rows.RemoveAt(0);
            }

            Tally = LibraryScanReport.Tally(found);
        }

        var estimate = report.EstimatedDirectories ?? Math.Max(report.Directories, 1);
        Progress = Math.Clamp((double)report.Directories / Math.Max(estimate, 1), 0, 1);
        Detail = report.EstimatedDirectories is null
            ? $"{report.Directories} pastas · {report.Current}"
            : $"{report.Directories} de {estimate} pastas · {report.Current}";
    }

    public void OnMatch(LibraryMatchProgress report)
    {
        Phase = LibraryScanPhase.Matching;
        Progress = report.Total == 0 ? 1 : (double)report.Done / report.Total;
        Detail = $"{report.Done} de {report.Total} jogos";
        var index = found.FindIndex(g => g.Id == report.Game.Id);
        if (index >= 0)
        {
            found[index] = report.Game;
            Tally = LibraryScanReport.Tally(found);
        }

        if (Rows.FirstOrDefault(r => r.Id == report.Game.Id) is { } row)
        {
            row.Vndb = LibraryScanReport.Row(report.Game).Vndb;
        }

        if (Rows.FirstOrDefault(r => r.Vndb == LibraryScanVndb.Waiting) is { } next)
        {
            next.Vndb = LibraryScanVndb.Searching;
        }
    }

    public void StartMatching()
    {
        Phase = LibraryScanPhase.Matching;
        Progress = 0;
        Detail = "Procurando os jogos no VNDB";
        if (Rows.FirstOrDefault(r => r.Vndb == LibraryScanVndb.Waiting) is { } first)
        {
            first.Vndb = LibraryScanVndb.Searching;
        }
    }

    public void Finish(bool paused, string summary)
    {
        Phase = paused ? LibraryScanPhase.Paused : LibraryScanPhase.Done;
        Detail = summary;
        if (!paused)
        {
            Progress = 1;
        }

        foreach (var row in Rows.Where(r => r.Vndb == LibraryScanVndb.Searching))
        {
            row.Vndb = LibraryScanVndb.Waiting;
        }
    }

    public void Reset() => Phase = LibraryScanPhase.Idle;

    /// <summary>Atualiza a tabela e o "Até agora" com os jogos como estão na biblioteca depois da varredura.</summary>
    public void Refresh(IEnumerable<LibraryGame> games)
    {
        var byId = games.ToDictionary(g => g.Id);
        for (var i = 0; i < found.Count; i++)
        {
            if (byId.TryGetValue(found[i].Id, out var current))
            {
                found[i] = current;
            }
        }

        foreach (var row in Rows)
        {
            if (byId.TryGetValue(row.Id, out var current))
            {
                row.Vndb = LibraryScanReport.Row(current).Vndb;
            }
        }

        Tally = LibraryScanReport.Tally(found);
    }
}
