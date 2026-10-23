using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Formatting;

public class FormattingPipelineTests
{
    private const string AllMarkers = @"\cOlá\u, {VAR:nome}!\nUma barra \\ e uma chave \{ literais.\pOutra caixa {RUBY:a|b} fim";

    [Fact]
    public void Scanner_separa_texto_e_marcadores_sem_perder_nada()
    {
        var segments = ControlMarkers.Split(AllMarkers);

        Assert.Equal(AllMarkers, string.Concat(segments.Select(s => s.Text)));
        Assert.Equal(
            [@"\c", @"\u", "{VAR:nome}", @"\n", @"\\", @"\{", @"\p", "{RUBY:a|b}"],
            segments.Where(s => s.IsMarker).Select(s => s.Text));
    }

    [Theory]
    [InlineData(@"fim\")]
    [InlineData(@"\x")]
    [InlineData("{aberta")]
    public void Scanner_recusa_marcador_invalido(string text)
    {
        Assert.Throws<ArgumentException>(() => ControlMarkers.Split(text));
    }

    [Fact]
    public void Pipeline_sem_etapas_devolve_o_texto_como_esta()
    {
        var result = new FormattingPipeline([]).Format(AllMarkers);

        Assert.Equal(AllMarkers, result.Text);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Etapas_rodam_na_ordem_das_fases_do_doc_02_e_na_ordem_de_registro_dentro_da_fase()
    {
        var log = new List<string>();
        var pipeline = new FormattingPipeline(
        [
            new SpyStage("depois", FormattingPhase.UserRulesAfter, log),
            new SpyStage("quebra", FormattingPhase.LineBreak, log),
            new SpyStage("tipografia-b", FormattingPhase.Typography, log),
            new SpyStage("antes", FormattingPhase.UserRulesBefore, log),
            new SpyStage("tipografia-a", FormattingPhase.Typography, log),
            new SpyStage("normalizacao", FormattingPhase.Normalization, log),
            new SpyStage("espacos", FormattingPhase.Spacing, log),
            new SpyStage("largura", FormattingPhase.Width, log),
        ]);

        pipeline.Format("x");

        Assert.Equal(["antes", "normalizacao", "espacos", "tipografia-b", "tipografia-a", "largura", "quebra", "depois"], log);
    }

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
    public void Etapa_que_nao_declara_quebra_de_linha_nao_pode_mexer_em_n()
    {
        var pipeline = new FormattingPipeline([new DelegateStage("cria-n", FormattingPhase.Spacing, (t, _) => t.Replace(" ", @"\n", StringComparison.Ordinal))]);

        Assert.Throws<FormattingException>(() => pipeline.Format("uma frase"));
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
    public void TextRunStage_so_enxerga_o_texto_entre_marcadores()
    {
        var seen = new List<string>();
        var stage = new UpperStage(seen);

        var result = new FormattingPipeline([stage]).Format(AllMarkers);

        Assert.Equal(@"\cOLÁ\u, {VAR:nome}!\nUMA BARRA \\ E UMA CHAVE \{ LITERAIS.\pOUTRA CAIXA {RUBY:a|b} FIM", result.Text);
        Assert.DoesNotContain(seen, run => run.Contains('\\') || run.Contains('{'));
    }

    [Fact]
    public void Marcador_invalido_no_texto_de_entrada_lanca_ArgumentException()
    {
        var pipeline = new FormattingPipeline([new UpperStage([])]);

        Assert.Throws<ArgumentException>(() => pipeline.Format(@"oi \x"));
    }

    [Fact]
    public void Perfil_padrao_nao_tem_etapas_e_nao_mexe_no_texto()
    {
        var pipeline = FormattingPipeline.Create(FormattingProfile.Default);

        Assert.Empty(pipeline.Stages);
        Assert.Equal(AllMarkers, pipeline.Format(AllMarkers).Text);
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

    [Fact]
    public void Quebra_informa_a_caixa_que_passa_do_limite_de_linhas()
    {
        var profile = FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: 5, MaxLines: 2, CharWidth: CharWidthMode.Uniform) };

        var result = FormattingPipeline.Create(profile).Format(@"aaaa bbbb cccc\pxx");

        var warning = Assert.Single(result.Warnings);
        Assert.Contains("caixa 1", warning);
        Assert.Contains("3 linhas", warning);
        Assert.Contains("máximo 2", warning);
    }

    [Fact]
    public void Largura_padrao_conta_japones_como_duas_colunas()
    {
        var profile = FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: 6, MaxLines: null) };

        var result = FormattingPipeline.Create(profile).Format("あああ いいい");

        Assert.Equal(@"あああ\nいいい", result.Text);
    }

    private sealed class SpyStage(string id, FormattingPhase phase, List<string> log) : IFormattingStage
    {
        public string Id => id;

        public FormattingPhase Phase => phase;

        public string Apply(string text, FormattingContext context)
        {
            log.Add(id);
            return text;
        }
    }

    private sealed class DelegateStage(string id, FormattingPhase phase, Func<string, FormattingContext, string> apply, bool mayChangeLineBreaks = false) : IFormattingStage
    {
        public string Id => id;

        public FormattingPhase Phase => phase;

        public bool MayChangeLineBreaks => mayChangeLineBreaks;

        public string Apply(string text, FormattingContext context) => apply(text, context);
    }

    private sealed class UpperStage(List<string> seen) : TextRunStage
    {
        public override string Id => "upper";

        public override FormattingPhase Phase => FormattingPhase.Typography;

        protected override string ApplyToRun(string run, FormattingContext context)
        {
            seen.Add(run);
            return run.ToUpperInvariant();
        }
    }
}
