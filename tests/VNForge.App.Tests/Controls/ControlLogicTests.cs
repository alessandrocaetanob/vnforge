using VNForge.App.Controls;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Controls;

public class ControlLogicTests
{

    [Fact]
    public void A_etapa_atual_e_a_primeira_nao_concluida()
    {
        Assert.Equal(
            [PipelineStepState.Done, PipelineStepState.Active, PipelineStepState.Pending, PipelineStepState.Pending, PipelineStepState.Pending],
            PipelineStepper.Resolve([true, false, false, false, false]));
    }
}
