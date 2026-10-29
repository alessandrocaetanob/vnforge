using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Projects;
using VNForge.Core.Review;
using VNForge.Core.Search;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>A tradução que a IA escreveu fica guardada em <see cref="TextUnit.AiTranslation"/> quando a pessoa a edita (#473).</summary>
public sealed class AiTranslationTrailTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _clock = new(Agora);
    private readonly ProjectFolder _project;

    public AiTranslationTrailTests()
    {
        _project = ProjectFolder.Create(Path.Combine(_temp.Path, "projeto"), new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private static TextUnit DaIa(int n, string traducao) =>
        new($"a:{n:D6}", TextUnitKind.Dialogue, "健太", $"原文{n}", traducao, TranslationStatus.Machine)
        {
            Origin = new TranslationOrigin("anthropic", "claude-opus-5-5", Agora.AddDays(-1)),
        };

    private TextUnit Gravada(int n) => _project.LoadText("a").Single(u => u.Id == $"a:{n:D6}");

    private LineEditor Editor() => new(_project, _clock);

    [Fact]
    public void Json_sem_o_campo_abre_e_o_campo_nulo_nao_entra_no_arquivo()
    {
        var json = TextUnitJson.Serialize(DaIa(1, "Ei, vem."));

        Assert.DoesNotContain("aiTranslation", json);
        Assert.Null(TextUnitJson.Deserialize(json).AiTranslation);
    }

    [Fact]
    public void Json_do_campo_faz_ida_e_volta_e_o_projeto_regrava_igual()
    {
        var unidade = DaIa(1, "Ô, vem.") with { AiTranslation = "Ei, vem." };

        var json = TextUnitJson.Serialize(unidade);

        Assert.Contains("\"aiTranslation\":\"Ei, vem.\"", json, StringComparison.Ordinal);
        Assert.Equal("Ei, vem.", TextUnitJson.Deserialize(json).AiTranslation);
        _project.SaveText("a", [unidade]);
        Assert.Equal("Ei, vem.", Gravada(1).AiTranslation);
        Assert.Equal(TextUnitJson.SerializeList([unidade]), TextUnitJson.SerializeList(_project.LoadText("a")));
    }

    [Fact]
    public void SetTranslation_guarda_o_texto_da_IA_na_primeira_edicao_e_o_mantem_nas_seguintes()
    {
        _project.SaveText("a", [DaIa(1, "Ei, vem.")]);

        var primeira = Editor().SetTranslation("a", "a:000001", "Ô, vem.");
        var segunda = Editor().SetTranslation("a", "a:000001", "Ô, vem logo.");

        Assert.Equal("Ei, vem.", primeira.AiTranslation);
        Assert.Equal("Ei, vem.", segunda.AiTranslation);
        Assert.Equal("Ei, vem.", Gravada(1).AiTranslation);
    }

    [Fact]
    public void SetTranslation_com_texto_igual_ao_gravado_nao_apaga_nem_cria_o_campo()
    {
        _project.SaveText("a", [DaIa(1, "Ei, vem."), DaIa(2, "Oi.")]);
        Editor().SetTranslation("a", "a:000001", "Ô, vem.");

        Editor().SetTranslation("a", "a:000001", "Ô, vem.");
        Editor().SetTranslation("a", "a:000002", "Oi.");

        Assert.Equal("Ei, vem.", Gravada(1).AiTranslation);
        Assert.Null(Gravada(2).AiTranslation);
    }

    [Fact]
    public void SetTranslation_que_devolve_o_texto_da_IA_ou_limpa_a_linha_apaga_o_campo()
    {
        _project.SaveText("a", [DaIa(1, "Ei, vem."), DaIa(2, "Oi.")]);
        Editor().SetTranslation("a", "a:000001", "Ô, vem.");
        Editor().SetTranslation("a", "a:000002", "Alô.");

        Editor().SetTranslation("a", "a:000001", "Ei, vem.");
        Editor().SetTranslation("a", "a:000002", "  ");

        Assert.Null(Gravada(1).AiTranslation);
        Assert.Null(Gravada(2).AiTranslation);
    }

    [Fact]
    public void Linha_que_nunca_foi_da_IA_nao_guarda_nada()
    {
        var manual = DaIa(1, "Ei.") with { Origin = new TranslationOrigin("manual", null, Agora), Status = TranslationStatus.Translated };
        var semOrigem = DaIa(2, "Ei.") with { Origin = null };
        _project.SaveText("a", [manual, semOrigem]);

        Editor().SetTranslation("a", "a:000001", "Ô.");
        Editor().SetTranslation("a", "a:000002", "Ô.");

        Assert.Null(Gravada(1).AiTranslation);
        Assert.Null(Gravada(2).AiTranslation);
    }

    [Fact]
    public void SetTranslationFor_e_Keep_seguem_a_mesma_regra()
    {
        _project.SaveText("a", [DaIa(1, "Ei, vem."), DaIa(2, "Ei, anda.")]);

        Editor().SetTranslationFor([("a", "a:000001")], "Ô, vem.");
        Editor().SetKeep("a", "a:000002", true);

        Assert.Equal("Ei, vem.", Gravada(1).AiTranslation);
        Assert.Null(Gravada(2).AiTranslation);
    }

    [Fact]
    public void Restore_devolve_o_campo_de_antes()
    {
        var antes = DaIa(1, "Ei, vem.");
        _project.SaveText("a", [antes]);
        var gravada = Editor().SetTranslation("a", antes.Id, "Ô, vem.");

        Editor().Restore("a", antes, gravada);

        Assert.Null(Gravada(1).AiTranslation);
        Assert.Equal("Ei, vem.", Gravada(1).Translation);
    }

    [Fact]
    public void Aprovar_nao_mexe_no_campo()
    {
        _project.SaveText("a", [DaIa(1, "Ei, vem.")]);
        Editor().SetTranslation("a", "a:000001", "Ô, vem.");

        Editor().Approve("a", "a:000001");

        Assert.Equal("Ei, vem.", Gravada(1).AiTranslation);
    }

    [Fact]
    public void Substituir_em_lote_guarda_o_texto_da_IA()
    {
        _project.SaveText("a", [DaIa(1, "Ei, Misaki.")]);
        var previa = ProjectSearch.PreviewReplace(_project.LoadAllText(), new SearchOptions("Misaki"), "Aoi", TestContext.Current.CancellationToken);

        ProjectSearch.Apply(_project, previa, _clock);

        Assert.Equal("Ei, Aoi.", Gravada(1).Translation);
        Assert.Equal("Ei, Misaki.", Gravada(1).AiTranslation);
    }

    [Fact]
    public void Importar_revisao_guarda_o_texto_da_IA_so_quando_a_traducao_muda()
    {
        _project.SaveText("a", [DaIa(1, "Ei vem."), DaIa(2, "Ei anda.")]);
        var csv = Path.Combine(_temp.Path, "revisao.csv");
        ReviewExporter.Export(_project, ReviewFormats.Csv, csv);
        File.WriteAllText(csv, File.ReadAllText(csv).Replace("Ei vem.", "Ô vem.", StringComparison.Ordinal).Replace("Ei anda.,machine,", "Ei anda.,approved,", StringComparison.Ordinal));

        ReviewImporter.Import(_project, csv);

        Assert.Equal("Ô vem.", Gravada(1).Translation);
        Assert.Equal("Ei vem.", Gravada(1).AiTranslation);
        Assert.Equal(TranslationStatus.Approved, Gravada(2).Status);
        Assert.Null(Gravada(2).AiTranslation);
    }

    [Fact]
    public void Importar_revisao_so_com_nota_nova_mantem_o_campo_que_ja_havia()
    {
        _project.SaveText("a", [DaIa(1, "Ô, vem.") with { AiTranslation = "Ei, vem." }]);
        var csv = Path.Combine(_temp.Path, "revisao.csv");
        ReviewExporter.Export(_project, ReviewFormats.Csv, csv);
        File.WriteAllText(csv, File.ReadAllText(csv).Replace(",machine,", ",machine,conferir", StringComparison.Ordinal));

        ReviewImporter.Import(_project, csv);

        Assert.Equal("conferir", Gravada(1).Notes);
        Assert.Equal("Ei, vem.", Gravada(1).AiTranslation);
    }

    [Fact]
    public void Desfazer_um_lote_de_edicao_devolve_o_campo_de_antes()
    {
        _project.SaveText("a", [DaIa(1, "Ei, vem."), DaIa(2, "Ei, anda.") with { AiTranslation = "Hey, anda." }]);
        var store = new RestorePointStore(_project, _clock);

        var feito = store.Record("Usar em todos", () => Editor().SetTranslationFor([("a", "a:000001"), ("a", "a:000002")], "Ô."));
        Assert.Equal("Ei, vem.", Gravada(1).AiTranslation);

        var desfeito = store.Undo(feito.Point!.Id);

        Assert.Equal(new RestoreUndoResult(2, 0, 0), desfeito);
        Assert.Null(Gravada(1).AiTranslation);
        Assert.Equal("Hey, anda.", Gravada(2).AiTranslation);
    }

    [Fact]
    public void Desfazer_nao_reverte_a_linha_cujo_campo_mudou_depois_do_lote()
    {
        _project.SaveText("a", [DaIa(1, "Ei, vem.")]);
        var store = new RestorePointStore(_project, _clock);
        var feito = store.Record("Usar em todos", () => Editor().SetTranslationFor([("a", "a:000001")], "Ô, vem."));
        _project.SaveText("a", [Gravada(1) with { AiTranslation = "Outra" }]);

        var desfeito = store.Undo(feito.Point!.Id);

        Assert.Equal(1, desfeito.EditedSince);
        Assert.Equal("Outra", Gravada(1).AiTranslation);
    }

    [Fact]
    public void Desfazer_a_traducao_da_IA_devolve_a_linha_editada_com_o_texto_da_IA_guardado()
    {
        _project.SaveText("a", [new TextUnit("a:000001", TextUnitKind.Dialogue, "健太", "原文1", "Ô, vem.", TranslationStatus.Translated) { AiTranslation = "Ei, vem." }]);
        var store = new RestorePointStore(_project, _clock);

        var feito = store.Record(
            "Traduzindo o jogo",
            () =>
            {
                var unidades = _project.LoadText("a").ToList();
                unidades[0] = unidades[0] with { Translation = "Nova da IA", Status = TranslationStatus.Machine, Origin = new TranslationOrigin("anthropic", "claude-opus-5-5", Agora), AiTranslation = null };
                _project.SaveText("a", unidades);
                return 0;
            },
            include: RestorePointStore.IsAiWrite);
        Assert.Null(Gravada(1).AiTranslation);

        store.Undo(feito.Point!.Id);

        Assert.Equal("Ô, vem.", Gravada(1).Translation);
        Assert.Equal("Ei, vem.", Gravada(1).AiTranslation);
    }
}
