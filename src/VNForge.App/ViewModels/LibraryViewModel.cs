using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Library;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>O que a página da Biblioteca mostra: o convite da biblioteca vazia, a primeira varredura ou os jogos.</summary>
public enum LibraryMode
{
    Empty,
    FirstScan,
    Games,
}

/// <summary>Como os jogos aparecem: a grade de capas ou a lista.</summary>
public enum LibraryLayout
{
    Grid,
    List,
}

/// <summary>Um chip de filtro com a contagem do Core.</summary>
public sealed partial class LibraryFilterChipViewModel(LibraryFilter filter, string label) : ObservableObject
{
    public LibraryFilter Filter { get; } = filter;

    public string Label { get; } = label;

    [ObservableProperty]
    public partial int Count { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string CountText => Count.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(CountText));
}

/// <summary>
/// A tela inicial: as VNs do usuário. Mostra a grade de capas com busca e filtros, a gaveta do jogo escolhido, o diálogo para confirmar o casamento com o VNDB, a primeira varredura e
/// a biblioteca vazia. Tudo o que decide o que aparece (cartão, gaveta, filtros, candidatos, tabela da varredura) vem do Core; esta classe liga a tela a ele e ao <c>library.json</c>.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly LibraryFile? file;
    private readonly IFolderPicker picker;
    private readonly LibraryScanService? scanService;
    private readonly LibraryEditor? editor;
    private readonly TimeProvider clock;
    private readonly Func<LibraryGame, StatusCounts?> readTranslation;
    private readonly Func<UserTags> readUserTags;
    private readonly ILibrarySheetSource? sheets;
    private readonly Action<string> openFolder;
    private readonly Action<Action> post;
    private readonly Dictionary<string, StatusCounts?> translations = new(StringComparer.Ordinal);
    private List<LibraryGame> games = [];
    private GameLibrary library = new();
    private UserTags userTags = new();
    private CancellationTokenSource? scanning;
    private long lastReport;
    private int loadGeneration;

    /// <param name="file">O <c>library.json</c>; nulo é a Biblioteca sem arquivo (só os testes do shell), que fica vazia e não grava nada.</param>
    /// <param name="post">Leva uma ação à thread da tela: o progresso da varredura chega de outras threads e mexe em coleções ligadas à tela. A interface passa <c>Dispatcher.UIThread.Post</c>.</param>
    public LibraryViewModel(
        LibraryFile? file,
        IFolderPicker picker,
        Action<Action> post,
        LibraryScanService? scanService = null,
        LibraryEditor? editor = null,
        TimeProvider? clock = null,
        Func<LibraryGame, StatusCounts?>? readTranslation = null,
        Func<UserTags>? userTags = null,
        ILibrarySheetSource? sheets = null,
        Action<string>? openFolder = null)
    {
        this.file = file;
        this.picker = picker;
        this.scanService = scanService;
        this.editor = editor;
        this.clock = clock ?? TimeProvider.System;
        this.readTranslation = readTranslation ?? LibraryCatalog.ReadTranslation;
        readUserTags = userTags ?? (() => new UserTags());
        this.sheets = sheets;
        this.openFolder = openFolder ?? (_ => { });
        this.post = post;
        Filters =
        [
            new LibraryFilterChipViewModel(LibraryFilter.All, "Todos") { IsSelected = true },
            new LibraryFilterChipViewModel(LibraryFilter.InTranslation, "Em tradução"),
            new LibraryFilterChipViewModel(LibraryFilter.NoPortuguese, "Sem português"),
            new LibraryFilterChipViewModel(LibraryFilter.Unsupported, "Sem suporte"),
        ];
        Match = new LibraryMatchViewModel(this);
        Scan.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LibraryScanViewModel.StatusText))
            {
                OnPropertyChanged(nameof(StatusText));
            }
            else if (e.PropertyName is nameof(LibraryScanViewModel.RescanOnOpen))
            {
                Remembering = RememberRescanOnOpenAsync();
            }
        };
    }

    /// <summary>Uma Biblioteca sem arquivo e sem varredura: sempre vazia. Serve ao shell montado sem janela, nos testes.</summary>
    internal static LibraryViewModel Detached() => new(null, new NoFolderPicker(), action => action());

    /// <summary>As pastas dos projetos de tradução conhecidos, para ligar cada projeto ao jogo dele; o shell traz a lista dos recentes.</summary>
    public Func<IReadOnlyList<string>> RecentProjects { get; set; } = () => [];

    /// <summary>A pessoa pediu "Abrir jogo": o shell abre o projeto do jogo (ou começa um) e leva às abas.</summary>
    public event EventHandler<LibraryGame>? GameOpenRequested;

    /// <summary>"Abrir um projeto que já existe" da biblioteca vazia: o shell pede a pasta do projeto.</summary>
    public event EventHandler? ExistingProjectRequested;

    public IReadOnlyList<LibraryFilterChipViewModel> Filters { get; }

    public ObservableCollection<LibraryCardViewModel> Cards { get; } = [];

    public LibraryMatchViewModel Match { get; }

    public LibraryScanViewModel Scan { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsFirstScan), nameof(HasGames), nameof(StatusText))]
    public partial LibraryMode Mode { get; internal set; }

    public bool IsEmpty => Mode == LibraryMode.Empty;

    public bool IsFirstScan => Mode == LibraryMode.FirstScan;

    public bool HasGames => Mode == LibraryMode.Games;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGrid), nameof(IsList))]
    public partial LibraryLayout Layout { get; set; }

    public bool IsGrid => Layout == LibraryLayout.Grid;

    public bool IsList => Layout == LibraryLayout.List;

    /// <summary>"Grade" e "Lista", o controle segmentado do canto direito.</summary>
    public IReadOnlyList<LibraryLayoutItem> Layouts { get; } = [new(LibraryLayout.Grid, "Grade"), new(LibraryLayout.List, "Lista")];

    public LibraryLayoutItem SelectedLayout
    {
        get => Layouts[(int)Layout];
        set
        {
            if (value is not null)
            {
                Layout = value.Layout;
            }

            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDrawer))]
    public partial LibraryDrawerViewModel? Drawer { get; private set; }

    public bool HasDrawer => Drawer is not null;

    [ObservableProperty]
    public partial LibraryCardViewModel? Selected { get; private set; }

    /// <summary>"12 jogos em 2 pastas · varredura há 2 horas".</summary>
    [ObservableProperty]
    public partial string Subtitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>Uma varredura está rodando: "Adicionar pasta" e "Varrer de novo" esperam, porque duas varreduras se cancelariam e sujariam a mesma página de andamento.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseFolderCommand), nameof(RescanCommand))]
    public partial bool IsScanBusy { get; private set; }

    /// <summary>A busca ou o filtro não deixaram nenhum jogo, e há jogos na biblioteca.</summary>
    [ObservableProperty]
    public partial bool HasNoResults { get; private set; }

    /// <summary>A varredura "de novo" (sem a página de primeira varredura) está rodando.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsRescanning { get; private set; }

    /// <summary>O jogo está sendo mostrado quando a busca ou o filtro estão ligados.</summary>
    public bool HasFilter => SelectedFilter != LibraryFilter.All || !string.IsNullOrWhiteSpace(SearchText);

    public LibraryFilter SelectedFilter => Filters.First(f => f.IsSelected).Filter;

    /// <summary>A barra de status: "12 jogos na Biblioteca", "Nenhum jogo ainda" ou "Varrendo D:\Jogos\VN".</summary>
    public string StatusText => Scan.IsRunning && !string.IsNullOrEmpty(Scan.StatusText)
        ? Scan.StatusText
        : games.Count switch
        {
            0 => "Nenhum jogo ainda",
            1 => "1 jogo na Biblioteca",
            var n => $"{n.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} jogos na Biblioteca",
        };

    /// <summary>A privacidade em vigor (spoiler e adulto escondidos por padrão).</summary>
    public LibraryPrivacy Privacy => library.Privacy;

    /// <summary>A leitura em curso, para os testes esperarem.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>A gravação da escolha "Ler a pasta toda de novo ao abrir o app", para os testes esperarem.</summary>
    public Task Remembering { get; private set; } = Task.CompletedTask;

    /// <summary>A varredura em curso, para os testes esperarem.</summary>
    public Task Scanning { get; private set; } = Task.CompletedTask;

    partial void OnSearchTextChanged(string value) => Refilter();

    partial void OnLayoutChanged(LibraryLayout value) => OnPropertyChanged(nameof(SelectedLayout));

    /// <summary>Escolhe o chip de filtro; os outros desmarcam.</summary>
    public void SelectFilter(LibraryFilter filter)
    {
        foreach (var chip in Filters)
        {
            chip.IsSelected = chip.Filter == filter;
        }

        OnPropertyChanged(nameof(SelectedFilter));
        Refilter();
    }

    [RelayCommand]
    private void ToggleFilter(LibraryFilterChipViewModel? chip) => SelectFilter(chip?.Filter ?? LibraryFilter.All);

    /// <summary>Lê o <c>library.json</c>, liga os projetos de tradução e redesenha. Um arquivo ilegível vira aviso, não lista vazia.</summary>
    public Task LoadAsync() => Loading = LoadCoreAsync();

    private async Task LoadCoreAsync()
    {
        var generation = ++loadGeneration;
        string? error = null;
        GameLibrary loaded;
        Dictionary<string, StatusCounts?> counts = new(StringComparer.Ordinal);
        UserTags tags;
        try
        {
            var recent = RecentProjects();
            (loaded, counts, tags) = await Task.Run(() => Read(recent));
        }
        catch (LibraryException e)
        {
            error = e.Message;
            loaded = new GameLibrary();
            tags = new UserTags();
        }

        if (generation != loadGeneration)
        {
            // Uma leitura mais nova já começou: o resultado desta é mais velho e não pode sobrescrever o dela.
            return;
        }

        library = loaded;
        userTags = tags;
        games = [.. loaded.Sorted()];
        translations.Clear();
        foreach (var pair in counts)
        {
            translations[pair.Key] = pair.Value;
        }

        ErrorMessage = error;
        if (Mode != LibraryMode.FirstScan || Scan.Phase == LibraryScanPhase.Idle)
        {
            Mode = games.Count == 0 ? LibraryMode.Empty : LibraryMode.Games;
        }

        Redraw();
    }

    private (GameLibrary Library, Dictionary<string, StatusCounts?> Counts, UserTags Tags) Read(IReadOnlyList<string> recent)
    {
        if (file is null)
        {
            return (new GameLibrary(), new Dictionary<string, StatusCounts?>(StringComparer.Ordinal), readUserTags());
        }

        var current = file.Load();
        if (recent.Count > 0 && LibraryProjectLinker.LinkAll(current, recent).Count > 0)
        {
            current = file.Update(l =>
            {
                LibraryProjectLinker.LinkAll(l, recent);
                return l;
            });
        }

        var counts = new Dictionary<string, StatusCounts?>(StringComparer.Ordinal);
        foreach (var game in current.Games.Where(g => g.ProjectPath is not null))
        {
            counts[game.Id] = readTranslation(game);
        }

        return (current, counts, readUserTags());
    }

    private int? PercentOf(LibraryGame game) =>
        game.ProjectPath is null ? null : LibraryCards.PercentReady(translations.GetValueOrDefault(game.Id));

    private void Redraw()
    {
        var counts = LibraryListing.Count(games, PercentOf);
        foreach (var chip in Filters)
        {
            chip.Count = counts.Of(chip.Filter);
        }

        Subtitle = SubtitleText();
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Privacy));
        Refilter();
    }

    private void Refilter()
    {
        var shown = LibraryListing.Filter(games, SelectedFilter, SearchText, PercentOf, userTags, library.Privacy);
        var selectedId = Selected?.Id;
        Cards.Clear();
        foreach (var game in shown)
        {
            Cards.Add(new LibraryCardViewModel(game, LibraryCards.Describe(game, library.Privacy, translations.GetValueOrDefault(game.Id))));
        }

        HasNoResults = games.Count > 0 && Cards.Count == 0;
        OnPropertyChanged(nameof(HasFilter));
        Select(Cards.FirstOrDefault(c => c.Id == selectedId));
    }

    private string SubtitleText()
    {
        var folders = library.Roots.Count;
        var total = games.Count;
        var left = total == 1 ? "1 jogo" : $"{total.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} jogos";
        if (folders > 0)
        {
            left += folders == 1 ? " em 1 pasta" : $" em {folders} pastas";
        }

        return library.LastScanAt is { } when ? $"{left} · varredura {ScanAge(when)}" : left;
    }

    private string ScanAge(DateTimeOffset when)
    {
        var elapsed = clock.GetUtcNow() - when;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "agora";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"há {(int)elapsed.TotalMinutes} min";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            var hours = (int)elapsed.TotalHours;
            return hours == 1 ? "há 1 hora" : $"há {hours} horas";
        }

        var days = (int)elapsed.TotalDays;
        return days == 1 ? "ontem" : days <= 30 ? $"há {days} dias" : "há mais de um mês";
    }

    /// <summary>Escolhe o jogo da grade e abre a gaveta dele; escolher o mesmo jogo de novo fecha a gaveta. Nulo fecha.</summary>
    [RelayCommand]
    private void Toggle(LibraryCardViewModel? card) => Select(ReferenceEquals(card, Selected) ? null : card);

    /// <summary>Escolhe o jogo (e abre a gaveta), ou nenhum.</summary>
    public void Select(LibraryCardViewModel? card)
    {
        if (Selected is { } previous)
        {
            previous.IsSelected = false;
        }

        Selected = card;
        if (card is null)
        {
            Drawer = null;
            return;
        }

        card.IsSelected = true;
        var game = card.Game;
        Drawer = new LibraryDrawerViewModel(
            card,
            LibraryDrawers.Describe(game, library.Privacy, userTags, sheets?.SheetOf(game), translations.GetValueOrDefault(game.Id)),
            this);
    }

    [RelayCommand]
    private void CloseDrawer() => Select(null);

    /// <summary>"Abrir jogo" da gaveta: o shell leva ao jogo, ou começa o projeto dele.</summary>
    [RelayCommand]
    private void OpenGame(LibraryCardViewModel? card)
    {
        if ((card ?? Selected) is { } target)
        {
            GameOpenRequested?.Invoke(this, target.Game);
        }
    }

    /// <summary>Um problema que não é da Biblioteca mas aparece nela (o projeto que não abriu).</summary>
    public void ReportProblem(string text) => ErrorMessage = text;

    [RelayCommand]
    private void OpenExistingProject() => ExistingProjectRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>"Mostrar a pasta" do menu da gaveta.</summary>
    [RelayCommand]
    private void ShowFolder(LibraryCardViewModel? card)
    {
        if ((card ?? Selected) is { } target)
        {
            openFolder(target.Game.Folder);
        }
    }

    /// <summary>Abre o diálogo do casamento para o jogo.</summary>
    [RelayCommand]
    private void ChooseMatch(LibraryCardViewModel? card)
    {
        if ((card ?? Selected) is { } target)
        {
            Match.Open(target.Game);
        }
    }

    /// <summary>"Desfazer o casamento" do menu da gaveta: o jogo volta ao título da pasta e fica sem a ficha do VNDB.</summary>
    [RelayCommand]
    private async Task UnconfirmAsync(LibraryCardViewModel? card)
    {
        if ((card ?? Selected) is { } target && editor is not null)
        {
            await RunEditAsync(() => editor.Unconfirm(target.Id), target.Id);
        }
    }

    /// <summary>"Tirar da Biblioteca": o jogo sai da lista; a pasta dele não é tocada.</summary>
    [RelayCommand]
    private async Task RemoveAsync(LibraryCardViewModel? card)
    {
        if ((card ?? Selected) is { } target && editor is not null)
        {
            await RunEditAsync(() => editor.Remove(target.Id), null);
        }
    }

    private async Task RunEditAsync<T>(Func<T> edit, string? keep)
    {
        try
        {
            await Task.Run(edit);
        }
        catch (LibraryException e)
        {
            ErrorMessage = e.Message;
            return;
        }

        await LoadCoreAsync();
        if (keep is not null)
        {
            Select(Cards.FirstOrDefault(c => c.Id == keep));
        }
    }

    internal async Task<LibraryGame?> ConfirmMatchAsync(string gameId, string vndbId)
    {
        if (editor is null)
        {
            return null;
        }

        var confirmed = await editor.ConfirmAsync(gameId, vndbId);
        await LoadCoreAsync();
        Select(Cards.FirstOrDefault(c => c.Id == gameId));
        return confirmed;
    }

    internal async Task DismissCandidatesAsync(string gameId)
    {
        if (editor is not null)
        {
            await Task.Run(() => editor.DismissCandidates(gameId));
            await LoadCoreAsync();
        }
    }

    internal async Task<LibraryGame?> SearchAgainAsync(string gameId)
    {
        if (editor is null)
        {
            return null;
        }

        var searched = await editor.SearchAgainAsync(gameId);
        await LoadCoreAsync();
        return searched;
    }

    /// <summary>Depois de confirmar um jogo, abre o diálogo do próximo que espera confirmação, na ordem da grade; sem outro, não faz nada.</summary>
    internal void OpenNextConfirmation(string after)
    {
        var waiting = games.Where(g => !g.IsConfirmed && g.Candidates.Count > 0 && g.Id != after).ToList();
        if (waiting.Count > 0 && Mode == LibraryMode.Games)
        {
            Match.Open(waiting[0]);
        }
    }

    /// <summary>Abre o diálogo do primeiro jogo que espera confirmação, se houver. Devolve se abriu.</summary>
    public bool OpenFirstConfirmation()
    {
        if (games.FirstOrDefault(g => !g.IsConfirmed && g.Candidates.Count > 0) is not { } first)
        {
            return false;
        }

        Match.Open(first);
        return true;
    }

    // ---- varredura ----

    /// <summary>"Escolher pasta" da biblioteca vazia e "Adicionar pasta" do cabeçalho: pede a pasta e varre. Sem jogos ainda, a varredura ganha a página de primeira varredura.</summary>
    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task ChooseFolderAsync()
    {
        var folder = await picker.PickFolderAsync("Pasta ou drive dos jogos");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            await ScanFolderAsync(folder);
        }
    }

    private bool CanStartScan() => !IsScanBusy;

    /// <summary>Solta uma pasta ou um drive na biblioteca vazia: o mesmo que escolher a pasta.</summary>
    public Task DropFolderAsync(string path) => ScanFolderAsync(path);

    /// <summary>Varre <paramref name="folder"/>. Devolve quando a varredura termina (ou pausa).</summary>
    public Task ScanFolderAsync(string folder)
    {
        if (IsScanBusy)
        {
            return Scanning;
        }

        var existing = library.Roots.FirstOrDefault(r => string.Equals(r.Path, GameLibrary.NormalizeFolder(folder), StringComparison.OrdinalIgnoreCase));
        var root = new LibraryRoot(folder, Scan.IncludeSubfolders, Scan.MatchVndb, existing?.RescanOnOpen ?? Scan.RescanOnOpen);
        // Pausada ou trocando de pasta, a primeira varredura já tem jogos achados, mas segue sendo a primeira.
        var firstScan = Mode == LibraryMode.FirstScan || games.Count == 0;
        return Scanning = RunExclusiveAsync(() => ScanAsync(root, firstScan));
    }

    /// <summary>"Trocar pasta" da primeira varredura: para a varredura de agora, pede outra pasta e recomeça.</summary>
    [RelayCommand]
    private async Task ChangeFolderAsync()
    {
        var folder = await picker.PickFolderAsync("Pasta ou drive dos jogos");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        scanning?.Cancel();
        await Scanning;
        await ScanFolderAsync(folder);
    }

    /// <summary>"Varrer de novo": lê de novo cada pasta guardada, em segundo plano, sem trocar a página.</summary>
    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private Task RescanAsync() => IsScanBusy ? Scanning : Scanning = RunExclusiveAsync(() => RescanAllAsync([.. library.Roots]));

    /// <summary>Lê de novo as pastas que a pessoa marcou com "Ler a pasta toda de novo ao abrir o app". O shell chama ao abrir.</summary>
    public Task RescanOnOpenAsync() => IsScanBusy ? Scanning : Scanning = RunExclusiveAsync(() => RescanAllAsync([.. library.Roots.Where(r => r.RescanOnOpen)]));

    private async Task RunExclusiveAsync(Func<Task> work)
    {
        IsScanBusy = true;
        try
        {
            await work();
        }
        finally
        {
            IsScanBusy = false;
        }
    }

    private async Task RescanAllAsync(IReadOnlyList<LibraryRoot> roots)
    {
        if (scanService is null || roots.Count == 0)
        {
            return;
        }

        IsRescanning = true;
        try
        {
            foreach (var root in roots)
            {
                if (!Directory.Exists(root.Path))
                {
                    ErrorMessage = $"A pasta {root.Path} não está acessível. Conecte o drive ou use \"Adicionar pasta\".";
                    continue;
                }

                await ScanAsync(root, firstScan: false);
            }
        }
        finally
        {
            IsRescanning = false;
        }
    }

    private async Task ScanAsync(LibraryRoot root, bool firstScan)
    {
        if (scanService is null)
        {
            ErrorMessage = "A varredura não está disponível.";
            return;
        }

        scanning?.Cancel();
        var source = scanning = new CancellationTokenSource();
        ErrorMessage = null;
        if (firstScan)
        {
            Mode = LibraryMode.FirstScan;
        }

        Scan.IncludeSubfolders = root.IncludeSubfolders;
        Scan.MatchVndb = root.MatchVndb;
        Scan.RescanOnOpen = root.RescanOnOpen;
        Scan.Begin(root.Path);
        OnPropertyChanged(nameof(StatusText));
        try
        {
            var outcome = await scanService.RunAsync(
                root,
                new Reporter<LibraryScanProgress>(OnScanReport),
                new Reporter<LibraryMatchProgress>(report => post(() => Scan.OnMatch(report))),
                _ => post(() => Scan.StartMatching()),
                source.Token);
            await LoadCoreAsync();
            Scan.Refresh(games);
            Scan.Finish(source.IsCancellationRequested, Summary(outcome));
            if (outcome.Match is { AbortReason: { } reason })
            {
                ErrorMessage = reason;
            }
        }
        catch (LibraryException e)
        {
            ErrorMessage = e.Message;
            Scan.Reset();
            await LoadCoreAsync();
            return;
        }

        if (!firstScan)
        {
            Scan.Reset();
        }
    }

    /// <summary>"Ler a pasta toda de novo ao abrir o app" vale na hora: a pasta já guardada muda no <c>library.json</c>. Sem a pasta guardada ainda, a escolha entra com a varredura.</summary>
    private async Task RememberRescanOnOpenAsync()
    {
        if (file is null || Scan.Phase == LibraryScanPhase.Idle || !Scan.HasFolder)
        {
            return;
        }

        var path = GameLibrary.NormalizeFolder(Scan.Folder);
        var value = Scan.RescanOnOpen;
        try
        {
            // A trava do arquivo espera até 15 s por outro processo: fora da thread da tela.
            await Task.Run(() => file.Update(l => SetRescanOnOpen(l, path, value)));
            SetRescanOnOpen(library, path, value);
        }
        catch (LibraryException e)
        {
            ErrorMessage = e.Message;
        }
    }

    private static void SetRescanOnOpen(GameLibrary target, string path, bool value)
    {
        if (target.Roots.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)) is { } root)
        {
            target.SetRoot(root with { RescanOnOpen = value });
        }
    }

    private void OnScanReport(LibraryScanProgress report)
    {
        // Uma pasta sem jogo não precisa de um quadro novo a cada passo: o andamento sai no máximo a cada 100 ms, e o jogo achado sempre sai.
        var now = Environment.TickCount64;
        if (report.Found is null && now - Interlocked.Read(ref lastReport) < 100)
        {
            return;
        }

        Interlocked.Exchange(ref lastReport, now);
        post(() => Scan.OnFolder(report));
    }

    private static string Summary(LibraryScanOutcome outcome)
    {
        var found = outcome.Scan.Games.Count;
        var left = found == 1 ? "1 jogo" : $"{found} jogos";
        return $"{outcome.Scan.Directories} pastas · {left}";
    }

    /// <summary>Pausa a varredura de agora. O que já foi achado e casado fica; "Continuar" varre de novo.</summary>
    [RelayCommand]
    private async Task ScanActionAsync()
    {
        switch (Scan.Phase)
        {
            case LibraryScanPhase.Scanning or LibraryScanPhase.Matching:
                scanning?.Cancel();
                break;
            case LibraryScanPhase.Paused:
                await ScanFolderAsync(Scan.Folder);
                break;
            case LibraryScanPhase.Done:
                FinishFirstScan();
                break;
        }
    }

    /// <summary>"Ver a Biblioteca" depois da primeira varredura: mostra os jogos e já pede a confirmação do primeiro que ficou em dúvida.</summary>
    public void FinishFirstScan()
    {
        Scan.Reset();
        Mode = games.Count == 0 ? LibraryMode.Empty : LibraryMode.Games;
        Redraw();
        OpenFirstConfirmation();
    }

    partial void OnModeChanged(LibraryMode value) => OnPropertyChanged(nameof(StatusText));

    private sealed class Reporter<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

/// <summary>Um item do controle segmentado Grade e Lista.</summary>
public sealed record LibraryLayoutItem(LibraryLayout Layout, string Label);
