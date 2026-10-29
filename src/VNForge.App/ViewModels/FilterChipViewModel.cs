using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Um chip de filtro de status da grade: o rótulo, quantas linhas passam e se é o filtro ativo.</summary>
public sealed partial class FilterChipViewModel(LineStatusFilter filter, string label) : ObservableObject
{
    public LineStatusFilter Filter { get; } = filter;

    public string Label { get; } = label;

    /// <summary>O chip só aparece quando há linhas nele ou quando é o filtro ativo: um filtro de marca que ninguém pôs ainda não ocupa a barra.</summary>
    public bool HidesWhenEmpty { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountLabel), nameof(IsShown))]
    public partial int Count { get; set; }

    public string CountLabel => ProjectLabels.Number(Count);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    public partial bool IsSelected { get; set; }

    public bool IsShown => !HidesWhenEmpty || Count > 0 || IsSelected;
}
