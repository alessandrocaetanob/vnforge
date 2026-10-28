using System.Collections.ObjectModel;
using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.ViewModels;

/// <summary>O filtro "Mudou desde o teste": as falas cuja tradução mudou depois do último teste no jogo, que a página Testar e o Painel listam por cena.</summary>
public sealed partial class EditorViewModel
{
    private IReadOnlySet<(string ScriptKey, string UnitId)>? changedLines;

    /// <summary>Os chips que a barra mostra: os cinco de sempre e, depois de "Abrir" na lista do que mudou, o "Mudou desde o teste".</summary>
    private readonly ObservableCollection<FilterChipViewModel> filters;

    /// <summary>Lê o último teste do projeto (<c>.cache/game-state.json</c>); os testes trocam por um fixo. O padrão lê o disco e devolve nulo se o arquivo não lê.</summary>
    public Func<string, GameLastTest?>? LastTestLoader { get; init; }

    /// <summary>
    /// Abre a cena <paramref name="scriptKey"/> só com as falas que mudaram desde o último teste no jogo. Espera a carga em andamento.
    /// Falso, sem mexer no Editor, quando não há projeto, o teste não guardou o retrato ou a cena não existe.
    /// </summary>
    public async Task<bool> ShowChangedSinceTestAsync(string scriptKey)
    {
        await Loading;
        if (Project?.Root is not { } root || data is null)
        {
            return false;
        }

        var script = Scripts.FirstOrDefault(s => string.Equals(s.Key, scriptKey, StringComparison.Ordinal));
        var changes = TranslationChanges.Since(ReadLastTest(root), TextsOf(data));
        if (script is null || changes.State is ChangesSinceTestState.NoTest or ChangesSinceTestState.NoSnapshot)
        {
            return false;
        }

        changedLines = changes.Scenes.SelectMany(scene => scene.UnitIds.Select(id => (scene.Key, id))).ToHashSet();
        var chip = chips.First(c => c.Filter == LineStatusFilter.ChangedSinceTest);
        if (!filters.Contains(chip))
        {
            filters.Add(chip);
        }

        if (!ReferenceEquals(SelectedScript, script))
        {
            SelectedScript = script;
        }

        UpdateCounts();
        if (statusFilter == LineStatusFilter.ChangedSinceTest)
        {
            Refresh();
        }
        else
        {
            SelectFilter(chip);
        }

        return true;
    }

    private GameLastTest? ReadLastTest(string root)
    {
        if (LastTestLoader is { } loader)
        {
            return loader(root);
        }

        try
        {
            return GameStateFile.Load(ProjectFolder.Open(root)).LastTest;
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Dictionary<string, IReadOnlyList<TextUnit>> TextsOf(ProjectLines lines) =>
        lines.Scripts.ToDictionary(s => s.Key, s => (IReadOnlyList<TextUnit>)[.. lines.LinesOf(s.Key).Select(l => l.Unit)], StringComparer.Ordinal);

    private LineFilter FilterFor(LineStatusFilter status, string? speaker) => new(status, speaker) { ChangedLines = changedLines };

    /// <summary>Ao trocar de projeto o filtro some, e a grade volta a mostrar tudo se ele estava ligado.</summary>
    private void ClearChangedSinceTest()
    {
        changedLines = null;
        var chip = chips.First(c => c.Filter == LineStatusFilter.ChangedSinceTest);
        filters.Remove(chip);
        if (statusFilter == LineStatusFilter.ChangedSinceTest)
        {
            SelectFilter(chips[0]);
        }
    }
}
