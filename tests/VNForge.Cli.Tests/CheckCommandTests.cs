using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class CheckCommandTests : IDisposable
{
    private readonly CliHarness _cli = new();
    private readonly ProjectFolder _project;
    private readonly string _projectPath;

    public CheckCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = ProjectFolder.Create(_projectPath, new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Unit(int n, string translation) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, null, "原文", translation, TranslationStatus.Translated);

    private void Save(params string[] translations) =>
        _project.SaveText("yst00001", [.. translations.Select((t, i) => Unit(i + 1, t))]);

    private void UseRules(params IQaRule[] rules) => _cli.Cli.Qa = new QaEngine(rules);

    private Task<RunResult> Check(params string[] args) =>
        _cli.RunAsync(["check", "--project", _projectPath, .. args], TestContext.Current.CancellationToken);

    [Fact]
    public async Task Erro_bloqueante_sai_com_2_e_lista_no_formato_do_doc()
    {
        Save("Olá", "proibida");
        UseRules(new TranslationIs("palavra-proibida", QaSeverity.Error, "proibida"));

        var result = await Check();

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("ERRO  yst00001:000002  tradução 'proibida' recusada", result.Output);
        Assert.Contains("1 erro, 0 avisos", result.Output);
    }

    [Fact]
    public async Task So_avisos_saem_com_0()
    {
        Save("Olá", "proibida");
        UseRules(new TranslationIs("palavra-proibida", QaSeverity.Warning, "proibida"));

        var result = await Check();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("AVISO yst00001:000002  tradução 'proibida' recusada", result.Output);
        Assert.Contains("0 erros, 1 aviso", result.Output);
    }

    [Fact]
    public async Task Sem_ocorrencias_sai_com_0()
    {
        Save("Olá");
        UseRules(new TranslationIs("palavra-proibida", QaSeverity.Error, "proibida"));

        var result = await Check();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("0 erros, 0 avisos", result.Output);
    }

    [Fact]
    public async Task Severity_error_esconde_os_avisos()
    {
        Save("a", "b");
        UseRules(new TranslationIs("aviso-a", QaSeverity.Warning, "a"), new TranslationIs("erro-b", QaSeverity.Error, "b"));

        var result = await Check("--severity", "error");

        Assert.Equal(2, result.ExitCode);
        Assert.DoesNotContain("AVISO", result.Output);
        Assert.Contains("1 erro, 0 avisos", result.Output);
    }

    [Fact]
    public async Task Severity_warn_lista_erros_e_avisos()
    {
        Save("a", "b");
        UseRules(new TranslationIs("aviso-a", QaSeverity.Warning, "a"), new TranslationIs("erro-b", QaSeverity.Error, "b"));

        var result = await Check("--severity", "warn");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("1 erro, 1 aviso", result.Output);
    }

    [Fact]
    public async Task Severity_invalida_sai_com_1()
    {
        Save("a");
        UseRules();

        var result = await Check("--severity", "grave");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--severity", result.Error + result.Output);
    }

    [Fact]
    public async Task Rules_roda_so_as_regras_pedidas_e_aceita_lista()
    {
        Save("a", "b");
        UseRules(new TranslationIs("regra-a", QaSeverity.Error, "a"), new TranslationIs("regra-b", QaSeverity.Error, "b"));

        var one = await Check("--rules", "regra-b");
        var both = await Check("--rules", "regra-a, regra-b");

        Assert.Contains("1 erro,", one.Output);
        Assert.DoesNotContain("000001", one.Output);
        Assert.Contains("2 erros,", both.Output);
    }

    [Fact]
    public async Task Rules_com_id_desconhecido_sai_com_1()
    {
        Save("a");
        UseRules(new TranslationIs("regra-a", QaSeverity.Error, "a"));

        var result = await Check("--rules", "nao-existe");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("nao-existe", result.Error + result.Output);
    }

    [Fact]
    public async Task Fix_corrige_grava_e_a_segunda_rodada_nao_muda_nada()
    {
        Save("Olá  mundo", "ok");
        UseRules(new DoubleSpaceRule());

        var first = await Check("--fix");
        var second = await Check("--fix");

        Assert.Equal(0, first.ExitCode);
        Assert.Contains("yst00001:000001", first.Output);
        Assert.Contains("espaco-duplo", first.Output);
        Assert.Equal("Olá mundo", _project.LoadText("yst00001")[0].Translation);
        Assert.DoesNotContain("espaco-duplo", second.Output);
        Assert.Contains("0 erros, 0 avisos", second.Output);
    }

    [Fact]
    public async Task Fix_o_codigo_de_saida_vale_depois_da_correcao()
    {
        Save("Olá  mundo", "proibida");
        UseRules(new DoubleSpaceRule(), new TranslationIs("palavra-proibida", QaSeverity.Error, "proibida"));

        var result = await Check("--fix");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("1 erro, 0 avisos", result.Output);
        Assert.DoesNotContain("AVISO", result.Output);
    }

    [Fact]
    public async Task Fix_com_rules_de_regra_sem_correcao_nao_falha()
    {
        Save("Olá  mundo");
        UseRules(new DoubleSpaceRule(), new TranslationIs("palavra-proibida", QaSeverity.Error, "x"));

        var result = await Check("--fix", "--rules", "palavra-proibida");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Olá  mundo", _project.LoadText("yst00001")[0].Translation);
    }

    [Fact]
    public async Task Fix_com_rules_desconhecida_sai_com_1_sem_gravar()
    {
        Save("Olá  mundo");
        UseRules(new DoubleSpaceRule());

        var result = await Check("--fix", "--rules", "espaco-duplo,nao-existe");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("Olá  mundo", _project.LoadText("yst00001")[0].Translation);
    }

    [Fact]
    public async Task Sem_fix_nao_grava_nada()
    {
        Save("Olá  mundo");
        UseRules(new DoubleSpaceRule());

        await Check();

        Assert.Equal("Olá  mundo", _project.LoadText("yst00001")[0].Translation);
    }

    [Fact]
    public async Task Json_traz_contagens_ocorrencias_e_codigo_de_saida()
    {
        Save("a", "b");
        UseRules(new TranslationIs("aviso-a", QaSeverity.Warning, "a"), new TranslationIs("erro-b", QaSeverity.Error, "b"));

        var result = await Check("--json");

        Assert.Equal(2, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("errors").GetInt32());
        Assert.Equal(1, data.GetProperty("warnings").GetInt32());
        Assert.Equal(0, data.GetProperty("fixed").GetInt32());
        var issues = data.GetProperty("issues").EnumerateArray().ToList();
        Assert.Equal(["warn", "error"], issues.Select(i => i.GetProperty("severity").GetString()));
        Assert.Equal(["aviso-a", "erro-b"], issues.Select(i => i.GetProperty("rule").GetString()));
        Assert.Equal("yst00001:000002", issues[1].GetProperty("unit").GetString());
    }

    [Fact]
    public async Task Aviso_marcado_como_ok_nao_conta_e_aparece_no_resumo()
    {
        Save("proibida");
        var engine = new QaEngine([new TranslationIs("palavra-proibida", QaSeverity.Warning, "proibida")]);
        QaFlags.MarkOk(_project, engine, "yst00001:000001", "palavra-proibida");
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
