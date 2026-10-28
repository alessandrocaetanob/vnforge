using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Uma cena que mudou desde o último teste: o nome como o Editor a chama, quantas falas mudaram e a chave que o "Abrir" leva ao Editor.</summary>
public sealed record ChangedSceneRow(string Key, string Name, string LinesText);

/// <summary>
/// "Mudou desde o último teste": a lista de cenas cuja tradução é diferente da que foi para o jogo, na ordem da lista de cenas do Editor, com quantas falas
/// mudaram em cada uma. A página Testar e o Painel mostram a mesma lista. Sem retrato do último teste (arquivo de uma versão anterior, teste só dos acentos,
/// nenhum teste) a lista fica vazia e <see cref="Message"/> explica por quê.
/// </summary>
public sealed partial class ChangedScenesViewModel : ObservableObject
{
    /// <summary>Quantas cenas aparecem na lista; o resto vira "e mais N cenas".</summary>
    public const int MaxRows = 8;

    public ObservableCollection<ChangedSceneRow> Rows { get; } = [];

    /// <summary>A frase de cima: "3 cenas mudaram desde o teste de 22/10 · 14 falas" ou o motivo de a lista estar vazia.</summary>
    [ObservableProperty]
    public partial string Message { get; private set; } = string.Empty;

    /// <summary>"e mais 4 cenas", quando a lista tem mais do que cabe.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMore))]
    public partial string MoreText { get; private set; } = string.Empty;

    public bool HasMore => MoreText.Length > 0;

    public bool HasRows => Rows.Count > 0;

    /// <summary>
    /// O último teste guardou o retrato, então há o que dizer: cenas mudadas ou "nada mudou". O Painel esconde a lista nos outros casos, porque não sobra altura
    /// para a explicação; a página Testar, que tem o espaço, mostra o motivo.
    /// </summary>
    [ObservableProperty]
    public partial bool IsInformative { get; private set; }

    /// <summary>"Abrir" numa cena: a janela leva ao Editor, nessa cena, só com as falas que mudaram.</summary>
    public event EventHandler<string>? OpenRequested;

    /// <param name="sceneName">O nome da cena pela chave e pela posição (<see cref="ChangedScene.Number"/>), o mesmo que o Editor mostra.</param>
    public void Update(ChangesSinceTest changes, Func<ChangedScene, string> sceneName)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(sceneName);
        Rows.Clear();
        MoreText = string.Empty;
        foreach (var scene in changes.Scenes.Take(MaxRows))
        {
            Rows.Add(new ChangedSceneRow(scene.Key, sceneName(scene), Lines(scene.Lines)));
        }

        var rest = changes.Scenes.Count - Rows.Count;
        if (rest > 0)
        {
            MoreText = rest == 1 ? "e mais 1 cena" : $"e mais {ProjectLabels.Number(rest)} cenas";
        }

        Message = MessageFor(changes);
        IsInformative = changes.State is ChangesSinceTestState.Changed or ChangesSinceTestState.Unchanged;
        OnPropertyChanged(nameof(HasRows));
    }

    public void Clear() => Update(ChangesSinceTest.None, _ => string.Empty);

    [RelayCommand]
    private void Open(ChangedSceneRow? row)
    {
        if (row is not null)
        {
            OpenRequested?.Invoke(this, row.Key);
        }
    }

    private static string MessageFor(ChangesSinceTest changes) => changes.State switch
    {
        ChangesSinceTestState.NoTest => "Ainda não houve teste neste projeto. Depois do primeiro, esta lista mostra o que mudou desde ele.",
        ChangesSinceTestState.NoSnapshot when changes.AccentOnly =>
            $"O último teste, de {DateText(changes.TestAt)}, foi só dos acentos e não levou a tradução. Teste a tradução no jogo e esta lista passa a mostrar o que mudou depois dele.",
        ChangesSinceTestState.NoSnapshot =>
            $"O último teste, de {DateText(changes.TestAt)}, é de uma versão anterior do VNForge e não guardou as traduções. Teste de novo e esta lista passa a mostrar o que mudou depois dele.",
        ChangesSinceTestState.Unchanged => $"Nenhuma cena mudou desde o teste de {DateText(changes.TestAt)}.",
        _ => $"{Scenes(changes.Scenes.Count)} desde o teste de {DateText(changes.TestAt)} · {Lines(changes.TotalLines)}",
    };

    private static string Scenes(int count) => count == 1 ? "1 cena mudou" : $"{ProjectLabels.Number(count)} cenas mudaram";

    private static string Lines(int count) => count == 1 ? "1 fala" : $"{ProjectLabels.Number(count)} falas";

    private static string DateText(DateTimeOffset? at) => at is { } value ? value.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture) : string.Empty;
}
