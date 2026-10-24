using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VNForge.App.Services;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>Um aviso curto na tela. Some sozinho; "Desfazer" aparece só quando quem avisou sabe desfazer.</summary>
public sealed partial class ToastItem : ObservableObject
{
    private readonly Action<ToastItem> dismiss;
    private readonly Action? undo;

    internal ToastItem(string? key, string message, ToastKind kind, string? undoLabel, Action? undo, Action<ToastItem> dismiss)
    {
        Key = key;
        Message = message;
        Kind = kind;
        UndoLabel = undo is null ? null : undoLabel ?? "Desfazer";
        this.undo = undo;
        this.dismiss = dismiss;
    }

    /// <summary>Avisos com a mesma chave se substituem (várias gravações seguidas viram um aviso só).</summary>
    public string? Key { get; }

    public string Message { get; }

    public ToastKind Kind { get; }

    public string? UndoLabel { get; }

    public bool CanUndo => undo is not null;

    public bool IsSuccess => Kind == ToastKind.Success;

    public bool IsWarning => Kind == ToastKind.Warning;

    public bool IsError => Kind == ToastKind.Error;

    public bool IsInfo => Kind == ToastKind.Info;

    internal IDisposable? Timer { get; set; }

    [RelayCommand]
    private void Undo()
    {
        dismiss(this);
        undo?.Invoke();
    }

    [RelayCommand]
    private void Dismiss() => dismiss(this);
}

/// <summary>Fila de avisos curtos (gravação, build pronto, injeção) que o <c>ToastHost</c> desenha no canto da janela.</summary>
public sealed class ToastService
{
    public const int MaxVisible = 3;

    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

    private readonly TimeProvider clock;
    private readonly Action<Action> postToUi;

    /// <param name="postToUi">Executa a remoção na thread da interface; os testes passam uma chamada direta.</param>
    public ToastService(TimeProvider? clock = null, Action<Action>? postToUi = null)
    {
        this.clock = clock ?? TimeProvider.System;
        this.postToUi = postToUi ?? (a => Avalonia.Threading.Dispatcher.UIThread.Post(a));
    }

    public ObservableCollection<ToastItem> Toasts { get; } = [];

    public ToastItem Show(string message, ToastKind kind = ToastKind.Info, string? key = null, string? undoLabel = null, Action? undo = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (key is not null)
        {
            foreach (var same in Toasts.Where(t => t.Key == key).ToList())
            {
                Dismiss(same);
            }
        }

        var toast = new ToastItem(key, message, kind, undoLabel, undo, Dismiss);
        Toasts.Add(toast);
        while (Toasts.Count > MaxVisible)
        {
            Dismiss(Toasts[0]);
        }

        toast.Timer = clock.CreateTimer(_ => postToUi(() => Dismiss(toast)), null, Lifetime, Timeout.InfiniteTimeSpan);
        return toast;
    }

    public void Dismiss(ToastItem toast)
    {
        toast.Timer?.Dispose();
        toast.Timer = null;
        Toasts.Remove(toast);
    }

    public void Clear()
    {
        foreach (var toast in Toasts.ToList())
        {
            Dismiss(toast);
        }
    }
}
