using System.Text.Json;
using System.Text.RegularExpressions;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class CheckCommandTests : IDisposable
{
    private const string ScriptKey = "yst00001";
    private const string Forbidden = "proibida";
    private const string ForbiddenRule = "palavra-proibida";
    private const string WarningRule = "aviso-a";
    private const string ErrorRule = "erro-b";
    private const string SeverityFlag = "--severity";
    private const string RulesFlag = "--rules";
    private const string FixFlag = "--fix";
    private const string DoubleSpaced = "Olá  mundo";

    private readonly CliHarness _cli = new();
    private readonly ProjectFolder _project;
    private readonly string _projectPath;

    public CheckCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = ProjectFolder.Create(_projectPath, new ProjectConfig(
            "Minha VN",
            new GameConfig("jogo", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Unit(int n, string translation) =>
        new($"{ScriptKey}:{n:D6}", TextUnitKind.Dialogue, null, "原文", translation, TranslationStatus.Translated);

    private void Save(params string[] translations) =>
        _project.SaveText(ScriptKey, [.. translations.Select((t, i) => Unit(i + 1, t))]);

    private void UseRules(params IQaRule[] rules) => _cli.Cli.Qa = new QaEngine(rules);

    private Task<RunResult> Check(params string[] args) =>
        _cli.RunAsync(["check", "--project", _projectPath, .. args], TestContext.Current.CancellationToken);

    [Fact]
    public async Task Traducoes_diferentes_do_mesmo_original_saem_num_bloco_com_variantes_e_linhas()
    {
        Save("Olá mundo.", "Olá mundo.", "Olá  mundo.", "Olá mundo");
        UseRules(new InconsistentTranslationRule());

        var result = await Check();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Mesmo original, 3 traduções (4 linhas): 原文", result.Output);
        Assert.Contains("● 2 linhas  Olá mundo.", result.Output);
        Assert.Contains("yst00001:000001, yst00001:000002", result.Output);
        Assert.Contains("○ 1 linha  Olá ·mundo.", result.Output);
        Assert.Contains("○ 1 linha  Olá mundo⟨.⟩", result.Output);
        Assert.Single(Regex.Matches(result.Output, "Mesmo original"));
        Assert.DoesNotContain("aparece traduzido como", result.Output);
        Assert.Contains("2 avisos", result.Output);
    }

    [Fact]
    public async Task Json_traz_os_grupos_com_variantes_linhas_e_diferenca()
    {
        Save("Olá mundo.", "Olá mundo.", "Olá mundo");
        UseRules(new InconsistentTranslationRule());

        var result = await Check("--json");

        using var doc = JsonDocument.Parse(result.Output);
        var group = doc.RootElement.GetProperty("data").GetProperty("consistencyGroups").EnumerateArray().Single();
        Assert.Equal("原文", group.GetProperty("source").GetString());
        var variants = group.GetProperty("variants").EnumerateArray().ToList();
        Assert.Equal(2, variants.Count);
        Assert.True(variants[0].GetProperty("reference").GetBoolean());
        Assert.Equal(2, variants[0].GetProperty("lines").GetArrayLength());
        Assert.Contains(variants[1].GetProperty("diff").EnumerateArray(), d => d.GetProperty("kind").GetString() == "missing" && d.GetProperty("text").GetString() == ".");
    }

    [Fact]
    public async Task Grupo_mantido_diferente_de_proposito_nao_aparece()
    {
        Save("Olá mundo.", "Olá mundo");
        ConsistencyFlags.Keep(_project, Assert.Single(ConsistencyGroups.Find(QaContext.ForTexts(_project.LoadAllText()))));
        UseRules(new InconsistentTranslationRule());

        var result = await Check();

        Assert.DoesNotContain("Mesmo original", result.Output);
    }

    [Fact]
    public async Task Erro_bloqueante_sai_com_2_e_lista_no_formato_do_doc()
    {
        Save("Olá", Forbidden);
        UseRules(new TranslationIs(ForbiddenRule, QaSeverity.Error, Forbidden));

        var result = await Check();

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("ERRO  yst00001:000002  tradução 'proibida' recusada", result.Output);
        Assert.Contains("1 erro, 0 avisos", result.Output);
    }

    [Fact]
    public async Task So_avisos_saem_com_0()
    {
        Save("Olá", Forbidden);
        UseRules(new TranslationIs(ForbiddenRule, QaSeverity.Warning, Forbidden));

        var result = await Check();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("AVISO yst00001:000002  tradução 'proibida' recusada", result.Output);
        Assert.Contains("0 erros, 1 aviso", result.Output);
    }

    [Fact]
    public async Task Sem_ocorrencias_sai_com_0()
    {
        Save("Olá");
        UseRules(new TranslationIs(ForbiddenRule, QaSeverity.Error, Forbidden));

        var result = await Check();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("0 erros, 0 avisos", result.Output);
    }

    [Fact]
    public async Task Severity_error_esconde_os_avisos()
    {
        Save("a", "b");
        UseRules(new TranslationIs(WarningRule, QaSeverity.Warning, "a"), new TranslationIs(ErrorRule, QaSeverity.Error, "b"));

        var result = await Check(SeverityFlag, "error");

        Assert.Equal(2, result.ExitCode);
        Assert.DoesNotContain("AVISO", result.Output);
        Assert.Contains("1 erro, 0 avisos", result.Output);
    }

    [Fact]
    public async Task Severity_warn_lista_erros_e_avisos()
    {
        Save("a", "b");
        UseRules(new TranslationIs(WarningRule, QaSeverity.Warning, "a"), new TranslationIs(ErrorRule, QaSeverity.Error, "b"));

        var result = await Check(SeverityFlag, "warn");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("1 erro, 1 aviso", result.Output);
    }

    [Fact]
    public async Task Severity_invalida_sai_com_1()
    {
        Save("a");
        UseRules();

        var result = await Check(SeverityFlag, "grave");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(SeverityFlag, result.Error + result.Output);
    }

    [Fact]
    public async Task Rules_roda_so_as_regras_pedidas_e_aceita_lista()
    {
        Save("a", "b");
        UseRules(new TranslationIs("regra-a", QaSeverity.Error, "a"), new TranslationIs("regra-b", QaSeverity.Error, "b"));

        var one = await Check(RulesFlag, "regra-b");
        var both = await Check(RulesFlag, "regra-a, regra-b");

        Assert.Contains("1 erro,", one.Output);
        Assert.DoesNotContain("000001", one.Output);
        Assert.Contains("2 erros,", both.Output);
    }

    [Fact]
    public async Task Rules_com_id_desconhecido_sai_com_1()
    {
        Save("a");
        UseRules(new TranslationIs("regra-a", QaSeverity.Error, "a"));

        var result = await Check(RulesFlag, "nao-existe");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("nao-existe", result.Error + result.Output);
    }

    [Fact]
    public async Task Fix_corrige_grava_e_a_segunda_rodada_nao_muda_nada()
    {
        Save(DoubleSpaced, "ok");
        UseRules(new DoubleSpaceRule());

        var first = await Check(FixFlag);
        var second = await Check(FixFlag);

        Assert.Equal(0, first.ExitCode);
        Assert.Contains("yst00001:000001", first.Output);
        Assert.Contains("espaco-duplo", first.Output);
        Assert.Equal("Olá mundo", _project.LoadText(ScriptKey)[0].Translation);
        Assert.DoesNotContain("espaco-duplo", second.Output);
        Assert.Contains("0 erros, 0 avisos", second.Output);
    }

    [Fact]
    public async Task Fix_o_codigo_de_saida_vale_depois_da_correcao()
    {
        Save(DoubleSpaced, Forbidden);
        UseRules(new DoubleSpaceRule(), new TranslationIs(ForbiddenRule, QaSeverity.Error, Forbidden));

        var result = await Check(FixFlag);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("1 erro, 0 avisos", result.Output);
        Assert.DoesNotContain("AVISO", result.Output);
    }

    [Fact]
    public async Task Fix_com_rules_de_regra_sem_correcao_nao_falha()
    {
        Save(DoubleSpaced);
        UseRules(new DoubleSpaceRule(), new TranslationIs(ForbiddenRule, QaSeverity.Error, "x"));

        var result = await Check(FixFlag, RulesFlag, ForbiddenRule);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(DoubleSpaced, _project.LoadText(ScriptKey)[0].Translation);
    }

    [Fact]
    public async Task Fix_com_rules_desconhecida_sai_com_1_sem_gravar()
    {
        Save(DoubleSpaced);
        UseRules(new DoubleSpaceRule());

        var result = await Check(FixFlag, RulesFlag, "espaco-duplo,nao-existe");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(DoubleSpaced, _project.LoadText(ScriptKey)[0].Translation);
    }

    [Fact]
    public async Task Sem_fix_nao_grava_nada()
    {
        Save(DoubleSpaced);
        UseRules(new DoubleSpaceRule());

        await Check();

        Assert.Equal(DoubleSpaced, _project.LoadText(ScriptKey)[0].Translation);
    }

    [Fact]
    public async Task Json_traz_contagens_ocorrencias_e_codigo_de_saida()
    {
        Save("a", "b");
        UseRules(new TranslationIs(WarningRule, QaSeverity.Warning, "a"), new TranslationIs(ErrorRule, QaSeverity.Error, "b"));

        var result = await Check("--json");

        Assert.Equal(2, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("errors").GetInt32());
        Assert.Equal(1, data.GetProperty("warnings").GetInt32());
        Assert.Equal(0, data.GetProperty("fixed").GetInt32());
        var issues = data.GetProperty("issues").EnumerateArray().ToList();
        Assert.Equal(["warn", "error"], issues.Select(i => i.GetProperty("severity").GetString()));
        Assert.Equal([WarningRule, ErrorRule], issues.Select(i => i.GetProperty("rule").GetString()));
        Assert.Equal("yst00001:000002", issues[1].GetProperty("unit").GetString());
    }

    [Fact]
    public async Task Aviso_marcado_como_ok_nao_conta_e_aparece_no_resumo()
    {
        Save(Forbidden);
        var engine = new QaEngine([new TranslationIs(ForbiddenRule, QaSeverity.Warning, Forbidden)]);
        QaFlags.MarkOk(_project, engine, "yst00001:000001", ForbiddenRule);
        _cli.Cli.Qa = engine;

        var result = await Check();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("0 erros, 0 avisos", result.Output);
        Assert.Contains("1 aviso marcado como ok", result.Output);
    }

    [Fact]
    public async Task Pasta_sem_projeto_sai_com_1()
    {
        var result = await _cli.RunAsync(["check", "--project", Path.Combine(_cli.Root, "vazia")], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task Texto_agrupa_por_categoria_com_as_categorias_de_erro_primeiro()
    {
        Save("a", "b", "c");
        UseRules(
            new TranslationIs("espaco-duplo", QaSeverity.Warning, "a"),
            new TranslationIs("marcador-diferente", QaSeverity.Error, "b"),
            new TranslationIs("glossario-diferente", QaSeverity.Warning, "c"),
            new TranslationIs("regra-sem-categoria", QaSeverity.Warning, "c"));

        var result = await Check();

        Assert.Equal(2, result.ExitCode);
        var headings = Lines(result).Where(l => !l.StartsWith(' ') && l.Contains('·')).ToArray();
        Assert.Equal(["Marcadores · 1 erro", "Espaços · 1 aviso", "Glossário · 1 aviso", "Outras · 1 aviso"], headings);
        Assert.Equal(1, Lines(result).Count(l => l == "  ERRO  yst00001:000002  tradução 'b' recusada  [marcador-diferente]"));
        Assert.Contains("3 avisos", result.Output);
    }

    [Fact]
    public async Task Dentro_da_categoria_os_erros_vem_antes_dos_avisos()
    {
        Save("a", "b");
        UseRules(new TranslationIs("espaco-duplo", QaSeverity.Warning, "a"), new TranslationIs("espaco-antes-pontuacao", QaSeverity.Error, "b"));

        var lines = Lines(await Check());

        Assert.Equal("Espaços · 1 erro, 1 aviso", lines.Single(l => l.StartsWith("Espaços", StringComparison.Ordinal)));
        Assert.True(Array.FindIndex(lines, l => l.StartsWith("  ERRO ", StringComparison.Ordinal)) < Array.FindIndex(lines, l => l.StartsWith("  AVISO", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Nao_traduzida_vira_uma_linha_por_script_e_aponta_o_comando_da_lista_completa()
    {
        SaveUntranslated(ScriptKey, 3);
        SaveUntranslated("yst00002", 2);
        UseRules(new UntranslatedQaRule());

        var result = await Check();

        Assert.Equal(0, result.ExitCode);
        var lines = Lines(result);
        Assert.Contains("Tradução · 5 avisos", lines);
        Assert.Contains("  AVISO yst00001  3 linhas sem tradução  [nao-traduzida]", lines);
        Assert.Contains("  AVISO yst00002  2 linhas sem tradução  [nao-traduzida]", lines);
        Assert.Contains("  Para listar uma a uma: vnf check --rules nao-traduzida", lines);
        Assert.DoesNotContain("linha não traduzida", result.Output);
        Assert.Contains("0 erros, 5 avisos", result.Output);
    }

    [Fact]
    public async Task Rules_nao_traduzida_lista_cada_linha()
    {
        SaveUntranslated(ScriptKey, 3);
        UseRules(new UntranslatedQaRule());

        var result = await Check(RulesFlag, "nao-traduzida");

        var lines = Lines(result);
        Assert.Equal(3, lines.Count(l => l.StartsWith("  AVISO yst00001:00000", StringComparison.Ordinal) && l.Contains("linha não traduzida", StringComparison.Ordinal)));
        Assert.DoesNotContain("Para listar uma a uma", result.Output);
    }

    [Fact]
    public async Task Json_continua_listando_cada_linha_nao_traduzida()
    {
        SaveUntranslated(ScriptKey, 3);
        UseRules(new UntranslatedQaRule());

        var result = await Check("--json");

        using var doc = JsonDocument.Parse(result.Output);
        var issues = doc.RootElement.GetProperty("data").GetProperty("issues").EnumerateArray().ToList();
        Assert.Equal(3, issues.Count);
        Assert.All(issues, i => Assert.Equal("nao-traduzida", i.GetProperty("rule").GetString()));
    }

    [Fact]
    public async Task Terminal_interativo_pinta_erro_e_aviso_com_as_cores_do_app()
    {
        using var rich = new CliHarness(terminal: CliTerminal.TrueColor(width: 100));
        var project = ProjectFolder.Create(Path.Combine(rich.Root, "projeto"), _project.LoadConfig());
        project.SaveText(ScriptKey, [Unit(1, "a"), Unit(2, "b")]);
        rich.Cli.Qa = new QaEngine([new TranslationIs(WarningRule, QaSeverity.Warning, "a"), new TranslationIs(ErrorRule, QaSeverity.Error, "b")]);

        var result = await rich.RunAsync(["check", "--project", project.Root], TestContext.Current.CancellationToken);

        Assert.Contains("38;2;245;154;178m", result.Output); // erro
        Assert.Contains("38;2;242;196;109m", result.Output); // aviso
    }

    private const int EllipsisLines = 1234;
    private const string GroupedEllipsis = "Reticências (Caractere …): 1.234 linhas com erro. A estratégia cp1252 não grava '…'. vnf check --fix troca para Três pontos.";

    private ProjectFolder EllipsisProject()
    {
        var project = ProjectFolder.Create(Path.Combine(_cli.Root, "cp1252"), _project.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        project.SaveFormatting(FormattingProfile.Default with { Typography = new TypographyRule(Ellipsis: EllipsisStyle.Character) });
        UseRules(new FormattingConflictRule(), new UnencodableCharacterRule());
        project.SaveText(ScriptKey, [.. Enumerable.Range(1, EllipsisLines).Select(n => Unit(n, "Espera...")), Unit(EllipsisLines + 1, "Sem reticências")]);
        return project;
    }

    private Task<RunResult> CheckEllipsis(ProjectFolder project, params string[] args) =>
        _cli.RunAsync(["check", "--project", project.Root, .. args], TestContext.Current.CancellationToken);

    [Fact]
    public async Task Ajuste_que_a_codificacao_nao_grava_sai_agrupado_numa_linha_por_ajuste()
    {
        var project = EllipsisProject();

        var result = await CheckEllipsis(project);

        Assert.Equal(2, result.ExitCode);
        var lines = Lines(result);
        Assert.Contains($"ERRO {GroupedEllipsis} [ajuste-nao-codificavel]", Regex.Replace(result.Output, @"\s+", " "));
        Assert.Equal(1, lines.Count(l => l.Contains("[ajuste-nao-codificavel]", StringComparison.Ordinal)));
        Assert.DoesNotContain("O ajuste Reticências", result.Output);
        Assert.Contains("Acentuação · 1.234 erros", lines);
    }

    [Fact]
    public async Task Ajuste_de_aspas_lista_os_dois_caracteres_que_a_codificacao_nao_grava()
    {
        var project = EllipsisProject();
        project.SaveFormatting(FormattingProfile.Default with { Typography = new TypographyRule(Quotes: QuoteStyle.Corner) });
        project.SaveText(ScriptKey, [Unit(1, "Ele disse “oi”")]);

        var result = await CheckEllipsis(project, "--json");

        Assert.Equal(2, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        var conflict = Assert.Single(doc.RootElement.GetProperty("data").GetProperty("settingConflicts").EnumerateArray());
        Assert.Equal("typography.quotes", conflict.GetProperty("field").GetString());
        Assert.Equal("「」", conflict.GetProperty("characters").GetString());
    }

    [Fact]
    public async Task Fix_troca_o_ajuste_conta_a_troca_e_a_segunda_rodada_nao_acha_erro()
    {
        var project = EllipsisProject();

        var first = await CheckEllipsis(project, FixFlag);
        var second = await CheckEllipsis(project, FixFlag);

        Assert.Equal(0, first.ExitCode);
        Assert.Contains("formatting.json: Reticências passou de Caractere … para Três pontos.", first.Output);
        Assert.Equal(EllipsisStyle.Dots, project.LoadFormatting().Typography.Ellipsis);
        Assert.Equal("Espera...", project.LoadText(ScriptKey)[0].Translation);
        Assert.Equal(0, second.ExitCode);
        Assert.DoesNotContain("formatting.json", second.Output);
        Assert.DoesNotContain("ajuste-nao-codificavel", second.Output);
        Assert.Contains("0 erros, 0 avisos", second.Output);
    }

    [Fact]
    public async Task Json_traz_os_ajustes_em_conflito_no_check_e_os_trocados_no_fix()
    {
        var project = EllipsisProject();

        var check = await CheckEllipsis(project, "--json");
        var fix = await CheckEllipsis(project, "--json", FixFlag);

        Assert.Equal(2, check.ExitCode);
        using var before = JsonDocument.Parse(check.Output);
        var data = before.RootElement.GetProperty("data");
        var conflict = Assert.Single(data.GetProperty("settingConflicts").EnumerateArray());
        Assert.Equal("typography.ellipsis", conflict.GetProperty("field").GetString());
        Assert.Equal("Reticências", conflict.GetProperty("setting").GetString());
        Assert.Equal("character", conflict.GetProperty("current").GetString());
        Assert.Equal("dots", conflict.GetProperty("safe").GetString());
        Assert.Equal(EllipsisLines, conflict.GetProperty("errors").GetInt32());
        Assert.Equal(EllipsisLines, conflict.GetProperty("lines").GetInt32());
        Assert.Equal(0, data.GetProperty("settingsFixed").GetArrayLength());
        Assert.All(data.GetProperty("issues").EnumerateArray(), i => Assert.Equal("ajuste-nao-codificavel", i.GetProperty("rule").GetString()));

        Assert.Equal(0, fix.ExitCode);
        using var after = JsonDocument.Parse(fix.Output);
        var fixedData = after.RootElement.GetProperty("data");
        var changed = Assert.Single(fixedData.GetProperty("settingsFixed").EnumerateArray());
        Assert.Equal("typography.ellipsis", changed.GetProperty("field").GetString());
        Assert.Equal("character", changed.GetProperty("before").GetString());
        Assert.Equal("dots", changed.GetProperty("after").GetString());
        Assert.Equal(0, fixedData.GetProperty("settingConflicts").GetArrayLength());
    }

    private static string[] Lines(RunResult result) =>
        [.. result.Output.TrimEnd().ReplaceLineEndings("\n").Split('\n').Select(l => l.TrimEnd())];

    private void SaveUntranslated(string key, int count) =>
        _project.SaveText(key, [.. Enumerable.Range(1, count).Select(n =>
            new TextUnit($"{key}:{n:D6}", TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated))]);

    private sealed class TranslationIs(string id, QaSeverity severity, string text) : UnitQaRule
    {
        public override string Id => id;

        public override string Description => "Teste.";

        public override QaSeverity Severity => severity;

        protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
        {
            if (unit.Translation == text)
            {
                yield return $"tradução '{text}' recusada";
            }
        }
    }
}
