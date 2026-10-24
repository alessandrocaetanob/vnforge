using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Um chip de filtro de status da grade: o rótulo, quantas linhas passam e se é o filtro ativo.</summary>
public sealed partial class FilterChipViewModel(LineStatusFilter filter, string label) : ObservableObject
{
    public LineStatusFilter Filter { get; } = filter;

    public string Label { get; } = label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountLabel))]
    public partial int Count { get; set; }

    public string CountLabel => ProjectLabels.Number(Count);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
