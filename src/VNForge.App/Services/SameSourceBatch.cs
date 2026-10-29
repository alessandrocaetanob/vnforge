using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.Core.Projects;

namespace VNForge.App.Services;

/// <summary>
/// O "Usar em todos" que está gravando no Editor: o andamento e o cancelamento ficam aqui, e não no painel da linha, para que trocar de linha no meio
/// do lote não perca o "Cancelar" nem deixe começar um segundo lote por cima. O Editor tem um só; cada painel de linha o observa.
/// </summary>
public sealed partial class SameSourceBatch : ObservableObject
{
    private CancellationTokenSource? cancellation;

    /// <summary>Há um lote gravando.</summary>
    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    /// <summary>O andamento, de 0 a 1.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    /// <summary>O que o lote está fazendo, com a porcentagem; vazio quando não há lote.</summary>
    [ObservableProperty]
    public partial string Text { get; private set; } = string.Empty;

    /// <summary>Começa um lote. Falso quando já há um: só um "Usar em todos" grava por vez.</summary>
    public bool TryStart(out CancellationToken token)
    {
        if (IsRunning)
        {
            token = CancellationToken.None;
            return false;
        }

        cancellation = new CancellationTokenSource();
        token = cancellation.Token;
        Progress = 0;
        Text = Describe(0);
        IsRunning = true;
        return true;
    }

    /// <summary>Mostra o andamento do lote que está rodando; depois de <see cref="Finish"/> não faz nada.</summary>
    public void Report(BatchProgress progress)
    {
        if (!IsRunning)
        {
            return;
        }

        Progress = progress.Fraction;
        Text = Describe(progress.Fraction);
    }

    /// <summary>Pede ao lote que pare entre um script e outro; o que já foi gravado fica gravado.</summary>
    public void Cancel() => cancellation?.Cancel();

    /// <summary>Encerra o lote, terminando, cancelado ou com falha.</summary>
    public void Finish()
    {
        var source = cancellation;
        cancellation = null;
        IsRunning = false;
        Progress = 0;
        Text = string.Empty;
        source?.Dispose();
    }

    private static string Describe(double fraction) => $"Aplicando nas outras linhas... {(int)(fraction * 100)}%";
}
