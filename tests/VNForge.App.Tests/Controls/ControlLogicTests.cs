using VNForge.App.Controls;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Controls;

public class ControlLogicTests
{
    [Theory]
    [InlineData(160, 250, 1.0, 160)]
    [InlineData(160, 250, 1.5, 160)]
    [InlineData(160, 250, 2.0, 125)]
    [InlineData(160, 208, 1.5, 138)]
    [InlineData(72, 250, 2.0, 72)]
    [InlineData(160, 87, 1.0, 87)]
    [InlineData(160, 0, 1.5, 160)]
    public void O_retrato_nunca_passa_da_origem_vezes_a_escala(double draw, int source, double scale, double expected)
    {
        Assert.Equal(expected, Portrait.DisplaySide(draw, source, scale));
    }

    [Fact]
    public void A_etapa_atual_e_a_primeira_nao_concluida()
    {
        Assert.Equal(
            [PipelineStepState.Done, PipelineStepState.Active, PipelineStepState.Pending, PipelineStepState.Pending, PipelineStepState.Pending],
            PipelineStepper.Resolve([true, false, false, false, false]));
    }
}
