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
    [InlineData("Sr. Tanaka-san chegou.")]
    public void Remover_avisa_de_cada_honorifico_que_sobrou(string translation)
    {
        var issue = Honorifics(HonorificsPolicy.Remove, translation).First();

        Assert.Equal((HonorificsRule.RuleId, QaSeverity.Warning, "a:1"), (issue.RuleId, issue.Severity, issue.UnitId));
        Assert.Contains("remover", issue.Message);
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
    [InlineData("O senhor Tanaka-sama chegou.", "-sama")]
    public void Adaptar_avisa_dos_sufixos_mas_deixa_o_senpai_seguir_o_glossario(string translation, string suffix)
    {
        var issue = Assert.Single(Honorifics(HonorificsPolicy.Adapt, translation));

        Assert.Contains($"“{suffix}”", issue.Message);
        Assert.Contains("adaptar", issue.Message);
        Assert.Empty(Honorifics(HonorificsPolicy.Adapt, "Misaki, o senpai chamou."));
    }

    [Theory]
    [InlineData("カイトくん", true)]
    public void Honorifico_no_original_sem_nome_conhecido_so_conta_ChanKun_depois_de_nome_ou_senpai(string source, bool expected)
    {
        Assert.Equal(expected, HonorificsRule.HasHonorificInSource(source));
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
}
