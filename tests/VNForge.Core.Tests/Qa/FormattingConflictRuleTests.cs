using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

public sealed class FormattingConflictRuleTests
{
    private static readonly FormattingProfile EllipsisAsCharacter =
        FormattingProfile.Default with { Typography = new TypographyRule(Ellipsis: EllipsisStyle.Character) };

    private static TextUnit Unit(string translation, string id = "a:1", TextUnitKind kind = TextUnitKind.Dialogue) =>
        new(id, kind, null, "原文", translation, TranslationStatus.Translated);

    private static QaContext Context(FormattingProfile profile, string strategy = "cp1252", params TextUnit[] units) =>
        new(
            new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = units },
            [],
            [],
            profile,
            TextEncoderRegistry.Default.Create(strategy));

    private static ProjectFolder NewProject(string root, string strategy = "cp1252") => ProjectFolder.Create(root, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig(strategy),
        AiConfig.Default));

    [Fact]
    public void Acusa_o_ajuste_uma_vez_por_linha_afetada()
    {
        var context = Context(EllipsisAsCharacter, units: [Unit("Bom dia... tudo bem... e voc\u00EA..."), Unit("Oi", "a:2"), Unit("Ah...", "a:3")]);

        var issues = new FormattingConflictRule().Check(context).ToList();

        Assert.Equal(["a:1", "a:3"], issues.Select(i => i.UnitId));
        var issue = issues[0];
        Assert.Equal(FormattingConflictRule.RuleId, issue.RuleId);
        Assert.Equal(QaSeverity.Error, issue.Severity);
        Assert.Equal("O ajuste Reticências (Caractere …) gera '…', que a estratégia cp1252 não grava.", issue.Message);
    }

    [Fact]
    public void O_id_e_estavel_e_a_regra_esta_no_qa_padrao()
    {
        Assert.Equal("ajuste-nao-codificavel", FormattingConflictRule.RuleId);
        Assert.IsType<FormattingConflictRule>(QaEngine.Default.Find("ajuste-nao-codificavel"));
        Assert.Equal(QaCategories.Accents, QaCategories.Of(FormattingConflictRule.RuleId));
    }

    [Fact]
    public void Reticencias_digitadas_na_traducao_tambem_sao_do_ajuste()
    {
        var context = Context(EllipsisAsCharacter, units: [Unit("Hm\u2026")]);

        Assert.Single(new FormattingConflictRule().Check(context));
    }

    [Fact]
    public void Linha_de_papel_que_o_perfil_nao_formata_nao_e_do_ajuste()
    {
        var context = Context(EllipsisAsCharacter, units: [Unit("Hm\u2026", kind: TextUnitKind.Choice)]);

        Assert.Empty(new FormattingConflictRule().Check(context));
        Assert.Single(new UnencodableCharacterRule().Check(context));
    }

    [Fact]
    public void Sem_conflito_nem_encoder_a_regra_cala()
    {
        Assert.Empty(new FormattingConflictRule().Check(Context(EllipsisAsCharacter, "sjis", Unit("Ah..."))));
        Assert.Empty(new FormattingConflictRule().Check(Context(FormattingProfile.Default, units: [Unit("Ah...")])));
        Assert.Empty(new FormattingConflictRule().Check(QaContext.ForTexts(new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = [Unit("Ah...")] })));
    }

    [Fact]
    public void Caractere_nao_codificavel_cala_no_caractere_que_o_ajuste_produz()
    {
        var context = Context(EllipsisAsCharacter, units: [Unit("Ah... \u266A")]);

        var ajuste = Assert.Single(new FormattingConflictRule().Check(context));
        var caractere = Assert.Single(new UnencodableCharacterRule().Check(context));

        Assert.Equal("a:1", ajuste.UnitId);
        Assert.Contains("U+266A", caractere.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Corte_de_um_ajuste_nao_esconde_o_mesmo_caractere_digitado_sem_ajuste()
    {
        var profile = FormattingProfile.Default with { Typography = new TypographyRule(Dash: '\u2015') };
        var context = Context(profile, units: [Unit("a \u2014 b"), Unit("c \u2015 d", "a:2")]);

        var ajustes = new FormattingConflictRule().Check(context).ToList();
        var caracteres = new UnencodableCharacterRule().Check(context).ToList();

        Assert.Equal("a:1", Assert.Single(ajustes).UnitId);
        Assert.Equal("a:2", Assert.Single(caracteres).UnitId);
    }

    [Fact]
    public void Sem_o_ajuste_a_linha_volta_a_ser_acusada_so_pelo_que_digitou()
    {
        var context = Context(FormattingProfile.Default, units: [Unit("Ah\u2026")]);

        Assert.Empty(new FormattingConflictRule().Check(context));
        Assert.Single(new UnencodableCharacterRule().Check(context));
    }

    [Fact]
    public void A_mensagem_do_caractere_sugere_o_substituto_do_corretor()
    {
        var context = Context(FormattingProfile.Default, units: [Unit("Ah \u266A"), Unit("a\u30FBb", "a:2"), Unit("\u0151", "a:3"), Unit("\U0001F600", "a:4")]);

        var messages = new UnencodableCharacterRule().Check(context).Select(i => i.Message).ToList();

        Assert.EndsWith("Use '~'.", messages[0], StringComparison.Ordinal);
        Assert.EndsWith("Nome: ponto médio japonês. Use um espaço.", messages[1], StringComparison.Ordinal);
        Assert.EndsWith("Use 'o'.", messages[2], StringComparison.Ordinal);
        Assert.EndsWith("não existe na estratégia 'cp1252'.", messages[3], StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_grava_o_perfil_com_tres_pontos_troca_a_nota_e_a_segunda_rodada_nao_acha_nada()
    {
        using var folder = new TempFolder();
        var project = NewProject(folder.Path);
        project.SaveFormatting(EllipsisAsCharacter);
        project.SaveText("a", [Unit("Ah... \u266A"), Unit("Oi", "a:2")]);

        var first = QaFixer.Apply(project, QaEngine.Default);

        var setting = Assert.Single(first.SettingsFixed);
        Assert.Equal("typography.ellipsis", setting.Field);
        Assert.Equal("character", setting.Current);
        Assert.Equal("dots", setting.Safe);
        Assert.Equal(EllipsisStyle.Dots, project.LoadFormatting().Typography.Ellipsis);
        var fix = Assert.Single(first.Fixes);
        Assert.Equal(UnencodableCharacterRule.RuleId, fix.RuleId);
        Assert.Equal("Ah... ~", project.LoadText("a")[0].Translation);

        var second = QaFixer.Apply(project, QaEngine.Default);

        Assert.Empty(second.SettingsFixed);
        Assert.Empty(second.Fixes);
        var report = QaEngine.Default.Run(QaContext.FromProject(project));
        Assert.DoesNotContain(report.Issues, i => i.RuleId is UnencodableCharacterRule.RuleId or FormattingConflictRule.RuleId);
    }

    [Fact]
    public void Apply_a_seco_nao_grava_o_perfil_mas_conta_o_ajuste()
    {
        using var folder = new TempFolder();
        var project = NewProject(folder.Path);
        project.SaveFormatting(EllipsisAsCharacter);
        project.SaveText("a", [Unit("Ah...")]);

        var result = QaFixer.Apply(project, QaEngine.Default, dryRun: true);

        Assert.Single(result.SettingsFixed);
        Assert.Equal(EllipsisStyle.Character, project.LoadFormatting().Typography.Ellipsis);
    }

    [Fact]
    public void Apply_sem_conflito_nao_cria_o_formatting_json()
    {
        using var folder = new TempFolder();
        var project = NewProject(folder.Path);
        project.SaveText("a", [Unit("Ah \u266A")]);

        var result = QaFixer.Apply(project, QaEngine.Default);

        Assert.Empty(result.SettingsFixed);
        Assert.False(File.Exists(project.FormattingPath));
    }

    [Fact]
    public void Apply_com_filtro_de_regras_nao_troca_o_ajuste_se_a_regra_nao_foi_pedida()
    {
        using var folder = new TempFolder();
        var project = NewProject(folder.Path);
        project.SaveFormatting(EllipsisAsCharacter);
        project.SaveText("a", [Unit("Ah...")]);

        var result = QaFixer.Apply(project, QaEngine.Default, [UnencodableCharacterRule.RuleId]);

        Assert.Empty(result.SettingsFixed);
        Assert.Equal(EllipsisStyle.Character, project.LoadFormatting().Typography.Ellipsis);
    }

    [Fact]
    public void Plan_nao_mexe_em_ajuste()
    {
        var texts = new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = [Unit("Ah...")] };

        var result = QaFixer.Plan(texts, QaEngine.Default, context: Context(EllipsisAsCharacter, units: [Unit("Ah...")]));

        Assert.Empty(result.SettingsFixed);
    }

    [Fact]
    public void O_projeto_expoe_os_conflitos_e_resolve_um_so()
    {
        using var folder = new TempFolder();
        var project = NewProject(folder.Path);
        project.SaveFormatting(FormattingProfile.Default with { Typography = new TypographyRule(QuoteStyle.Corner, EllipsisStyle.Character) });

        var conflicts = QaFixer.FindSettingConflicts(project);
        Assert.Equal(["typography.quotes", "typography.ellipsis"], conflicts.Select(c => c.Field));

        var resolved = QaFixer.ApplySetting(project, "typography.ellipsis");

        Assert.Equal("typography.ellipsis", resolved?.Field);
        var profile = project.LoadFormatting();
        Assert.Equal(EllipsisStyle.Dots, profile.Typography.Ellipsis);
        Assert.Equal(QuoteStyle.Corner, profile.Typography.Quotes);
        Assert.Null(QaFixer.ApplySetting(project, "typography.ellipsis"));
    }
}
