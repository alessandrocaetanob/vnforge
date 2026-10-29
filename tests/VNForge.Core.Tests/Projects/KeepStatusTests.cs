using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>O estado "Não traduzir" (<see cref="TranslationStatus.Keep"/>, #373): a linha fica como no original de propósito e conta como pronta.</summary>
public sealed class KeepStatusTests
{
    private const string FontName = "ＭＳ ゴシック";
    private static readonly DateTimeOffset Agora = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static TextUnit Linha(int n, TranslationStatus status, string? traducao = null, string? origem = null) =>
        new($"yst00001:{n:D6}", TextUnitKind.System, null, origem ?? $"原文{n}", traducao, status)
        {
            Origin = traducao is null ? null : new TranslationOrigin("anthropic", "claude-opus-5-5", Agora.AddDays(-1)),
        };

    private static ProjectFolder NovoProjeto(string raiz, params TextUnit[] linhas)
    {
        var projeto = ProjectFolder.Create(raiz, new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        projeto.SaveText("yst00001", linhas);
        return projeto;
    }

    private static LineEditor EditorDe(ProjectFolder projeto) => new(projeto, new FakeTimeProvider(Agora));

    [Fact]
    public void O_status_grava_keep_no_json_e_um_arquivo_antigo_continua_lendo_igual()
    {
        var keep = Linha(1, TranslationStatus.Keep, origem: FontName);
        var json = TextUnitJson.Serialize(keep);

        Assert.Contains("\"status\":\"keep\"", json);
        Assert.Equal(TranslationStatus.Keep, TextUnitJson.Deserialize(json).Status);

        var antigo = TextFileSerializer.Serialize([Linha(1, TranslationStatus.Untranslated), Linha(2, TranslationStatus.Approved, "ok")]);
        Assert.DoesNotContain("keep", antigo);
        Assert.Equal(
            [TranslationStatus.Untranslated, TranslationStatus.Approved],
            TextFileSerializer.Parse(antigo, "text/yst00001.json").Select(u => u.Status));
    }

    [Fact]
    public void Ida_e_volta_do_text_json_com_linha_keep_nao_muda_nada()
    {
        using var pasta = new TempFolder();
        var linhas = new[] { Linha(1, TranslationStatus.Keep, origem: FontName), Linha(2, TranslationStatus.Translated, "Olá") };
        var projeto = NovoProjeto(pasta.Path, linhas);

        var lidas = projeto.LoadText("yst00001");

        Assert.Equal(linhas.Select(TextUnitJson.Serialize), lidas.Select(TextUnitJson.Serialize));
        Assert.Equal(TextFileSerializer.Serialize(linhas), TextFileSerializer.Serialize(lidas));
    }

    [Fact]
    public void Marcar_a_ultima_linha_pendente_como_keep_leva_a_cena_de_99_a_100()
    {
        var antes = StatusCounts.Of([.. Enumerable.Range(1, 99).Select(n => Linha(n, TranslationStatus.Approved, "ok")), Linha(100, TranslationStatus.Untranslated)]);
        var depois = StatusCounts.Of([.. Enumerable.Range(1, 99).Select(n => Linha(n, TranslationStatus.Approved, "ok")), Linha(100, TranslationStatus.Keep)]);

        Assert.Equal(99, antes.Percent);
        Assert.Equal(100, depois.Percent);
        Assert.Equal(1, depois.Keep);
        Assert.Equal(0, depois.Untranslated);
        Assert.Equal(100, depois.Total);
        Assert.Equal(100, depois.Ready);
        Assert.Equal(0, depois.ToReview);
    }

    [Fact]
    public void Keep_conta_como_pronta_no_progresso_do_script_e_na_barra_de_distribuicao()
    {
        var counts = new StatusCounts(0, 0, 0, 0, 1, Keep: 1);

        Assert.Equal(100, new ScriptProgress("a", counts).CompletedPercent);
        var distribuicao = StatusDistribution.Of(counts);
        Assert.Equal(0, distribuicao.Pending);
        Assert.Equal(100, distribuicao.Approved + distribuicao.Reviewed);
    }

    [Fact]
    public void Contagem_antiga_de_cinco_campos_continua_valendo()
    {
        var counts = new StatusCounts(1, 2, 3, 4, 5);

        Assert.Equal(0, counts.Keep);
        Assert.Equal(15, counts.Total);
        Assert.Equal(14, counts.Done);
    }

    [Fact]
    public void Linha_keep_nao_gera_nao_traduzida_nem_traducao_vazia()
    {
        var texts = new Dictionary<string, IReadOnlyList<TextUnit>> { ["yst00001"] = [Linha(1, TranslationStatus.Keep, origem: FontName), Linha(2, TranslationStatus.Untranslated)] };

        var issues = QaEngine.Default.Run(QaContext.ForTexts(texts)).Issues;

        Assert.DoesNotContain(issues, i => i.UnitId == "yst00001:000001" && i.RuleId is "nao-traduzida" or "traducao-vazia");
        Assert.Contains(issues, i => i.UnitId == "yst00001:000002" && i.RuleId == "nao-traduzida");
    }

    [Fact]
    public void Linha_keep_nao_tem_traducao_utilizavel_e_o_build_copia_o_original()
    {
        var keep = Linha(1, TranslationStatus.Keep, origem: FontName);

        Assert.False(keep.HasTranslation);
        Assert.False(keep.NeedsRewrite);
    }

    [Fact]
    public void Filtro_Keep_mostra_so_as_linhas_nao_traduzir()
    {
        var keep = new LineEntry("a", 1, Linha(1, TranslationStatus.Keep), []);
        var pendente = new LineEntry("a", 2, Linha(2, TranslationStatus.Untranslated), []);

        var filtro = new LineFilter(LineStatusFilter.Keep);

        Assert.True(filtro.Matches(keep));
        Assert.False(filtro.Matches(pendente));
        Assert.False(new LineFilter(LineStatusFilter.Pending).Matches(keep));
    }

    [Fact]
    public void SetKeep_marca_a_linha_limpa_a_traducao_e_a_origem_e_grava_no_arquivo()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path, Linha(1, TranslationStatus.Machine, "Fonte MS"), Linha(2, TranslationStatus.Untranslated));

        var gravada = EditorDe(projeto).SetKeep("yst00001", "yst00001:000001", keep: true);

        Assert.Equal(TranslationStatus.Keep, gravada.Status);
        Assert.Null(gravada.Translation);
        Assert.Null(gravada.Origin);
        Assert.Equal(TextUnitJson.Serialize(gravada), TextUnitJson.Serialize(projeto.LoadText("yst00001")[0]));
        Assert.Equal(TranslationStatus.Untranslated, projeto.LoadText("yst00001")[1].Status);
    }

    [Fact]
    public void SetKeep_falso_devolve_a_linha_a_sem_traducao_e_nao_mexe_nas_outras()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path, Linha(1, TranslationStatus.Keep), Linha(2, TranslationStatus.Approved, "ok"));
        var editor = EditorDe(projeto);

        var volta = editor.SetKeep("yst00001", "yst00001:000001", keep: false);
        var aprovada = editor.SetKeep("yst00001", "yst00001:000002", keep: false);

        Assert.Equal(TranslationStatus.Untranslated, volta.Status);
        Assert.Equal(TranslationStatus.Approved, aprovada.Status);
        Assert.Equal("ok", aprovada.Translation);
    }

    [Fact]
    public void SetKeep_repetido_nao_regrava_o_arquivo()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path, Linha(1, TranslationStatus.Keep));
        var gravacoes = 0;
        var editor = new LineEditor(projeto, new FakeTimeProvider(Agora), (key, units) => { gravacoes++; projeto.SaveText(key, units); });

        editor.SetKeep("yst00001", "yst00001:000001", keep: true);

        Assert.Equal(0, gravacoes);
    }

    [Fact]
    public void SetKeepMany_grava_um_arquivo_por_script_e_devolve_o_que_mudou()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path, Linha(1, TranslationStatus.Untranslated), Linha(2, TranslationStatus.Keep), Linha(3, TranslationStatus.Machine, "x"));

        var mudancas = EditorDe(projeto).SetKeepMany("yst00001", ["yst00001:000001", "yst00001:000002", "yst00001:000003"], keep: true);

        Assert.Equal(["yst00001:000001", "yst00001:000003"], mudancas.Select(c => c.Saved.Id));
        Assert.All(projeto.LoadText("yst00001"), u => Assert.Equal(TranslationStatus.Keep, u.Status));
    }

    [Fact]
    public void SetKeepMany_encerra_a_duvida_das_linhas_que_ficam_como_no_original()
    {
        using var pasta = new TempFolder();
        var duvida = new TranslationDoubt(DoubtKind.Pun, "Trocadilho.");
        var projeto = NovoProjeto(pasta.Path, Linha(1, TranslationStatus.Machine, "x") with { Doubt = duvida }, Linha(2, TranslationStatus.Machine, "y") with { Doubt = duvida });

        EditorDe(projeto).SetKeepMany("yst00001", ["yst00001:000001"], keep: true);

        var linhas = projeto.LoadText("yst00001");
        Assert.Null(linhas[0].Doubt);
        Assert.Equal(duvida, linhas[1].Doubt);
    }

    [Fact]
    public void SetKeep_com_id_inexistente_falha_em_portugues()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path, Linha(1, TranslationStatus.Untranslated));

        Assert.Throws<KeyNotFoundException>(() => EditorDe(projeto).SetKeep("yst00001", "yst00001:009999", keep: true));
    }

    [Fact]
    public void Traducao_vazia_numa_linha_keep_nao_tira_o_keep_e_texto_novo_traduz_a_linha()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path, Linha(1, TranslationStatus.Keep));
        var editor = EditorDe(projeto);

        Assert.Equal(TranslationStatus.Keep, editor.SetTranslation("yst00001", "yst00001:000001", "  ").Status);
        Assert.Equal(TranslationStatus.Translated, editor.SetTranslation("yst00001", "yst00001:000001", "MS Gothic").Status);
    }
}
