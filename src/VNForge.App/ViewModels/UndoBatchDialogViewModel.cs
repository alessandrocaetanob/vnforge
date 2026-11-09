using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>
/// "Desfazer o lote": o diálogo diz a decisão e o efeito antes de o lote noturno voltar. As linhas que a pessoa editou depois do lote ficam como estão,
/// como no ponto de restauração do #478.
/// </summary>
public sealed partial class UndoBatchDialogViewModel : ObservableObject
{
    private TaskCompletionSource<bool>? answer;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Message { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string RestoreText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string KeptText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmLabel { get; private set; } = "Desfazer";

    /// <summary>Há linha a devolver; sem isso o botão fica desligado.</summary>
    [ObservableProperty]
    public partial bool CanConfirm { get; private set; }

    /// <summary>Abre o diálogo e espera a resposta: <see langword="true"/> desfaz, <see langword="false"/> mantém o lote (inclusive Escape e o X).</summary>
    /// <param name="batchTitle">O nome do lote ("Sala de aula").</param>
    /// <param name="submittedAt">Quando o lote saiu, na hora local de quem usa.</param>
    public Task<bool> AskAsync(string batchTitle, DateTimeOffset submittedAt, RestoreUndoResult preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        Settle(false);
        var total = preview.Restored + preview.EditedSince;
        var when = submittedAt.ToString("dd/MM", CultureInfo.InvariantCulture);
        Title = $"Desfazer o lote {OfBatch(batchTitle)}?";
        var edited = preview.EditedSince == 0
            ? string.Empty
            : preview.EditedSince == 1
                ? " A linha que você editou depois fica como está."
                : $" As {ProjectLabels.Number(preview.EditedSince)} linhas que você editou depois ficam como estão.";
        Message = $"Volta {Lines(total)} ao que eram antes do lote de {when}.{edited}";
        RestoreText = Lines(preview.Restored);
        KeptText = Lines(preview.EditedSince);
        ConfirmLabel = $"Desfazer {Lines(preview.Restored)}";
        CanConfirm = preview.Restored > 0;
        ConfirmCommand.NotifyCanExecuteChanged();
        answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        IsOpen = true;
        return answer.Task;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => Close(true);

    [RelayCommand]
    private void Keep() => Close(false);

    partial void OnIsOpenChanged(bool value)
    {
        if (!value)
        {
            Settle(false);
        }
    }

    private void Close(bool result)
    {
        Settle(result);
        IsOpen = false;
    }

    private void Settle(bool result)
    {
        var pending = answer;
        answer = null;
        pending?.TrySetResult(result);
    }

    /// <summary>
    /// O nome do lote com a preposição, sem aspas: "Sala de aula" vira "da Sala de aula"; os escopos que já abrem com artigo contraem
    /// ("O capítulo" vira "do capítulo", "Esta cena" vira "desta cena", "As cenas escolhidas" vira "das cenas escolhidas").
    /// </summary>
    internal static string OfBatch(string name)
    {
        foreach (var (article, contracted) in Contractions)
        {
            if (name.StartsWith(article, StringComparison.Ordinal) && name.Length > article.Length)
            {
                return contracted + name[article.Length..];
            }
        }

        return "da " + name;
    }

    private static readonly (string Article, string Contracted)[] Contractions =
    [
        ("Esta ", "desta "), ("Este ", "deste "), ("Estas ", "destas "), ("Estes ", "destes "),
        ("As ", "das "), ("Os ", "dos "), ("O ", "do "), ("A ", "da "),
    ];

    private static string Lines(int count) => count == 1 ? "1 linha" : $"{ProjectLabels.Number(count)} linhas";
}
