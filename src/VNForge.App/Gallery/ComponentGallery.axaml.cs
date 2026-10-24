using Avalonia.Controls;
using VNForge.App.Controls;
using VNForge.Core.Projects;

namespace VNForge.App.Gallery;

/// <summary>Os controles do design system em todos os estados, para conferir contra o design. Só existe no build Debug.</summary>
public sealed partial class ComponentGallery : UserControl
{
    public static readonly StatusDistribution SampleDistribution = new(21, 18, 6, 17, 38);

    public ComponentGallery()
    {
        InitializeComponent();
        DistributionLegend.Value = SampleDistribution;
        Bar.Value = SampleDistribution;
        CardBar.Value = SampleDistribution;
    }
}
