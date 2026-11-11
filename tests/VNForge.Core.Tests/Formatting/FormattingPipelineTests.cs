using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Formatting;

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

    [Theory]
    [InlineData(@"\c", @"\u")]
    [InlineData("{VAR:nome}", "{VAR:outro}")]
    [InlineData(@"\\", @"\{")]
    public void Etapa_que_troca_um_marcador_por_outro_e_recusada(string from, string to)
    {
        var pipeline = new FormattingPipeline([new DelegateStage("troca", FormattingPhase.Typography, (t, _) => t.Replace(from, to, StringComparison.Ordinal))]);

        Assert.Throws<FormattingException>(() => pipeline.Format(AllMarkers));
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

    [Fact]
    public void Etapa_que_declara_quebra_de_linha_pode_inserir_n_mas_nao_tirar_os_outros_marcadores()
    {
        var inserts = new FormattingPipeline([new DelegateStage("quebra", FormattingPhase.LineBreak, (t, _) => t.Replace(" ", @"\n", StringComparison.Ordinal), mayChangeLineBreaks: true)]);
        var drops = new FormattingPipeline([new DelegateStage("quebra", FormattingPhase.LineBreak, (t, _) => t.Replace(@"\p", string.Empty, StringComparison.Ordinal), mayChangeLineBreaks: true)]);

        Assert.Equal(@"uma\nfrase", inserts.Format("uma frase").Text);
        Assert.Throws<FormattingException>(() => drops.Format(@"a\pb"));
        var dropsBreak = new FormattingPipeline([new DelegateStage("quebra", FormattingPhase.LineBreak, (t, _) => t.Replace(@"\n", " ", StringComparison.Ordinal), mayChangeLineBreaks: true)]);
        Assert.Throws<FormattingException>(() => dropsBreak.Format(@"a\nb"));
    }

    [Fact]
    public void Quebra_ligada_no_perfil_entra_na_fase_de_quebra_e_preserva_todos_os_marcadores()
    {
        var profile = FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: 12, MaxLines: null, CharWidth: CharWidthMode.Uniform) };
        var pipeline = FormattingPipeline.Create(profile);

        var result = pipeline.Format(AllMarkers);

        var stage = Assert.Single(pipeline.Stages);
        Assert.Equal(FormattingPhase.LineBreak, stage.Phase);
        Assert.Contains(@"\n", result.Text);
        Assert.Equal(
            ControlMarkers.Split(AllMarkers).Where(s => s.IsMarker && s.Text != @"\n").Select(s => s.Text),
            ControlMarkers.Split(result.Text).Where(s => s.IsMarker && s.Text != @"\n").Select(s => s.Text));
    }

    private sealed class DelegateStage(string id, FormattingPhase phase, Func<string, FormattingContext, string> apply, bool mayChangeLineBreaks = false) : IFormattingStage
    {
        public string Id => id;

        public FormattingPhase Phase => phase;

        public bool MayChangeLineBreaks => mayChangeLineBreaks;

        public string Apply(string text, FormattingContext context) => apply(text, context);
    }
}
