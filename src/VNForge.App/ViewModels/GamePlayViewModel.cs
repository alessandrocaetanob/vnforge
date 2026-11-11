using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

/// <summary>Uma barra do gráfico por semana: a altura em pixels, se é a semana de agora e o texto da dica.</summary>
public sealed record WeekBar(double Height, bool IsCurrent, string Tip);

/// <summary>
/// A aba Jogar do jogo aberto (d3, #549): "Jogando agora" com a hora e o tempo desta partida, os interruptores do Locale Emulator e do lançador, a lista de sessões, o tempo total e o
/// gráfico por semana. O que é jogo e sessão vem da <see cref="LibraryViewModel"/> (o <c>library.json</c> e o <c>playtime.json</c>); o que vira texto e barra vem do Core (<see cref="PlayHistory"/>).
/// </summary>
public sealed partial class GamePlayViewModel : ObservableObject
{
    private const double ChartHeight = 80;
    private const double ChartMinBar = 4;

    // O quadro da aba mostra as quatro mais recentes; o total e o gráfico contam todas.
    private const int VisibleRows = 4;

    private readonly LibraryViewModel library;
    private readonly TimeProvider clock;
    private string? projectRoot;
    private LibraryGame? game;
    private DateTimeOffset? startedAt;
    private bool startedViaLocale;
    private IReadOnlyList<PlaySession> sessions = [];
    private int liveMinutes = -1;

    public GamePlayViewModel(LibraryViewModel library, TimeProvider? clock = null)
    {
        this.library = library;
        this.clock = clock ?? TimeProvider.System;
        PlayCommand = new AsyncRelayCommand(PlayAsync, () => game is not null && library.PlayingId is null);
        OpenSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
        library.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LibraryViewModel.Played) or nameof(LibraryViewModel.PlayingId))
            {
                Refresh();
            }
        };
    }

    /// <summary>"Caminho nos Ajustes": o shell abre os Ajustes na aba Jogos.</summary>
    public event EventHandler? SettingsRequested;

    public IAsyncRelayCommand PlayCommand { get; }

    public IRelayCommand OpenSettingsCommand { get; }

    /// <summary>O jogo da Biblioteca a que o projeto aberto pertence; a aba só tem o que mostrar quando ele existe.</summary>
    public bool HasGame => game is not null;

    public string NoGameMessage => "Este projeto não está ligado a um jogo da Biblioteca. Adicione a pasta do jogo na Biblioteca para abri-lo e contar o tempo por aqui.";

    public bool IsPlaying => game is not null && library.PlayingId == game.Id;

    public string PlayLabel => IsPlaying ? "Jogando" : "Jogar";

    /// <summary>Muda a cada segundo enquanto a partida corre: a tela chama <see cref="Tick"/>.</summary>
    public string ElapsedText => startedAt is { } start && IsPlaying ? Hms(clock.GetUtcNow() - start) : string.Empty;

    public string CardTitle => IsPlaying ? "Jogando agora" : "Pronto para jogar";

    public string CardDetail
    {
        get
        {
            var process = $"o app conta pelo processo {(game?.Exe is { Length: > 0 } exe ? Path.GetFileName(exe) : "do jogo")}";
            if (!IsPlaying || startedAt is not { } start)
            {
                return $"Abre o jogo{(game?.RequiresJapaneseLocale == true ? " pelo Locale Emulator" : string.Empty)} · {process}";
            }

            return string.Create(CultureInfo.InvariantCulture, $"Aberto às {start.ToLocalTime():HH:mm}{(startedViaLocale ? " pelo Locale Emulator" : string.Empty)} · {process}");
        }
    }

    public bool LocaleEmulatorOn
    {
        get => game?.RequiresJapaneseLocale == true;
        set
        {
            if (game is { } current && current.RequiresJapaneseLocale != value)
            {
                game = current with { RequiresJapaneseLocale = value };
                library.RememberJapaneseLocale(current.Id, value);
                Raise();
            }
        }
    }

    public string LocaleEmulatorNote => (LocaleEmulatorOn, string.IsNullOrWhiteSpace(library.LocaleEmulatorPath)) switch
    {
        (false, _) => "Marque se o jogo pede localidade japonesa. O caminho do Locale Emulator fica nos Ajustes.",
        (true, true) => "Este jogo pede localidade japonesa, mas o Locale Emulator não está configurado. Informe o caminho nos Ajustes.",
        _ => $"Este jogo pede localidade japonesa. Locale Emulator encontrado em {library.LocaleEmulatorPath}.",
    };

    public bool OpensLauncherOn
    {
        get => game?.OpensLauncher == true;
        set
        {
            if (game is { } current && current.OpensLauncher != value)
            {
                game = current with { OpensLauncher = value };
                library.RememberOpensLauncher(current.Id, value);
                Raise();
            }
        }
    }

    // --- Sessões ---

    public IReadOnlyList<string> ViewOptions { get; } = ["Semanas", "Lista"];

    /// <summary>O segmento da lista de sessões: 0 é "Semanas" e 1 é "Lista", que abre marcada.</summary>
    [ObservableProperty]
    public partial int ViewIndex { get; set; } = 1;

    private bool ShowsWeeks => ViewIndex == 0;

    public IReadOnlyList<PlaySessionRow> Rows { get; private set; } = [];

    public bool HasRows => Rows.Count > 0;

    public bool ShowsEmptyRows => !HasRows;

    public string EmptyRowsText => "Nenhuma sessão ainda. O tempo de jogo aparece aqui depois da primeira partida.";

    // --- Tempo de jogo ---

    public string TotalText { get; private set; } = string.Empty;

    public string AverageText { get; private set; } = string.Empty;

    public IReadOnlyList<WeekBar> Bars { get; private set; } = [];

    /// <summary>Começa a ler as sessões do jogo do projeto em <paramref name="root"/>; <see langword="null"/> quando não há projeto aberto.</summary>
    public void Open(string? root)
    {
        projectRoot = root;
        Refresh();
    }

    /// <summary>Atualiza o tempo desta partida; a tela chama a cada segundo enquanto <see cref="IsPlaying"/>.</summary>
    public void Tick()
    {
        OnPropertyChanged(nameof(ElapsedText));
        if (IsPlaying && startedAt is { } start && (int)(clock.GetUtcNow() - start).TotalMinutes != liveMinutes)
        {
            RefreshRows();
        }
    }

    private async Task PlayAsync()
    {
        if (game is { } current)
        {
            await library.PlayGameAsync(current);
        }
    }

    private void Refresh()
    {
        game = projectRoot is null ? null : library.GameOfProject(projectRoot);
        // O começo vem da Biblioteca, que o anotou ao abrir o jogo: abrir a aba com a partida em curso não reinicia o relógio.
        startedAt = IsPlaying ? library.PlayingSince : null;
        startedViaLocale = library.PlayingViaLocale;

        var sessions = game is null ? [] : library.Played.SessionsOf(game.Id);
        var now = clock.GetUtcNow();
        var stats = game is null ? PlayStats.None : library.Played.StatsOf(game.Id);
        this.sessions = sessions;
        BuildRows();
        TotalText = stats.Sessions == 0 ? "0 min" : PlayTime.Format(stats.Total);
        AverageText = Describe(stats);
        Bars = BarsOf(PlayHistory.PerWeek(sessions, now));
        Raise();
        PlayCommand.NotifyCanExecuteChanged();
    }

    private void Raise() => OnPropertyChanged(string.Empty);

    private void RefreshRows()
    {
        BuildRows();
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ShowsEmptyRows));
    }

    // A lista traz a partida em curso, com o tempo de agora, junto das sessões do playtime.json (que só entra lá quando a Biblioteca relê).
    private void BuildRows()
    {
        var now = clock.GetUtcNow();
        var listed = sessions;
        liveMinutes = -1;
        if (IsPlaying && startedAt is { } start)
        {
            var elapsed = now - start;
            liveMinutes = (int)elapsed.TotalMinutes;
            listed = [.. sessions.Where(s => !s.Open), new PlaySession(PlaySession.NewId(), start, now, elapsed, Open: true, ViaLocaleEmulator: startedViaLocale)];
        }

        Rows = [.. (ShowsWeeks ? PlayHistory.WeekRows(listed, now) : PlayHistory.Rows(listed, now)).Take(VisibleRows)];
    }

    partial void OnViewIndexChanged(int value) => Refresh();

    private static string Describe(PlayStats stats)
    {
        if (stats.Sessions == 0)
        {
            return "ainda sem sessões";
        }

        var count = stats.Sessions == 1 ? "1 sessão" : string.Create(CultureInfo.InvariantCulture, $"{stats.Sessions} sessões");
        return $"em {count} · a média é de {PlayTime.Format(stats.Total / stats.Sessions)}";
    }

    private static IReadOnlyList<WeekBar> BarsOf(IReadOnlyList<PlayWeek> weeks)
    {
        var max = weeks.Max(w => w.Total);
        return
        [
            .. weeks.Select((week, i) => new WeekBar(
                max == TimeSpan.Zero ? ChartMinBar : Math.Max(ChartMinBar, Math.Round(ChartHeight * week.Total / max)),
                i == weeks.Count - 1,
                week.Sessions == 0 ? "Sem jogo" : PlayTime.Format(week.Total))),
        ];
    }

    private static string Hms(TimeSpan elapsed) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}");
}
