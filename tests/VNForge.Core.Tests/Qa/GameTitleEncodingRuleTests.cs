using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

public class GameTitleEncodingRuleTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> NoTexts = new Dictionary<string, IReadOnlyList<TextUnit>>();

    private static IReadOnlyList<QaIssue> Run(string strategy, GameTitleConfig? title, bool withEncoder = true)
    {
        var encoder = withEncoder ? TextEncoderRegistry.Default.Create(strategy) : null;
        var context = new QaContext(NoTexts, [], [], FormattingProfile.Default, encoder) { GameTitle = title };
        return [.. new GameTitleEncodingRule().Check(context)];
    }

    [Fact]
    public void Titulo_com_acento_em_sjis_e_erro_que_cita_o_caractere()
    {
        var issue = Assert.Single(Run("sjis", new GameTitleConfig("原題", "Título")));

        Assert.Equal(GameTitleEncodingRule.RuleId, issue.RuleId);
        Assert.Equal(QaSeverity.Error, issue.Severity);
        Assert.Equal(GameTitleText.UnitId, issue.UnitId);
        Assert.Contains("í", issue.Message, StringComparison.Ordinal);
        Assert.Contains("sjis", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Titulo_com_acento_em_cp1252_passa() => Assert.Empty(Run("cp1252", new GameTitleConfig("原題", "Título")));

    [Fact]
    public void Titulo_sem_traducao_nao_e_conferido() => Assert.Empty(Run("sjis", new GameTitleConfig("原題", null)));

    [Fact]
    public void Sem_titulo_no_projeto_nao_confere_nada() => Assert.Empty(Run("sjis", null));

    [Fact]
    public void Sem_encoder_no_contexto_nao_confere_nada() => Assert.Empty(Run("sjis", new GameTitleConfig("原題", "Título"), withEncoder: false));

    [Fact]
    public void A_regra_esta_no_conjunto_padrao_e_na_categoria_de_acentuacao()
    {
        Assert.NotNull(QaEngine.Default.Find(GameTitleEncodingRule.RuleId));
        Assert.Equal(QaCategories.Accents, QaCategories.Of(GameTitleEncodingRule.RuleId));
    }
}

public class GameTitleTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sem_traducao_nao_ha_titulo_a_gravar(string? translation)
    {
        var title = GameTitleText.Resolve(translation, TextEncoderRegistry.Default.Create("cp1252"));

        Assert.Null(title.Text);
        Assert.Null(title.Error);
    }

    [Fact]
    public void Tira_os_espacos_das_pontas()
    {
        Assert.Equal("Oi", GameTitleText.Resolve("  Oi ", TextEncoderRegistry.Default.Create("cp1252")).Text);
    }

    [Fact]
    public void Largura_total_vira_normal_quando_so_assim_a_estrategia_grava()
    {
        var title = GameTitleText.Resolve("ＡＢＣ", TextEncoderRegistry.Default.Create("cp1252"));

        Assert.Equal("ABC", title.Text);
        Assert.Null(title.Error);
    }

    [Fact]
    public void Caractere_recusado_volta_como_erro()
    {
        var title = GameTitleText.Resolve("Título", TextEncoderRegistry.Default.Create("sjis"));

        Assert.NotNull(title.Error);
        Assert.False(title.IsWritable);
    }
}
