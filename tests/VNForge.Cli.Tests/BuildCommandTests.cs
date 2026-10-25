using System.Security.Cryptography;
using System.Text.Json;
using VNForge.Core.Engines;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystl.Synthetic;
using VNForge.Engines.YuRis.Text;
using VNForge.Engines.YuRis.Ypf;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Cli.Tests;

public sealed class BuildCommandTests : IDisposable
{
    private const string YsbinYpf = "ysbin.ypf";
    private const string Yst00001Ybn = "yst00001.ybn";
    private const string ProjectOption = "--project";
    private const string BuildVerb = "build";
    private const string Yst00001000000 = "yst00001:000000";

    private const string GoodMorning = "Good morning";

    private const string Hello = "Hello";

    private const string ForbiddenWord = "palavra-proibida";

    private const string AllowWarningsFlag = "--allow-warnings";

    private const string EncodingFlag = "--encoding";

    private const string JsonFlag = "--json";

    private const string Yst2 = "yst00002";

    private const string Yst1 = "yst00001";

    private const string ReportFlag = "--report";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();
    private readonly string _game;
    private readonly byte[] _scriptsYpf;
    private readonly byte[] _extraYpf;

    public BuildCommandTests()
    {
        _game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "MinhaVN.exe"), "MZ");
        _scriptsYpf = ScriptArchive();
        _extraYpf = ExtraArchive();
        File.WriteAllBytes(Path.Combine(_game, YsbinYpf), _scriptsYpf);
        File.WriteAllBytes(Path.Combine(_game, "bgm.ypf"), new SyntheticYpfBuilder(500).AddFile("bgm01.ogg", [1, 2, 3]).Build().Bytes);
    }

    public void Dispose() => _cli.Dispose();

    private string ProjectPath => Path.Combine(_cli.WorkingDirectory, "minha-vn-ptbr");

    private static SyntheticYstbBuilder NewScript() => new(Yscm);

    private static byte[] ScriptArchive()
    {
        var first = NewScript().WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        first.AddWord("美咲「おはよう」", 1).AddReturnCode(0, 2);
        first.AddCommand("GOSUB", 3, first.StringExpression("ES.CHAR.NAME"), first.StringExpression("美咲"));

        var second = NewScript().WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        second.AddWord("雨が降っていた。", 1).AddReturnCode(0, 2);
        second.AddCommand("GOSUB", 3, second.StringExpression("ES.SEL.SET"), second.StringExpression("はい"), second.StringExpression("いいえ"));

        var list = new SyntheticYstlBuilder().WithVersion(500).AddScript(@"userscript\global.yst").AddScript(@"userscript\01_prologo.yst").Build();
        return new SyntheticYpfBuilder(500)
            .AddFile("ysc.ybn", Yscm.Bytes)
            .AddFile("yst_list.ybn", list.Bytes)
            .AddFile(Yst00001Ybn, first.Build().Bytes)
            .AddFile("yst00002.ybn", second.Build().Bytes)
            .Build().Bytes;
    }

    private static byte[] ExtraArchive()
    {
        var third = NewScript().WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        third.AddWord("夜になった。", 1).AddReturnCode(0, 2);
        return new SyntheticYpfBuilder(500).AddFile("yst00003.ybn", third.Build().Bytes).Build().Bytes;
    }

    private async Task InitAndExtract(bool withExtraArchive = false)
    {
        if (withExtraArchive)
        {
            Directory.CreateDirectory(Path.Combine(_game, "pac"));
            await File.WriteAllBytesAsync(Path.Combine(_game, "pac", "extra.ypf"), _extraYpf, TestContext.Current.CancellationToken);
        }

        var init = await _cli.RunAsync(["init", "minha-vn-ptbr", "--game", _game], TestContext.Current.CancellationToken);
        Assert.Equal(0, init.ExitCode);
        UseStrategy("sjis");
        var extract = await _cli.RunAsync(["extract", ProjectOption, ProjectPath], TestContext.Current.CancellationToken);
        Assert.Equal(0, extract.ExitCode);
    }

    private void UseStrategy(string strategy)
    {
        var project = ProjectFolder.Open(ProjectPath);
        project.SaveConfig(project.LoadConfig() with { Encoding = new EncodingConfig(strategy) });
    }

    private Task<RunResult> Build(params string[] args) =>
        _cli.RunAsync([BuildVerb, .. args, ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

    private void Translate(string key, Func<int, string> translation)
    {
        var project = ProjectFolder.Open(ProjectPath);
        var units = project.LoadText(key).Select((u, i) => u with { Translation = translation(i), Status = TranslationStatus.Translated }).ToList();
        project.SaveText(key, units);
    }

    private static Dictionary<string, string> HashTree(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(directory, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    private static Dictionary<string, byte[]> ReadEntries(string ypfPath)
    {
        using var stream = File.OpenRead(ypfPath);
        using var archive = (YpfArchive)new YpfArchiveFormat().Open(stream);
        return archive.Entries.ToDictionary(e => e.Path, e =>
        {
            using var content = archive.OpenEntry(e);
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            return buffer.ToArray();
        });
    }

    private static IReadOnlyList<TextUnit> Reextract(string name, byte[] bytes)
    {
        var commands = YscmReader.Read(Yscm.Bytes, "ysc.ybn");
        var file = new ScriptFile(name, bytes);
        var script = YstbReader.Read(bytes, name, commands);
        return YstbTextExtractor.Extract(file, script, commands).Units;
    }

    [Fact]
    public async Task Em_terminal_interativo_o_resumo_vira_painel_com_scripts_pacote_acentuacao_e_duracao()
    {
        await InitAndExtract();
        Translate(Yst1, i => $"Traduzido {i}");
        using var rich = new CliHarness(terminal: TestTerminals.InteractivePlain());
        rich.Cli.Qa = new QaEngine([]);

        var result = await rich.RunAsync([BuildVerb, ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.Contains("Resultado do build", result.Output);
        Assert.Contains("1 reconstruídos de 2", result.Output);
        Assert.Contains(YsbinYpf, result.Output);
        Assert.Contains("(original ", result.Output);
        Assert.Contains("Acentuação", result.Output);
        Assert.Contains("sjis · nenhum caractere trocado", result.Output);
        Assert.Contains("Duração", result.Output);
        Assert.DoesNotContain("Reconstruídos", result.Output);
    }

    [Fact]
    public async Task No_terminal_interativo_com_no_color_o_log_de_sempre_continua()
    {
        await InitAndExtract();
        Translate(Yst1, i => $"Traduzido {i}");
        using var rich = new CliHarness(terminal: TestTerminals.InteractivePlain());
        rich.Cli.Qa = new QaEngine([]);

        var result = await rich.RunAsync([BuildVerb, "--no-color", ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Reconstruídos 1 script", result.Output);
        Assert.DoesNotContain("Resultado do build", result.Output);
    }

    [Fact]
    public async Task Sem_traducao_o_ysbin_ypf_do_build_e_identico_ao_original()
    {
        await InitAndExtract();

        var result = await Build();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        var built = Path.Combine(ProjectPath, BuildVerb, YsbinYpf);
        Assert.Equal(_scriptsYpf, await File.ReadAllBytesAsync(built, TestContext.Current.CancellationToken));
        Assert.Equal([YsbinYpf], Directory.EnumerateFiles(Path.Combine(ProjectPath, BuildVerb), "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(Path.Combine(ProjectPath, BuildVerb), f)));
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

    private async Task<RunResult> BuildWithRule(QaSeverity severity, params string[] args)
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? Hello : "Mika");
        _cli.Cli.Qa = new QaEngine([new TranslationIs(ForbiddenWord, severity, Hello)]);
        return await Build(args);
    }

    [Fact]
    public async Task Erro_de_qa_sai_com_2_lista_a_linha_e_nao_grava_build()
    {
        var result = await BuildWithRule(QaSeverity.Error);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("ERRO  yst00001:", result.Output);
        Assert.Contains("[palavra-proibida] tradução 'Hello' recusada", result.Output);
        Assert.Contains("1 erro, 0 avisos", result.Output);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(ProjectPath, BuildVerb)));
    }

    [Fact]
    public async Task Erro_de_qa_continua_parando_com_allow_warnings()
    {
        var result = await BuildWithRule(QaSeverity.Error, AllowWarningsFlag);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(ProjectPath, BuildVerb)));
    }

    [Fact]
    public async Task Aviso_de_qa_para_o_build_sem_allow_warnings()
    {
        var result = await BuildWithRule(QaSeverity.Warning);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("AVISO yst00001:", result.Output);
        Assert.Contains(AllowWarningsFlag, result.Output);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(ProjectPath, BuildVerb)));
    }

    [Fact]
    public async Task Allow_warnings_deixa_so_os_avisos_passar_e_os_lista()
    {
        var result = await BuildWithRule(QaSeverity.Warning, AllowWarningsFlag);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("AVISO yst00001:", result.Output);
        Assert.True(File.Exists(Path.Combine(ProjectPath, BuildVerb, YsbinYpf)));
    }

    [Fact]
    public async Task Caixa_acima_do_limite_aparece_uma_vez_so_na_saida_de_texto()
    {
        await InitAndExtract();
        File.WriteAllText(ProjectFolder.Open(ProjectPath).FormattingPath, """{ "wrap": { "enabled": true, "maxWidth": 5, "maxLines": 2, "charWidth": "uniform" } }""");
        Translate(Yst1, i => i == 0 ? "aaa bbb ccc" : "Mika");
        _cli.Cli.Qa = QaEngine.Default;

        var result = await Build(AllowWarningsFlag);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("[caixa-acima-do-limite] caixa 1 com 3 linhas (máximo 2).", result.Output);
        Assert.Equal(1, result.Output.Split(Yst00001000000).Length - 1);
        Assert.DoesNotContain("Aviso: yst00001", result.Output);
    }

    [Fact]
    public async Task Caixa_acima_do_limite_no_json_fica_so_na_lista_do_qa()
    {
        await InitAndExtract();
        File.WriteAllText(ProjectFolder.Open(ProjectPath).FormattingPath, """{ "wrap": { "enabled": true, "maxWidth": 5, "maxLines": 2, "charWidth": "uniform" } }""");
        Translate(Yst1, i => i == 0 ? "aaa bbb ccc" : "Mika");
        _cli.Cli.Qa = QaEngine.Default;

        var result = await Build(JsonFlag, AllowWarningsFlag);

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("warnings").GetArrayLength());
        var issue = Assert.Single(data.GetProperty("qa").GetProperty("issues").EnumerateArray(), i => i.GetProperty("rule").GetString() == "caixa-acima-do-limite");
        Assert.Equal(Yst00001000000, issue.GetProperty("unit").GetString());
    }

    [Fact]
    public async Task Linhas_sem_traducao_nao_bloqueiam_o_build_e_o_resumo_traz_a_contagem()
    {
        await InitAndExtract();
        _cli.Cli.Qa = QaEngine.Default;

        var result = await Build();

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(ProjectPath, BuildVerb, YsbinYpf)));
        Assert.Contains("linhas sem tradução; o build copiou o original nelas", result.Output);
        Assert.DoesNotContain("[nao-traduzida]", result.Output);
    }

    [Fact]
    public async Task Json_do_build_parcial_traz_a_contagem_de_linhas_sem_traducao()
    {
        await InitAndExtract();
        _cli.Cli.Qa = QaEngine.Default;

        var result = await Build(JsonFlag);

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.True(document.RootElement.GetProperty("data").GetProperty("qa").GetProperty("untranslated").GetInt32() > 0);
    }

    [Fact]
    public async Task Build_bloqueado_por_outro_aviso_nao_lista_nao_traduzida_mas_traz_a_contagem()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? Hello : "Mika");
        _cli.Cli.Qa = new QaEngine([new UntranslatedQaRule(), new TranslationIs(ForbiddenWord, QaSeverity.Warning, Hello)]);

        var result = await Build();

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("[palavra-proibida]", result.Output);
        Assert.DoesNotContain("[nao-traduzida]", result.Output);
        Assert.Contains("linhas sem tradução; o build copiou o original nelas", result.Output);
        Assert.Contains("0 erros, 1 aviso.", result.Output);
    }

    [Fact]
    public async Task Json_de_build_bloqueado_traz_untranslated_sem_listar_as_linhas()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? Hello : "Mika");
        _cli.Cli.Qa = new QaEngine([new UntranslatedQaRule(), new TranslationIs(ForbiddenWord, QaSeverity.Warning, Hello)]);

        var result = await Build(JsonFlag);

        using var document = JsonDocument.Parse(result.Output);
        var data = document.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("untranslated").GetInt32() > 0);
        Assert.Equal(1, data.GetProperty("warnings").GetInt32());
        Assert.Single(data.GetProperty("issues").EnumerateArray());
    }

    [Fact]
    public async Task Aviso_marcado_como_ok_nao_para_o_build()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? Hello : "Mika");
        var engine = new QaEngine([new TranslationIs(ForbiddenWord, QaSeverity.Warning, Hello)]);
        var first = ProjectFolder.Open(ProjectPath).LoadText(Yst1)[0];
        QaFlags.MarkOk(ProjectFolder.Open(ProjectPath), engine, first.Id, ForbiddenWord);
        _cli.Cli.Qa = engine;

        var result = await Build();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1 aviso marcado como ok", result.Output);
    }

    [Fact]
    public async Task Json_de_qa_bloqueado_repete_o_codigo_2_e_traz_as_ocorrencias()
    {
        var result = await BuildWithRule(QaSeverity.Error, JsonFlag);

        Assert.Equal(2, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal(2, document.RootElement.GetProperty("exitCode").GetInt32());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("errors").GetInt32());
        var issue = data.GetProperty("issues")[0];
        Assert.Equal(ForbiddenWord, issue.GetProperty("rule").GetString());
        Assert.Equal("error", issue.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task Json_do_build_com_aviso_liberado_traz_o_qa()
    {
        var result = await BuildWithRule(QaSeverity.Warning, JsonFlag, AllowWarningsFlag);

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var qa = document.RootElement.GetProperty("data").GetProperty("qa");
        Assert.Equal(1, qa.GetProperty("warnings").GetInt32());
        Assert.Equal(0, qa.GetProperty("errors").GetInt32());
    }

    [Fact]
    public async Task Resumo_conta_scripts_linhas_e_pacotes()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? GoodMorning : "Mika");

        var result = await Build();

        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.StartsWith("Gravado: ", lines[0]);
        Assert.EndsWith("ysbin.ypf (1 script alterado)", lines[0]);
        Assert.Equal("Reconstruídos 1 script (2 linhas trocadas); 1 pacote gravado em build/", lines[^1]);
    }

    [Fact]
    public async Task Build_sem_mudanca_desde_o_anterior_nao_reconstroi_nenhum_script_e_mantem_o_build()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? GoodMorning : "Mika");
        await Build();
        var before = HashTree(Path.Combine(ProjectPath, BuildVerb));

        var result = await Build();

        Assert.Equal(0, result.ExitCode);
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("Nada mudou desde o build anterior: 1 script reaproveitado, nenhum reconstruído.", lines[^1]);
        Assert.Equal(before, HashTree(Path.Combine(ProjectPath, BuildVerb)));
    }

    [Fact]
    public async Task Build_incremental_so_reconstroi_o_script_que_mudou()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? GoodMorning : "Mika");
        await Build();
        Translate(Yst2, _ => "Rain");

        var result = await Build(JsonFlag);

        using var document = JsonDocument.Parse(result.Output);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("scripts").GetInt32());
        Assert.Equal(1, data.GetProperty("reusedScripts").GetInt32());
        var entries = ReadEntries(Path.Combine(ProjectPath, BuildVerb, YsbinYpf));
        Assert.Equal(2, entries.Count(e => e.Key.StartsWith("yst0000", StringComparison.Ordinal)));
        Assert.Equal(2, Assert.Single(data.GetProperty("archives").EnumerateArray()).GetProperty("replacedFiles").GetInt32());
    }

    [Fact]
    public async Task Full_reconstroi_tudo_mesmo_sem_mudanca()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? GoodMorning : "Mika");
        await Build();

        var result = await Build("--full", JsonFlag);

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("scripts").GetInt32());
        Assert.Equal(0, data.GetProperty("reusedScripts").GetInt32());
    }

    [Fact]
    public async Task Sem_full_o_build_reaproveita_os_scripts_que_nao_mudaram()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? GoodMorning : "Mika");
        await Build();

        var result = await Build(JsonFlag);

        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal(1, document.RootElement.GetProperty("data").GetProperty("reusedScripts").GetInt32());
    }

    [Fact]
    public async Task Only_changed_deixou_de_existir_e_a_ajuda_aponta_o_full()
    {
        await InitAndExtract();

        var result = await Build("--only-changed", "false");
        var help = await _cli.RunAsync(["build", "--help"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--only-changed", result.Error);
        Assert.Contains("--full", help.Output);
        Assert.DoesNotContain("--only-changed", help.Output);
    }

    [Fact]
    public async Task Encoding_vale_so_para_o_build_e_nao_altera_o_vnforge_json()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? GoodMorning : "Mika");

        var result = await Build(EncodingFlag, "fold", JsonFlag);

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal("fold", document.RootElement.GetProperty("data").GetProperty("encoding").GetProperty("strategy").GetString());
        Assert.Equal("sjis", ProjectFolder.Open(ProjectPath).LoadConfig().Encoding.Strategy);
    }

    [Fact]
    public async Task Encoding_desconhecido_sai_com_erro_em_portugues()
    {
        await InitAndExtract();

        var result = await Build(EncodingFlag, "latin9");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("latin9", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mudar_o_encoding_reconstroi_o_script_mesmo_sem_mudar_a_traducao()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? GoodMorning : "Mika");
        await Build();

        var result = await Build(EncodingFlag, "fold");

        Assert.StartsWith("Reconstruídos 1 script", result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_com_encoding_usa_a_estrategia_pedida()
    {
        await InitAndExtract();

        var result = await Build(ReportFlag, EncodingFlag, "fold");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("'fold'", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sem_traducao_o_resumo_diz_que_o_pacote_saiu_identico()
    {
        await InitAndExtract();

        var result = await Build();

        Assert.Contains("(sem alterações, idêntico ao original)", result.Output);
        Assert.Contains("Reconstruídos 0 scripts (0 linhas trocadas); 1 pacote gravado em build/", result.Output);
    }

    [Fact]
    public async Task Nao_altera_a_pasta_do_jogo()
    {
        var before = HashTree(_game);
        var entriesBefore = Directory.EnumerateFileSystemEntries(_game, "*", SearchOption.AllDirectories).Order().ToArray();
        await InitAndExtract();
        Translate(Yst1, _ => Hello);
        Translate(Yst2, _ => "Rain");

        var result = await Build();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(before, HashTree(_game));
        Assert.Equal(entriesBefore, Directory.EnumerateFileSystemEntries(_game, "*", SearchOption.AllDirectories).Order().ToArray());
    }

    [Fact]
    public async Task Nao_altera_o_cache_descompactado()
    {
        await InitAndExtract();
        Translate(Yst1, _ => Hello);
        var cache = HashTree(Path.Combine(ProjectPath, ".cache", "game"));

        await Build();

        Assert.Equal(cache, HashTree(Path.Combine(ProjectPath, ".cache", "game")));
    }

    [Fact]
    public async Task Linha_traduzida_em_ascii_reabre_no_ypf_e_reextrai_com_a_traducao()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? GoodMorning : "Mika");

        var result = await Build();

        Assert.Equal(0, result.ExitCode);
        var original = ReadEntries(Path.Combine(_game, YsbinYpf));
        var built = ReadEntries(Path.Combine(ProjectPath, BuildVerb, YsbinYpf));
        Assert.Equal(original.Keys.Order(), built.Keys.Order());
        var units = Reextract(Yst00001Ybn, built[Yst00001Ybn]);
        Assert.Equal([GoodMorning, "Mika"], units.Select(u => u.Source));
        Assert.Equal(Reextract(Yst00001Ybn, original[Yst00001Ybn]).Select(u => u.Id), units.Select(u => u.Id));
        Assert.NotEqual(original[Yst00001Ybn], built[Yst00001Ybn]);
        foreach (var other in original.Keys.Where(k => k != Yst00001Ybn))
        {
            Assert.Equal(original[other], built[other]);
        }
    }

    [Fact]
    public async Task Pacote_em_subpasta_mantem_o_caminho_e_o_sem_mudanca_sai_identico()
    {
        await InitAndExtract(withExtraArchive: true);
        Translate("yst00003", _ => "Night fell.");

        var result = await Build(JsonFlag);

        Assert.Equal(0, result.ExitCode);
        var build = Path.Combine(ProjectPath, BuildVerb);
        Assert.Equal(_scriptsYpf, await File.ReadAllBytesAsync(Path.Combine(build, YsbinYpf), TestContext.Current.CancellationToken));
        var extra = ReadEntries(Path.Combine(build, "pac", "extra.ypf"));
        Assert.Equal("Night fell.", Assert.Single(Reextract("yst00003.ybn", extra["yst00003.ybn"])).Source);
        using var document = JsonDocument.Parse(result.Output);
        var archives = document.RootElement.GetProperty("data").GetProperty("archives");
        Assert.Equal(["pac/extra.ypf", YsbinYpf], archives.EnumerateArray().Select(a => a.GetProperty("path").GetString()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Json_traz_o_resumo_no_envelope()
    {
        await InitAndExtract();
        Translate(Yst2, _ => "Rain");

        var result = await Build(JsonFlag);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
        var data = root.GetProperty("data");
        Assert.Equal(1, data.GetProperty("scripts").GetInt32());
        Assert.Equal(2, data.GetProperty("totalScripts").GetInt32());
        Assert.Equal(3, data.GetProperty("lines").GetInt32());
        var rebuilt = Assert.Single(data.GetProperty("rebuilt").EnumerateArray());
        Assert.Equal(Yst2, rebuilt.GetProperty("key").GetString());
        Assert.Equal("yst00002.ybn", rebuilt.GetProperty("name").GetString());
        var archive = Assert.Single(data.GetProperty("archives").EnumerateArray());
        Assert.Equal(YsbinYpf, archive.GetProperty("path").GetString());
        Assert.Equal(1, archive.GetProperty("replacedFiles").GetInt32());
        Assert.Equal(0, data.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public async Task Caractere_fora_do_shift_jis_aborta_citando_o_id_e_nao_toca_no_build_anterior()
    {
        await InitAndExtract();
        var first = await Build();
        Assert.Equal(0, first.ExitCode);
        var build = Path.Combine(ProjectPath, BuildVerb);
        var before = HashTree(build);
        Translate(Yst1, i => i == 0 ? "Bom dia 😀" : "Mika");

        var result = await Build();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(Yst00001000000, result.Error);
        Assert.Equal(before, HashTree(build));
        Assert.False(Directory.Exists(build + ".tmp"));
    }

    [Fact]
    public async Task Marcador_invalido_aborta_citando_o_id_sem_criar_o_build()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? "Bom {XYZ:1} dia" : "Mika");

        var result = await Build();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(Yst00001000000, result.Error);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(ProjectPath, BuildVerb), "*", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.Combine(ProjectPath, "build.tmp")));
    }

    [Fact]
    public async Task Texto_que_nao_e_mais_o_do_script_aborta_citando_o_id()
    {
        await InitAndExtract();
        var project = ProjectFolder.Open(ProjectPath);
        var units = project.LoadText(Yst1).Select((u, i) => i == 0 ? u with { Source = "outra coisa", Translation = "Hi", Status = TranslationStatus.Translated } : u).ToList();
        project.SaveText(Yst1, units);

        var result = await Build();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(Yst00001000000, result.Error);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(ProjectPath, BuildVerb), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Erro_em_json_sai_no_envelope()
    {
        await InitAndExtract();
        Translate(Yst1, _ => "😀");

        var result = await Build(JsonFlag);

        Assert.Equal(1, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains(Yst00001000000, document.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Jogo_que_mudou_depois_da_extracao_e_recusado()
    {
        await InitAndExtract();
        Translate(Yst1, _ => Hello);
        var changed = NewScript().WithVersion(500).WithKey(SyntheticYstbBuilder.KeyV290);
        changed.AddWord("まったく違う", 1).AddReturnCode(0, 2);
        File.WriteAllBytes(
            Path.Combine(_game, YsbinYpf),
            new SyntheticYpfBuilder(500).AddFile("ysc.ybn", Yscm.Bytes).AddFile(Yst00001Ybn, changed.Build().Bytes).Build().Bytes);

        var result = await Build();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("o jogo mudou depois da extração", result.Error);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(ProjectPath, BuildVerb), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Build_anterior_e_substituido_por_inteiro()
    {
        await InitAndExtract();
        var stale = Path.Combine(ProjectPath, BuildVerb, "sobra.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(stale) ?? ".");
        await File.WriteAllTextAsync(stale, "x", TestContext.Current.CancellationToken);

        var result = await Build();

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(stale));
    }

    [Fact]
    public async Task Sem_extract_pede_para_rodar_vnf_extract()
    {
        var init = await _cli.RunAsync(["init", "minha-vn-ptbr", "--game", _game], TestContext.Current.CancellationToken);
        Assert.Equal(0, init.ExitCode);
        UseStrategy("sjis");

        var result = await Build();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Rode vnf extract", result.Error);
    }

    [Fact]
    public async Task Texto_sem_script_correspondente_vira_aviso()
    {
        await InitAndExtract();
        var project = ProjectFolder.Open(ProjectPath);
        project.SaveText("yst99999", [new TextUnit("yst99999:000000", TextUnitKind.Dialogue, null, "x", "y", TranslationStatus.Translated)]);

        var result = await Build();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Aviso: text/yst99999.json não corresponde a nenhum script", result.Output);
    }

    [Fact]
    public async Task Pasta_que_nao_e_projeto_gera_erro_em_portugues()
    {
        var result = await _cli.RunAsync([BuildVerb, ProjectOption, _cli.WorkingDirectory], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("vnforge.json não encontrado", result.Error);
    }

    [Fact]
    public async Task Inject_grava_o_build_no_jogo_e_restore_devolve_o_original()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? GoodMorning : "Mika");
        await Build();
        var gameYpf = Path.Combine(_game, YsbinYpf);

        var inject = await _cli.RunAsync(["inject", "--yes", ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, inject.ExitCode);
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(ProjectPath, BuildVerb, YsbinYpf), TestContext.Current.CancellationToken), await File.ReadAllBytesAsync(gameYpf, TestContext.Current.CancellationToken));
        Assert.NotEqual(_scriptsYpf, await File.ReadAllBytesAsync(gameYpf, TestContext.Current.CancellationToken));

        var restore = await _cli.RunAsync(["restore", "--yes", ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, restore.ExitCode);
        Assert.Equal(_scriptsYpf, await File.ReadAllBytesAsync(gameYpf, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Aborto_por_caractere_nao_codificavel_lista_todas_as_linhas_de_uma_vez()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? "«ªã»" : "Mika ã");

        var result = await Build();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("yst00001:000000 posição 0: '«' (U+00AB)", result.Error);
        Assert.Contains("yst00001:000000 posição 2: 'ã' (U+00E3)", result.Error);
        Assert.Contains("yst00001:000513 posição 5: 'ã' (U+00E3)", result.Error);
        Assert.False(File.Exists(Path.Combine(ProjectPath, BuildVerb, YsbinYpf)));
    }

    [Fact]
    public async Task Estrategia_sem_encoder_aborta_com_mensagem_clara()
    {
        await InitAndExtract();
        UseStrategy("remap");

        var result = await Build();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("'remap' ainda não está disponível", result.Error);
    }

    [Fact]
    public async Task Report_lista_cada_caractere_com_posicao_e_nao_reconstroi_nada()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? "«ªã" : "Mika");

        var result = await Build(ReportFlag);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Estratégia de codificação 'sjis': 2 linhas conferidas.", result.Output);
        Assert.Contains("yst00001:000000 posição 1: 'ª' (U+00AA) não codificável", result.Output);
        Assert.Contains("3 caracteres não codificáveis; 0 caracteres trocados.", result.Output);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(ProjectPath, BuildVerb)));
    }

    [Fact]
    public async Task Report_sem_problemas_sai_com_zero()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? GoodMorning : "Mika");

        var result = await Build(ReportFlag);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("0 caracteres não codificáveis; 0 caracteres trocados.", result.Output);
    }

    [Fact]
    public async Task Report_em_json_traz_caractere_ponto_de_codigo_posicao_e_substituto()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? "a«" : "Mika");

        var result = await Build(ReportFlag, JsonFlag);

        Assert.Equal(2, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(2, root.GetProperty("exitCode").GetInt32());
        var data = root.GetProperty("data");
        Assert.Equal("sjis", data.GetProperty("strategy").GetString());
        Assert.Equal(1, data.GetProperty("unencodable").GetInt32());
        var issue = Assert.Single(data.GetProperty("issues").EnumerateArray());
        Assert.Equal(Yst00001000000, issue.GetProperty("unit").GetString());
        Assert.Equal("«", issue.GetProperty("character").GetString());
        Assert.Equal("U+00AB", issue.GetProperty("codePoint").GetString());
        Assert.Equal(1, issue.GetProperty("position").GetInt32());
        Assert.False(issue.TryGetProperty("replacement", out _));
    }

    [Fact]
    public async Task Json_do_build_traz_o_bloco_encoding()
    {
        await InitAndExtract();
        Translate(Yst1, i => i == 0 ? GoodMorning : "Mika");

        var result = await Build(JsonFlag);

        using var document = JsonDocument.Parse(result.Output);
        var encoding = document.RootElement.GetProperty("data").GetProperty("encoding");
        Assert.Equal("sjis", encoding.GetProperty("strategy").GetString());
        Assert.Equal(0, encoding.GetProperty("replaced").GetInt32());
    }
}
