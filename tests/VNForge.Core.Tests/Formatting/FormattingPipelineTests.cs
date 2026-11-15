using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Formatting;

[Trait("Risco", "R4")]
public class FormattingPipelineTests
{
    private const string AllMarkers = @"\cOlá\u, {VAR:nome}!\nUma barra \\ e uma chave \{ literais.\pOutra caixa {RUBY:a|b} fim";

    [Fact]
    public void Etapa_que_mexe_num_marcador_e_recusada_citando_a_etapa()
    {
        var pipeline = new FormattingPipeline([new DelegateStage("destruidora", FormattingPhase.Typography, (t, _) => t.Replace(@"\c", string.Empty, StringComparison.Ordinal))]);

        var ex = Assert.Throws<FormattingException>(() => pipeline.Format(AllMarkers));

        Assert.Contains("destruidora", ex.Message);
    }

    [Fact]
    public void Etapa_que_reordena_marcadores_e_recusada()
    {
        var pipeline = new FormattingPipeline([new DelegateStage("reordena", FormattingPhase.Typography, (t, _) => @"\u\c")]);

        Assert.Throws<FormattingException>(() => pipeline.Format(@"\c\u"));
    }

    [Fact]
    public void Etapa_que_deixa_uma_barra_solta_e_recusada()
    {
        var pipeline = new FormattingPipeline([new DelegateStage("barra", FormattingPhase.Typography, (t, _) => t + "\\")]);

        Assert.Throws<FormattingException>(() => pipeline.Format("oi"));
    }

    private sealed class DelegateStage(string id, FormattingPhase phase, Func<string, FormattingContext, string> apply) : IFormattingStage
    {
        public string Id => id;

        public FormattingPhase Phase => phase;

        public string Apply(string text, FormattingContext context) => apply(text, context);
    }
}
