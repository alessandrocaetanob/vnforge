using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Página do Editor. Por enquanto só recebe o projeto aberto; a grade e o inspetor entram com a E10.2.</summary>
public sealed partial class EditorViewModel : ObservableObject
{
    [ObservableProperty]
    public partial ProjectSummary? Project { get; set; }
}
