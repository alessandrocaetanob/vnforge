using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Um chip de filtro de status da grade: o rótulo, quantas linhas passam e se é o filtro ativo.</summary>
public sealed partial class FilterChipViewModel(LineStatusFilter filter, string label, bool hideWhenEmpty = false) : ObservableObject
{
    public LineStatusFilter Filter { get; } = filter;

    public string Label { get; } = label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountLabel), nameof(IsShown))]
    public partial int Count { get; set; }

    public string CountLabel => ProjectLabels.Number(Count);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    public partial bool IsSelected { get; set; }

    /// <summary>O chip que só existe quando há o que filtrar (a dúvida da IA) some da barra enquanto está vazio, a não ser que seja o filtro ativo.</summary>
    public bool IsShown => !hideWhenEmpty || Count > 0 || IsSelected;
}
