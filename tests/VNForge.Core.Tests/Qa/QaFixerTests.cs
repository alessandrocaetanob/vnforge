using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

public sealed class QaFixerTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;

    public QaFixerTests()
    {
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig("jogo", "Jogo.exe"),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("sjis"),
                AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private static TextUnit Unit(string id, string? translation, TranslationStatus status = TranslationStatus.Translated, params string[] flags) =>
        new(id, TextUnitKind.Dialogue, null, "原文", translation, status) { Flags = flags };

    private static Dictionary<string, IReadOnlyList<TextUnit>> Texts(params TextUnit[] units) =>
        new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = units };

    private static string Fixed(string translation)
    {
        var result = QaFixer.Plan(Texts(Unit("a:1", translation)), QaEngine.Default);
        return result.Texts.TryGetValue("a", out var units) ? units[0].Translation ?? translation : translation;
    }

    [Fact]
    public void Colapsa_espacos_comuns_repetidos_em_um()
    {
        Assert.Equal("Olá, mundo", Fixed("Olá,   mundo"));
    }

    [Fact]
    public void Nao_mexe_em_nbsp_nem_em_espaco_ideografico()
    {
        Assert.Equal("a\u00A0 b", Fixed("a\u00A0 b"));
        Assert.Equal("a\u3000\u3000b", Fixed("a\u3000\u3000b"));
    }

    [Fact]
    public void Nao_junta_espacos_separados_por_marcador()
    {
        Assert.Equal(@"Olá \n mundo", Fixed(@"Olá \n mundo"));
        Assert.Equal(@"a \n b", Fixed(@"a  \n  b"));
    }

    [Fact]
    public void Tira_o_espaco_antes_de_pontuacao()
    {
        Assert.Equal("Sério? Sim, claro!", Fixed("Sério ? Sim , claro  !"));
    }

    [Fact]
    public void Combina_as_duas_correcoes_e_lista_cada_uma()
    {
        var result = QaFixer.Plan(Texts(Unit("a:1", "Oi  , tudo bem")), QaEngine.Default);

        Assert.Equal(["espaco-duplo", "espaco-antes-pontuacao"], result.Fixes.Select(f => f.RuleId));
        var last = result.Fixes[^1];
        Assert.Equal("a:1", last.UnitId);
        Assert.Equal("a", last.ScriptKey);
        Assert.Equal("Oi , tudo bem", last.Before);
        Assert.Equal("Oi, tudo bem", last.After);
        Assert.Equal("Oi, tudo bem", result.Texts["a"][0].Translation);
    }

    [Fact]
    public void Segunda_rodada_nao_muda_nada()
    {
        var first = QaFixer.Plan(Texts(Unit("a:1", "A  b ,  c ?  d"), Unit("a:2", "limpo")), QaEngine.Default);
        var second = QaFixer.Plan(first.Texts, QaEngine.Default);
        Assert.NotEmpty(first.Fixes);
        Assert.Empty(second.Fixes);
        Assert.Empty(second.Texts);
    }

    [Fact]
    public void Respeita_o_ok_do_revisor_so_na_regra_marcada()
    {
        var result = QaFixer.Plan(Texts(Unit("a:1", "a  b ,c", TranslationStatus.Translated, "espaco-duplo")), QaEngine.Default);

        Assert.Equal("a  b,c", result.Texts["a"][0].Translation);
        Assert.Equal(["espaco-antes-pontuacao"], result.Fixes.Select(f => f.RuleId));
    }

    [Fact]
    public void Ignora_linha_sem_traducao_e_nao_mexe_no_status()
    {
        var result = QaFixer.Plan(
            Texts(Unit("a:1", "a  b", TranslationStatus.Untranslated), Unit("a:2", null), Unit("a:3", "c  d", TranslationStatus.Reviewed)),
            QaEngine.Default);

        var fix = Assert.Single(result.Fixes);
        Assert.Equal("a:3", fix.UnitId);
        Assert.Equal(TranslationStatus.Reviewed, result.Texts["a"][2].Status);
        Assert.Equal("a  b", result.Texts["a"][0].Translation);
    }

    [Fact]
    public void Filtro_de_regras_limita_a_correcao()
    {
        var result = QaFixer.Plan(Texts(Unit("a:1", "a  b ,c")), QaEngine.Default, ["espaco-antes-pontuacao"]);

        Assert.Equal("a  b,c", result.Texts["a"][0].Translation);
    }

    [Fact]
    public void Filtro_com_regra_desconhecida_falha()
    {
        Assert.Throws<VNForgeException>(() => QaFixer.Plan(Texts(Unit("a:1", "x")), QaEngine.Default, ["nao-existe"]));
    }

    [Fact]
    public void Filtro_com_regra_sem_correcao_falha()
    {
        Assert.Throws<VNForgeException>(() => QaFixer.Plan(Texts(Unit("a:1", "x")), QaEngine.Default, ["marcador-diferente"]));
    }

    [Fact]
    public void Apply_grava_so_o_script_que_mudou_e_a_segunda_chamada_nao_regrava()
    {
        _project.SaveText("a", [Unit("a:1", "Olá,  mundo")]);
        _project.SaveText("b", [new TextUnit("b:1", TextUnitKind.Dialogue, null, "原文", "limpo", TranslationStatus.Translated)]);
        var pathB = _project.TextPath("b");
        File.SetLastWriteTimeUtc(pathB, File.GetLastWriteTimeUtc(pathB).AddMinutes(-5));
        var stampB = File.GetLastWriteTimeUtc(pathB);

        var first = QaFixer.Apply(_project, QaEngine.Default);

        Assert.Single(first.Fixes);
        Assert.Equal("Olá, mundo", _project.LoadText("a")[0].Translation);
        Assert.Equal(stampB, File.GetLastWriteTimeUtc(pathB));

        var pathA = _project.TextPath("a");
        var bytes = File.ReadAllBytes(pathA);
        var second = QaFixer.Apply(_project, QaEngine.Default);

        Assert.Empty(second.Fixes);
        Assert.Equal(bytes, File.ReadAllBytes(pathA));
    }

    [Fact]
    public void Apply_espera_a_gravacao_em_andamento_e_corrige_o_que_ela_gravou()
    {
        _project.SaveText("a", [Unit("a:1", "limpo")]);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var writer = new Thread(() =>
        {
            lock (ProjectTextLock.For(_project))
            {
                entered.Set();
                release.Wait();
                _project.SaveText("a", [Unit("a:1", "Olá,  mundo")]);
            }
        });
        writer.Start();
        entered.Wait(TestContext.Current.CancellationToken);
        QaFixResult? result = null;
        var fixer = new Thread(() => result = QaFixer.Apply(_project, QaEngine.Default));
        fixer.Start();

        // Quem não espera a trava termina sem nunca bloquear; quem espera fica bloqueado até a gravação acabar.
        SpinWait.SpinUntil(() => fixer.ThreadState.HasFlag(ThreadState.WaitSleepJoin) || fixer.ThreadState.HasFlag(ThreadState.Stopped));
        release.Set();
        writer.Join();
        fixer.Join();

        Assert.Single(result!.Fixes);
        Assert.Equal("Olá, mundo", _project.LoadText("a")[0].Translation);
    }

    [Fact]
    public void Apply_a_seco_lista_sem_gravar()
    {
        _project.SaveText("a", [Unit("a:1", "Olá,  mundo")]);

        var result = QaFixer.Apply(_project, QaEngine.Default, dryRun: true);

        Assert.Single(result.Fixes);
        Assert.Equal("Olá,  mundo", _project.LoadText("a")[0].Translation);
    }
    private static QaReport Run(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts) =>
        QaEngine.Default.Run(QaContext.ForTexts(texts));

    [Fact]
    public void Propoe_a_correcao_de_cada_regra_sobre_a_traducao_original_sem_encadear()
    {
        var texts = Texts(Unit("a:1", "Vamos,  anda !"));

        var proposals = QaFixer.Propose(texts, QaEngine.Default, Run(texts).Issues);

        // Cada regra propõe a sua correção sobre o texto gravado: a de espaço duplo não vê a de pontuação.
        Assert.Equal(
            [("espaco-duplo", "Vamos, anda !"), ("espaco-antes-pontuacao", "Vamos,  anda!")],
            proposals.Select(p => (p.RuleId, p.After)));
        Assert.All(proposals, p => Assert.Equal(("a:1", "a", "Vamos,  anda !"), (p.UnitId, p.ScriptKey, p.Before)));
    }

    [Fact]
    public void Propoe_uma_vez_por_linha_e_regra_mesmo_com_varias_ocorrencias()
    {
        var texts = Texts(Unit("a:1", "a  b  c"));

        var proposals = QaFixer.Propose(texts, QaEngine.Default, Run(texts).Issues);

        Assert.Equal("a b c", Assert.Single(proposals).After);
    }

    [Fact]
    public void Nao_propoe_para_regra_sem_corretor_nem_para_linha_silenciada()
    {
        var texts = Texts(
            Unit("a:1", null, TranslationStatus.Approved),
            Unit("a:2", "dois  espaços", TranslationStatus.Translated, "espaco-duplo"));

        var report = Run(texts);

        Assert.NotEmpty(report.Issues);
        Assert.Empty(QaFixer.Propose(texts, QaEngine.Default, report.Issues));
    }

    [Fact]
    public void ApplyOne_corrige_so_a_linha_e_a_regra_pedidas()
    {
        _project.SaveText("a", [Unit("a:1", "Olá,  mundo !"), Unit("a:2", "outra  linha")]);

        var fix = QaFixer.ApplyOne(_project, QaEngine.Default, "a", "a:1", "espaco-duplo");

        Assert.NotNull(fix);
        Assert.Equal("Olá, mundo !", fix.After);
        var saved = _project.LoadText("a");
        Assert.Equal("Olá, mundo !", saved[0].Translation);
        Assert.Equal("outra  linha", saved[1].Translation);
    }

    [Fact]
    public void ApplyOne_devolve_nulo_e_nao_grava_quando_nada_muda()
    {
        _project.SaveText("a", [Unit("a:1", "limpo")]);
        var path = _project.TextPath("a");
        var bytes = File.ReadAllBytes(path);

        Assert.Null(QaFixer.ApplyOne(_project, QaEngine.Default, "a", "a:1", "espaco-duplo"));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void ApplyOne_com_regra_sem_corretor_ou_desconhecida_falha()
    {
        _project.SaveText("a", [Unit("a:1", "x")]);

        Assert.Throws<VNForgeException>(() => QaFixer.ApplyOne(_project, QaEngine.Default, "a", "a:1", "traducao-vazia"));
        Assert.Throws<VNForgeException>(() => QaFixer.ApplyOne(_project, QaEngine.Default, "a", "a:1", "nada"));
    }
}
