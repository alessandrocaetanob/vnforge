using VNForge.AI.Claude;
using VNForge.AI.Eval;
using VNForge.Core.Eval;

namespace VNForge.AI.Tests.Eval;

public sealed class EvalConfigParserTests
{
    [Fact]
    public void Le_modelo_esforco_contexto_e_biblia()
    {
        Assert.Equal(new EvalConfig("A", ClaudeModels.Sonnet, "medium", 15, true), EvalConfigParser.Parse("sonnet:medium:15:com-biblia", "A"));
        Assert.Equal(new EvalConfig("B", ClaudeModels.Haiku, "low", 2, false), EvalConfigParser.Parse("HAIKU:low:2:sem-biblia", "B"));
    }

    [Fact]
    public void Omitir_usa_os_padroes_e_aceita_o_id_completo_do_modelo()
    {
        Assert.Equal(new EvalConfig("A", ClaudeModels.Opus, "medium", EvalConfigParser.DefaultContextLines, false), EvalConfigParser.Parse("opus", "A"));
        Assert.Equal(ClaudeModels.Fable, EvalConfigParser.Parse("claude-fable-5-1:high", "A").Model);
        Assert.Equal("high", EvalConfigParser.Parse("claude-fable-5-1:high", "A").Effort);
    }

    [Theory]
    [InlineData("")]
    [InlineData("gpt")]
    [InlineData("opus:enorme")]
    [InlineData("opus:low:muitas")]
    [InlineData("opus:low:-1")]
    [InlineData("opus:low:31")]
    [InlineData("opus:low:5:talvez")]
    [InlineData("opus:low:5:com-biblia:extra")]
    public void Recusa_configuracao_invalida_em_portugues(string spec)
    {
        var error = Assert.Throws<EvalException>(() => EvalConfigParser.Parse(spec, "A"));

        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void Da_a_cada_configuracao_a_letra_da_posicao()
    {
        var configs = EvalConfigParser.ParseAll(["opus", "sonnet", "haiku"]);

        Assert.Equal(["A", "B", "C"], configs.Select(c => c.Id));
    }

    [Fact]
    public void Recusa_mais_de_26_configuracoes()
    {
        Assert.Throws<EvalException>(() => EvalConfigParser.ParseAll(Enumerable.Repeat("opus", 27)));
    }
}
