using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

public sealed class UnencodableCharacterFixerTests
{
    private static QaContext Context(string strategy = "cp1252", IReadOnlyList<TextUnit>? units = null) =>
        new(
            new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = units ?? [] },
            [],
            [],
            FormattingProfile.Default,
            TextEncoderRegistry.Default.Create(strategy));

    private static TextUnit Unit(string translation) => new("a:1", TextUnitKind.Dialogue, null, "原文", translation, TranslationStatus.Translated);

    private static ProjectFolder NovoProjeto(string raiz, string estrategia = "cp1252") => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig(estrategia),
        AiConfig.Default));

    [Theory]
    [InlineData("Oi。", "Oi.")]
    [InlineData("～Manhã～", "~Manhã~")]
    [InlineData("（x）", "(x)")]
    [InlineData("「x」", "\"x\"")]
    [InlineData("x！？", "x!?")]
    [InlineData("ＡＢ１", "AB1")]
    public void O_corretor_troca_a_pontuacao_japonesa_pela_do_teclado(string entrada, string esperado)
    {
        var rule = new UnencodableCharacterRule();
        var context = Context();

        var corrigido = rule.Fix(entrada, context);

        Assert.Equal(esperado, corrigido);
        Assert.Equal(corrigido, rule.Fix(corrigido, context));
        Assert.Empty(rule.Check(Context(units: [Unit(corrigido)])));
    }

    [Fact]
    public void Caractere_sem_substituto_na_tabela_fica_como_esta()
    {
        var rule = new UnencodableCharacterRule();

        Assert.Equal("Oi ★ 😀 x.", rule.Fix("Oi ★ 😀 x。", Context()));
    }

    [Fact]
    public void Sem_encoder_o_corretor_nao_muda_nada()
    {
        var rule = new UnencodableCharacterRule();
        var semEncoder = QaContext.ForTexts(new Dictionary<string, IReadOnlyList<TextUnit>>());

        Assert.Equal("Oi。", rule.Fix("Oi。", semEncoder));
        Assert.Equal("Oi。", ((IQaFixer)rule).Fix("Oi。"));
    }

    [Fact]
    public void A_mensagem_diz_o_nome_o_codigo_e_o_que_usar()
    {
        var issue = Assert.Single(new UnencodableCharacterRule().Check(Context(units: [Unit("～Manhã")])));

        Assert.Contains("'～' (U+FF5E)", issue.Message, StringComparison.Ordinal);
        Assert.Contains("Nome: til japonês. Use '~'.", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Caractere_fora_da_tabela_nao_ganha_nome_na_mensagem()
    {
        var issue = Assert.Single(new UnencodableCharacterRule().Check(Context(units: [Unit("★")])));

        Assert.EndsWith("não existe na estratégia 'cp1252'.", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_usa_o_encoder_do_contexto()
    {
        var texts = new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = [Unit("Oi。")] };
        var engine = new QaEngine([new UnencodableCharacterRule()]);

        var comContexto = QaFixer.Plan(texts, engine, context: Context());
        var semContexto = QaFixer.Plan(texts, engine);

        Assert.Equal("Oi.", Assert.Single(comContexto.Fixes).After);
        Assert.Empty(semContexto.Fixes);
    }

    [Fact]
    public void Apply_corrige_o_projeto_pela_estrategia_dele()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        projeto.SaveText("a", [Unit("～朝～。"), Unit("sem nada") with { Id = "a:2" }]);

        var resultado = QaFixer.Apply(projeto, QaEngine.Default);

        var fix = Assert.Single(resultado.Fixes);
        Assert.Equal(UnencodableCharacterRule.RuleId, fix.RuleId);
        Assert.Equal("~朝~.", projeto.LoadText("a")[0].Translation);
    }

    [Fact]
    public void Apply_na_estrategia_sjis_deixa_a_pontuacao_que_ela_grava()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path, "sjis");
        projeto.SaveText("a", [Unit("Oi。")]);

        var resultado = QaFixer.Apply(projeto, QaEngine.Default);

        Assert.Empty(resultado.Fixes);
        Assert.Equal("Oi。", projeto.LoadText("a")[0].Translation);
    }

    [Fact]
    public void ApplyOne_corrige_so_a_linha_pedida()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        projeto.SaveText("a", [Unit("Oi。"), Unit("Tchau。") with { Id = "a:2" }]);

        var fix = QaFixer.ApplyOne(projeto, QaEngine.Default, "a", "a:1", UnencodableCharacterRule.RuleId);

        Assert.Equal("Oi.", fix?.After);
        Assert.Equal(["Oi.", "Tchau。"], projeto.LoadText("a").Select(u => u.Translation));
    }
}
