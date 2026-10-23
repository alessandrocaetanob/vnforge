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

    private static IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> Texts(params TextUnit[] units) =>
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
    public void Apply_a_seco_lista_sem_gravar()
    {
        _project.SaveText("a", [Unit("a:1", "Olá,  mundo")]);

        var result = QaFixer.Apply(_project, QaEngine.Default, dryRun: true);

        Assert.Single(result.Fixes);
        Assert.Equal("Olá,  mundo", _project.LoadText("a")[0].Translation);
    }
}
