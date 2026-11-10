using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core.Library;

namespace VNForge.App.ViewModels;

/// <summary>Uma captura da grade: o arquivo PNG e a data e a hora do rótulo ("08/11 21:14").</summary>
public sealed record GameCaptureItem(string File, string Path, string Label);

/// <summary>
/// A aba Jogar do jogo (#553): as capturas feitas pelo atalho F9, a pasta delas, as notas livres do jogo e o caminho para o Editor. As capturas e as notas são do jogo
/// da Biblioteca ligado ao projeto aberto; sem ele a página só avisa. O atalho só fica registrado enquanto a página está aberta (<see cref="Enter"/> e <see cref="Leave"/>),
/// para não tomar a F9 dos outros programas.
/// </summary>
public sealed partial class GameCapturesViewModel : ObservableObject
{
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(600);

    private static readonly TimeSpan AgeTick = TimeSpan.FromSeconds(1);

    private LibraryGame? game;
    private IDisposable? registration;
    private ITimer? saveTimer;
    private ITimer? ageTimer;
    private DateTimeOffset? savedAt;
    private bool loading;
    private int capturingNow;

    /// <summary>O índice e as imagens das capturas; nulo (os testes do shell) deixa a página sem o que guardar.</summary>
    public GameCapturesFile? CapturesFile { get; set; }

    public GameNotesFile? NotesFile { get; set; }

    public IWindowCapturer? Capturer { get; set; }

    public IGlobalHotkey? Hotkey { get; set; }

    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>Leva uma ação à thread da tela: o atalho dispara numa thread de fundo. A interface passa <c>Dispatcher.UIThread.Post</c>.</summary>
    public Action<Action> PostToUi { get; set; } = action => action();

    public Action<string> OpenFolder { get; set; } = _ => { };

    public Action? ShowEditor { get; set; }

    public ObservableCollection<GameCaptureItem> Captures { get; } = [];

    /// <summary>A última captura em andamento; os testes a aguardam.</summary>
    public Task Capturing { get; private set; } = Task.CompletedTask;

    public bool HasGame => game is not null;

    public bool HasNoGame => game is null;

    public bool HasCaptures => Captures.Count > 0;

    public bool HasNoCaptures => Captures.Count == 0;

    [ObservableProperty]
    public partial string NoteText { get; set; } = string.Empty;

    /// <summary>"Salvo há 4 segundos"; vazio enquanto o jogo não tem nota.</summary>
    [ObservableProperty]
    public partial string SavedText { get; private set; } = string.Empty;

    /// <summary>A última captura saiu toda preta: jogo em tela cheia exclusiva, que o app não consegue ler.</summary>
    [ObservableProperty]
    public partial bool BlackWarning { get; private set; }

    /// <summary>Outro programa já usa a F9: a captura por atalho não funciona até ele largar a tecla.</summary>
    [ObservableProperty]
    public partial bool HotkeyTaken { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    partial void OnNoteTextChanged(string value)
    {
        if (!loading && game is not null)
        {
            ScheduleSave();
        }
    }

    /// <summary>Abre a página para o jogo (nulo quando o projeto não está na Biblioteca): lê as capturas e a nota e liga o atalho.</summary>
    public void Enter(LibraryGame? opened)
    {
        Leave();
        game = opened;
        OnPropertyChanged(nameof(HasGame));
        OnPropertyChanged(nameof(HasNoGame));
        BlackWarning = false;
        HotkeyTaken = false;
        ErrorMessage = null;
        Captures.Clear();
        loading = true;
        NoteText = string.Empty;
        SavedText = string.Empty;
        savedAt = null;
        loading = false;
        NotifyCaptures();
        if (opened is null)
        {
            return;
        }

        Reload(opened);
        if (Hotkey is { } hotkey)
        {
            registration = hotkey.Register(OnHotkey);
            HotkeyTaken = registration is null;
        }
    }

    /// <summary>Sai da página: grava a nota pendente e solta o atalho.</summary>
    public void Leave()
    {
        Flush();
        registration?.Dispose();
        registration = null;
        ageTimer?.Dispose();
        ageTimer = null;
    }

    private void Reload(LibraryGame opened)
    {
        try
        {
            if (CapturesFile is { } captures)
            {
                foreach (var capture in captures.Load().Of(opened.Id).OrderByDescending(c => c.CapturedAt))
                {
                    Captures.Add(ItemOf(captures, opened.Id, capture));
                }
            }

            if (NotesFile?.Load().Of(opened.Id) is { } note)
            {
                loading = true;
                NoteText = note.Text;
                loading = false;
                savedAt = note.UpdatedAt;
                RefreshSavedText();
                StartAgeTimer();
            }
        }
        catch (LibraryException e)
        {
            ErrorMessage = e.Message;
        }

        NotifyCaptures();
    }

    private static GameCaptureItem ItemOf(GameCapturesFile file, string gameId, GameCapture capture) =>
        new(capture.File, file.PathOf(gameId, capture), capture.CapturedAt.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture));

    private void NotifyCaptures()
    {
        OnPropertyChanged(nameof(HasCaptures));
        OnPropertyChanged(nameof(HasNoCaptures));
    }

    /// <summary>Uma captura de cada vez: a F9 apertada enquanto uma roda é ignorada, porque duas gravariam o índice ao mesmo tempo.</summary>
    private void OnHotkey()
    {
        if (Interlocked.CompareExchange(ref capturingNow, 1, 0) == 0)
        {
            Capturing = CaptureAsync();
        }
    }

    /// <summary>Captura a janela do jogo e a guarda com a data. Roda fora da thread da tela; o resultado entra na grade pela thread da tela.</summary>
    private async Task CaptureAsync()
    {
        try
        {
            if (game is not { } target || CapturesFile is not { } captures || Capturer is not { } capturer)
            {
                return;
            }

            var outcome = await Task.Run<CaptureOutcome>(() =>
            {
                try
                {
                    var image = capturer.Capture(new WindowTarget(Folder: target.Folder));
                    return new CaptureOutcome(captures.Save(target.Id, image), IsBlack(image), null);
                }
                catch (Exception e)
                {
                    // Roda numa thread de fundo que ninguém aguarda: qualquer falha vira o aviso da página.
                    return new CaptureOutcome(null, false, e is LibraryException ? e.Message : $"A captura falhou: {e.Message}");
                }
            });
            PostToUi(() => Apply(target, captures, outcome.Capture, outcome.Black, outcome.Error));
        }
        finally
        {
            Volatile.Write(ref capturingNow, 0);
        }
    }

    private sealed record CaptureOutcome(GameCapture? Capture, bool Black, string? Error);

    private void Apply(LibraryGame target, GameCapturesFile captures, GameCapture? capture, bool black, string? error)
    {
        if (game?.Id != target.Id)
        {
            return;
        }

        ErrorMessage = error;
        if (capture is null)
        {
            return;
        }

        BlackWarning = black;
        Captures.Insert(0, ItemOf(captures, target.Id, capture));
        NotifyCaptures();
    }

    /// <summary>Sem nenhum pixel com cor: a janela não entregou a imagem (tela cheia exclusiva).</summary>
    private static bool IsBlack(WindowImage image)
    {
        var bgra = image.Bgra;
        for (var at = 0; at < bgra.Length; at += 4)
        {
            if ((bgra[at] | bgra[at + 1] | bgra[at + 2]) != 0)
            {
                return false;
            }
        }

        return true;
    }

    [RelayCommand]
    private void OpenCapturesFolder()
    {
        if (game is null || CapturesFile is not { } captures)
        {
            return;
        }

        var folder = Path.Combine(captures.Directory, game.Id);
        try
        {
            Directory.CreateDirectory(folder);
            OpenFolder(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"A pasta das capturas não pôde ser aberta: {e.Message}";
        }
    }

    [RelayCommand]
    private void ShowInEditor() => ShowEditor?.Invoke();

    private void ScheduleSave()
    {
        if (saveTimer is null)
        {
            saveTimer = Clock.CreateTimer(_ => PostToUi(Flush), null, SaveDelay, Timeout.InfiniteTimeSpan);
        }
        else
        {
            saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Grava já a nota que ainda espera o <see cref="SaveDelay"/>.</summary>
    public void Flush()
    {
        if (saveTimer is null)
        {
            return;
        }

        saveTimer.Dispose();
        saveTimer = null;
        if (game is not { } target || NotesFile is not { } notes)
        {
            return;
        }

        try
        {
            var now = Clock.GetLocalNow();
            var saved = notes.Update(all => all.Set(target.Id, NoteText, now));
            ErrorMessage = null;
            savedAt = saved is null ? null : now;
            RefreshSavedText();
            StartAgeTimer();
        }
        catch (LibraryException e)
        {
            ErrorMessage = e.Message;
        }
    }

    private void StartAgeTimer() => ageTimer ??= savedAt is null ? null : Clock.CreateTimer(_ => PostToUi(RefreshSavedText), null, AgeTick, AgeTick);

    private void RefreshSavedText()
    {
        if (savedAt is not { } at)
        {
            SavedText = string.Empty;
            return;
        }

        var elapsed = Clock.GetLocalNow() - at;
        SavedText = elapsed.TotalSeconds < 1 ? "Salvo agora"
            : elapsed < TimeSpan.FromMinutes(1) ? $"Salvo há {(int)elapsed.TotalSeconds} {((int)elapsed.TotalSeconds == 1 ? "segundo" : "segundos")}"
            : elapsed < TimeSpan.FromHours(1) ? $"Salvo há {(int)elapsed.TotalMinutes} min"
            : elapsed < TimeSpan.FromDays(1) ? $"Salvo há {(int)elapsed.TotalHours} h"
            : $"Salvo há {(int)elapsed.TotalDays} dias";
    }
}
