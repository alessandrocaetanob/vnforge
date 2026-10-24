using VNForge.App.Controls;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Controls;

public class ControlLogicTests
{
    [Theory]
    [InlineData(SeverityLevel.Error, 1, "1 erro")]
    [InlineData(SeverityLevel.Error, 2, "2 erros")]
    [InlineData(SeverityLevel.Warning, 1, "1 aviso")]
    [InlineData(SeverityLevel.Warning, 37, "37 avisos")]
    [InlineData(SeverityLevel.Warning, 1204, "1.204 avisos")]
    [InlineData(SeverityLevel.Error, null, "Erro")]
    [InlineData(SeverityLevel.Warning, null, "Aviso")]
    [InlineData(SeverityLevel.Ok, 5, "Sem problemas")]
    [InlineData(SeverityLevel.Ok, null, "Sem problemas")]
    public void Severidade_tem_plural_e_milhar_em_pt_br(SeverityLevel level, int? count, string expected) =>
        Assert.Equal(expected, SeverityText.Format(level, count));

    [Fact]
    public void A_etapa_atual_e_a_primeira_nao_concluida()
    {
        Assert.Equal(
            [PipelineStepState.Done, PipelineStepState.Active, PipelineStepState.Pending, PipelineStepState.Pending, PipelineStepState.Pending],
            PipelineStepper.Resolve([true, false, false, false, false]));
    }

    [Fact]
    public void Etapa_pendente_depois_de_uma_atual_continua_pendente_mesmo_se_concluida_antes()
    {
        Assert.Equal(
            [PipelineStepState.Done, PipelineStepState.Active, PipelineStepState.Done],
            PipelineStepper.Resolve([true, false, true]));
    }

    [Fact]
    public void Tudo_concluido_nao_tem_etapa_atual() =>
        Assert.DoesNotContain(PipelineStepState.Active, PipelineStepper.Resolve([true, true, true]));

    [Fact]
    public void Legenda_da_distribuicao_segue_a_ordem_fixa_do_design()
    {
        var entries = LegendEntry.ForDistribution(new StatusDistribution(21, 18, 6, 17, 38));

        Assert.Equal(["Aprovadas 21%", "Revisadas 18%", "Traduzidas 6%", "IA 17%", "Pendentes 38%"], entries.Select(e => e.Text));
        Assert.Equal(
            [LegendTone.Approved, LegendTone.Reviewed, LegendTone.Translated, LegendTone.Machine, LegendTone.Pending],
            entries.Select(e => e.Tone));
    }
}
