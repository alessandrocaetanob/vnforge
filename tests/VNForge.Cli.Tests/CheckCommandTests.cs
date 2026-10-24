using System.Text.Json;
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
