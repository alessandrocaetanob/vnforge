using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

public sealed class StyleRulesTests
{
    private const string WithHonorific = "「美咲ちゃん、先輩が呼んでるよ」";

    private static TextUnit Unit(string id, string source, string? translation, string? speaker = null, TranslationStatus status = TranslationStatus.Translated) =>
        new(id, TextUnitKind.Dialogue, speaker, source, translation, status);

    private static QaContext Context(HonorificsPolicy policy, IReadOnlyList<CharacterEntry> characters, params TextUnit[] units) =>
        new(units.GroupBy(u => u.Id.Split(':')[0]).ToDictionary(g => g.Key, g => (IReadOnlyList<TextUnit>)g.ToList()),
            [], characters, FormattingProfile.Default)
        {
            Style = StyleConfig.Default with { Honorifics = policy },
        };

    private static List<QaIssue> Honorifics(HonorificsPolicy policy, string translation, string source = WithHonorific) =>
        [.. new HonorificsRule().Check(Context(policy, [], Unit("a:1", source, translation)))];

    [Theory]
    [InlineData("Misaki-chan, o senpai chamou.")]
    [InlineData("Sr. Tanaka-san chegou.")]
    [InlineData("Fale com o Kenta-KUN.")]
    [InlineData("Sama? Não: Rei-sama voltou.")]
    public void Remover_avisa_de_cada_honorifico_que_sobrou(string translation)
    {
        var issue = Honorifics(HonorificsPolicy.Remove, translation).First();

        Assert.Equal((HonorificsRule.RuleId, QaSeverity.Warning, "a:1"), (issue.RuleId, issue.Severity, issue.UnitId));
        Assert.Contains("remover", issue.Message);
    }

    [Fact]
    public void Remover_nao_avisa_quando_a_traducao_esta_limpa()
    {
        Assert.Empty(Honorifics(HonorificsPolicy.Remove, "Misaki, o veterano chamou."));
    }

    [Fact]
    public void Remover_conta_um_aviso_por_honorifico_diferente()
    {
        var issues = Honorifics(HonorificsPolicy.Remove, "Misaki-chan, o senpai e o Ken-kun, de novo Misaki-chan.");

        Assert.True(issues.Count == 3, string.Join(" / ", issues.Select(i => i.Message)));
        Assert.Contains(issues, i => i.Message.Contains("“-chan”"));
        Assert.Contains(issues, i => i.Message.Contains("“senpai”"));
        Assert.Contains(issues, i => i.Message.Contains("“-kun”"));
    }

    [Theory]
    [InlineData("Misaki-chan, espera!", "-chan")]
    [InlineData("Ken-kun, vem!", "-kun")]
    [InlineData("Tanaka-san chegou.", "-san")]
    [InlineData("O senhor Tanaka-sama chegou.", "-sama")]
    public void Adaptar_avisa_dos_sufixos_mas_deixa_o_senpai_seguir_o_glossario(string translation, string suffix)
    {
        var issue = Assert.Single(Honorifics(HonorificsPolicy.Adapt, translation));

        Assert.Contains($"“{suffix}”", issue.Message);
        Assert.Contains("adaptar", issue.Message);
        Assert.Empty(Honorifics(HonorificsPolicy.Adapt, "Misaki, o senpai chamou."));
    }

    [Fact]
    public void Adaptar_nao_avisa_com_senhor_e_nome_sem_sufixo()
    {
        Assert.Empty(Honorifics(HonorificsPolicy.Adapt, "Misaki, o senhor Tanaka chamou."));
    }

    [Theory]
    [InlineData("Misaki-chan, o senpai chamou.")]
    [InlineData("O Kenta-san veio.")]
    public void Manter_nao_avisa_quando_a_traducao_traz_honorifico(string translation)
    {
        Assert.Empty(Honorifics(HonorificsPolicy.Keep, translation));
    }

    [Fact]
    public void Manter_avisa_quando_o_original_tem_honorifico_e_a_traducao_nao()
    {
        var issue = Assert.Single(Honorifics(HonorificsPolicy.Keep, "Misaki, o veterano chamou."));

        Assert.Contains("manter", issue.Message);
    }

    [Theory]
    [InlineData("「こんにちは」")]
    [InlineData("散歩に行こう")]
    [InlineData("三人で行く")]
    public void Manter_nao_avisa_quando_o_original_nao_tem_honorifico(string source)
    {
        Assert.Empty(Honorifics(HonorificsPolicy.Keep, "Vamos passear.", source));
    }

    [Theory]
    [InlineData("カイトくん", true)]
    [InlineData("美咲ちゃん", true)]
    [InlineData("先輩", true)]
    [InlineData("センパイ", true)]
    [InlineData("おはよう", false)]
    [InlineData("さんぽ", false)]
    [InlineData("赤ちゃんが泣いている", false)]
    [InlineData("お嬢ちゃん、こちらへ", false)]
    [InlineData("ちゃんと食べなさい", false)]
    [InlineData("お母さん、模様が変だよ", false)]
    [InlineData("お客様、どうぞ", false)]
    [InlineData("同様に仕様も多様で異様だ", false)]
    [InlineData("ごちそうさま、ご馳走さまでした", false)]
    [InlineData("父さんと母さん", false)]
    public void Honorifico_no_original_sem_nome_conhecido_so_conta_ChanKun_depois_de_nome_ou_senpai(string source, bool expected)
    {
        Assert.Equal(expected, HonorificsRule.HasHonorificInSource(source));
    }

    [Theory]
    [InlineData("田中さん、どうも", true)]
    [InlineData("田中様がお見えです", true)]
    [InlineData("田中さまは?", true)]
    [InlineData("美咲さんの席", true)]
    [InlineData("お母さん、田中を見た?", false)]
    [InlineData("お客様、田中は不在です", false)]
    [InlineData("田中さんぽ", false)]
    public void Com_o_nome_conhecido_antes_o_sufixo_conta(string source, bool expected)
    {
        Assert.Equal(expected, HonorificsRule.HasHonorificInSource(source, ["田中", "美咲"]));
    }

    [Fact]
    public void Nome_completo_de_characters_json_vale_pelo_sobrenome_e_pelo_nome()
    {
        var cast = new[] { new CharacterEntry("佐藤 健太", "Kenta") };
        var rule = new HonorificsRule();

        var givenName = Context(HonorificsPolicy.Keep, cast, Unit("a:1", "健太さん、こんにちは", "Oi."));
        var family = Context(HonorificsPolicy.Keep, cast, Unit("a:2", "佐藤様、こんにちは", "Oi."));
        var stranger = Context(HonorificsPolicy.Keep, cast, Unit("a:3", "山田さん、こんにちは", "Oi."));

        Assert.Single(rule.Check(givenName));
        Assert.Single(rule.Check(family));
        Assert.Empty(rule.Check(stranger));
    }

    [Fact]
    public void Nome_do_glossario_e_do_falante_tambem_valem()
    {
        var withGlossary = new QaContext(
            new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = [Unit("a:1", "ルナさん、おいで", "Venha.")] },
            [new GlossaryTerm("ルナ", "Luna")], [], FormattingProfile.Default) { Style = StyleConfig.Default with { Honorifics = HonorificsPolicy.Keep } };
        var bySpeaker = Context(HonorificsPolicy.Keep, [], Unit("a:2", "ルナさん、おいで", "Venha.", speaker: "ルナ"));
        var withoutName = Context(HonorificsPolicy.Keep, [], Unit("a:3", "ルナさん、おいで", "Venha."));

        Assert.Single(new HonorificsRule().Check(withGlossary));
        Assert.Single(new HonorificsRule().Check(bySpeaker));
        Assert.Empty(new HonorificsRule().Check(withoutName));
    }

    [Theory]
    [InlineData("「お母さん、模様が変だよ」")]
    [InlineData("「お客様、こちらへどうぞ」")]
    [InlineData("「同様の仕様です」")]
    [InlineData("「ごちそうさま!」")]
    public void Manter_nao_avisa_de_palavra_comum_traduzida_sem_honorifico(string source)
    {
        Assert.Empty(Honorifics(HonorificsPolicy.Keep, "Mãe, o padrão está estranho.", source));
    }

    [Fact]
    public void Marcadores_de_controle_nao_contam_como_texto()
    {
        Assert.Empty(Honorifics(HonorificsPolicy.Remove, "Oi.{VAR:rei-san}\\p"));
    }

    [Fact]
    public void Linha_sem_traducao_ou_nao_traduzida_nao_conta()
    {
        var context = Context(
            HonorificsPolicy.Remove, [],
            Unit("a:1", WithHonorific, null, status: TranslationStatus.Untranslated),
            Unit("a:2", WithHonorific, "Misaki-chan", status: TranslationStatus.Untranslated));

        Assert.Empty(new HonorificsRule().Check(context));
    }

    [Fact]
    public void Honorificos_so_avisa_e_nao_tem_corretor()
    {
        Assert.IsNotType<IQaFixer>(new HonorificsRule(), exactMatch: false);
        Assert.IsNotType<IQaFixer>(new GenderRule(), exactMatch: false);
    }

    private static readonly CharacterEntry[] Cast =
    [
        new("春花", "Haruka", CharacterGender.Unknown),
        new("凛", "Rin", CharacterGender.Female),
        new("ナレーター", "Narrador", CharacterGender.Other),
    ];

    [Fact]
    public void Genero_da_um_aviso_por_personagem_com_a_contagem_de_falas()
    {
        var context = Context(
            HonorificsPolicy.Adapt, Cast,
            Unit("a:1", "こんにちは", "Oi", "春花"),
            Unit("a:2", "やあ", "Olá", "凛"),
            Unit("a:3", "またね", "Tchau", "春花"),
            Unit("b:1", "じゃあ", "Então", "春花"));

        var issue = Assert.Single(new GenderRule().Check(context));

        Assert.Equal((GenderRule.RuleId, QaSeverity.Warning, "a:1", "a"), (issue.RuleId, issue.Severity, issue.UnitId, issue.ScriptKey));
        Assert.Contains("春花", issue.Message);
        Assert.Contains("3 falas traduzidas", issue.Message);
        Assert.Contains("characters.json", issue.Message);
    }

    [Fact]
    public void Genero_com_uma_fala_diz_1_fala_e_cada_personagem_tem_o_seu_aviso()
    {
        var cast = new[] { new CharacterEntry("春花", "Haruka"), new CharacterEntry("秋人", "Akito") };
        var context = Context(HonorificsPolicy.Adapt, cast, Unit("a:1", "x", "x", "春花"), Unit("a:2", "y", "y", "秋人"), Unit("a:3", "z", "z", "秋人"));

        var issues = new GenderRule().Check(context).ToList();

        Assert.Equal(["春花", "秋人"], issues.Select(i => i.Message.Split('“', '”')[1]));
        Assert.Contains("1 fala traduzida", issues[0].Message);
        Assert.Contains("2 falas traduzidas", issues[1].Message);
    }

    [Fact]
    public void Genero_ignora_outro_neutro_falante_vazio_desconhecido_e_linha_sem_traducao()
    {
        var context = Context(
            HonorificsPolicy.Adapt, Cast,
            Unit("a:1", "x", "x", "ナレーター"),
            Unit("a:2", "x", "x", null),
            Unit("a:3", "x", "x", "   "),
            Unit("a:4", "x", "x", "ある人"),
            Unit("a:5", "x", null, "春花", TranslationStatus.Untranslated));

        Assert.Empty(new GenderRule().Check(context));
    }

    [Fact]
    public void Genero_compara_o_falante_sem_espacos_nas_pontas()
    {
        var context = Context(HonorificsPolicy.Adapt, Cast, Unit("a:1", "こんにちは", "Oi", "　春花 "));

        Assert.Single(new GenderRule().Check(context));
    }

    [Fact]
    public void As_duas_regras_entram_no_QA_padrao_na_categoria_Estilo()
    {
        foreach (var id in new[] { HonorificsRule.RuleId, GenderRule.RuleId })
        {
            Assert.NotNull(QaEngine.Default.Find(id));
            Assert.Equal(QaCategories.Style, QaCategories.Of(id));
        }
    }

    [Fact]
    public void Os_avisos_de_estilo_nao_travam_o_build_mas_os_outros_avisos_e_os_erros_travam()
    {
        QaReport Report(params QaIssue[] issues) => new(issues, [], []);
        var honorific = new QaIssue(HonorificsRule.RuleId, QaSeverity.Warning, "a:1", "a", "x");
        var gender = new QaIssue(GenderRule.RuleId, QaSeverity.Warning, "a:2", "a", "y");
        var spaces = new QaIssue("espaco-duplo", QaSeverity.Warning, "a:3", "a", "z");
        var error = new QaIssue("traducao-vazia", QaSeverity.Error, "a:4", "a", "w");

        Assert.False(Report(honorific, gender).Blocks(allowWarnings: false));
        Assert.True(Report(honorific, spaces).Blocks(allowWarnings: false));
        Assert.False(Report(honorific, spaces).Blocks(allowWarnings: true));
        Assert.True(Report(honorific, error).Blocks(allowWarnings: true));
        Assert.Equal(2, Report(honorific, gender).OtherIssues.Count);
    }

    [Fact]
    public void A_mensagem_de_bloqueio_conta_so_os_avisos_que_travam()
    {
        var spaces = new QaIssue("espaco-duplo", QaSeverity.Warning, "a:1", "a", "dois espaços");
        var style = Enumerable.Range(0, 30)
            .Select(n => new QaIssue(n % 2 == 0 ? HonorificsRule.RuleId : GenderRule.RuleId, QaSeverity.Warning, $"a:{n + 2}", "a", "estilo"))
            .ToList();
        var report = new QaReport([spaces, .. style], [], []);

        var message = new QaBlockedException(report, allowWarnings: false).Message;

        Assert.Equal("0 erros, 1 aviso", QaBlockedException.DescribeBlocking(report));
        Assert.Contains("(0 erros, 1 aviso)", message);
        Assert.Contains("[espaco-duplo]", message);
        Assert.DoesNotContain(HonorificsRule.RuleId, message);
        Assert.DoesNotContain(GenderRule.RuleId, message);
        Assert.Equal([spaces], report.BlockingIssues);
        Assert.Equal(30, report.StyleAdviceCount);
    }

    [Fact]
    public void O_motor_silencia_o_aviso_de_honorifico_marcado_como_ok()
    {
        var unit = Unit("a:1", WithHonorific, "Misaki-chan, espera!") with { Flags = [HonorificsRule.RuleId] };

        var report = new QaEngine([new HonorificsRule()]).Run(Context(HonorificsPolicy.Remove, [], unit));

        Assert.Empty(report.Issues);
        Assert.Single(report.Silenced);
    }
}
