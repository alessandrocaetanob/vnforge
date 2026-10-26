using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>
/// Um capítulo na lista de cenas do Editor: o grupo que o Core propõe (ou que a pessoa escolheu) com as cenas dele. O cabeçalho dele na lista abre e fecha o
/// grupo; o menu "Mover para o capítulo" chama <see cref="MoveHereCommand"/>.
/// </summary>
public sealed partial class ChapterGroupViewModel : ObservableObject
{
    /// <param name="totals">O grupo como <see cref="ScriptGrouping.Build"/> o devolve.</param>
    /// <param name="scripts">As cenas do grupo, na ordem numérica.</param>
    /// <param name="moveHere">Move as cenas marcadas na lista para este capítulo.</param>
    public ChapterGroupViewModel(ScriptGroupTotals totals, IReadOnlyList<ScriptItemViewModel> scripts, Func<ChapterGroupViewModel, Task> moveHere)
    {
        ArgumentNullException.ThrowIfNull(totals);
        ArgumentNullException.ThrowIfNull(moveHere);
        Name = totals.Name;
        Number = totals.Number;
        IsExtra = totals.IsExtra;
        Scripts = scripts;
        MoveHereCommand = new AsyncRelayCommand(() => moveHere(this));
    }

    public string Name { get; }

    /// <summary>A posição na história; nula em "Menus e nomes".</summary>
    public int? Number { get; }

    public bool IsExtra { get; }

    public IReadOnlyList<ScriptItemViewModel> Scripts { get; }

    /// <summary>A contagem à direita do cabeçalho: quantas cenas o capítulo tem.</summary>
    public string CountText => ProjectLabels.Number(Scripts.Count);

    /// <summary>O grupo está aberto na lista; fechado, só o cabeçalho aparece.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    /// <summary>O item "Mover para o capítulo > este" do menu da cena.</summary>
    public IAsyncRelayCommand MoveHereCommand { get; }
}
