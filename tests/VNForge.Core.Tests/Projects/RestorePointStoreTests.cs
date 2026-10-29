using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Search;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public sealed class RestorePointStoreTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _clock = new(Agora);
    private readonly ProjectFolder _project;
    private readonly RestorePointStore _store;

    public RestorePointStoreTests()
    {
        _project = ProjectFolder.Create(Path.Combine(_temp.Path, "projeto"), new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        _store = new RestorePointStore(_project, _clock);
    }

    public void Dispose() => _temp.Dispose();

    private static TextUnit Linha(string script, int n, string? traducao, TranslationStatus status = TranslationStatus.Translated, string original = "同じ") =>
        new($"{script}:{n:D6}", TextUnitKind.Dialogue, null, original, traducao, status)
        {
            Origin = traducao is null ? null : new TranslationOrigin("anthropic", "claude-opus-5-5", Agora.AddDays(-1)),
            Notes = $"nota {n}",
        };

    private static TextUnit Pendente(string script, int n) => Linha(script, n, null, TranslationStatus.Untranslated);

    private TextUnit Gravada(string script, int n) => _project.LoadText(script).Single(u => u.Id == $"{script}:{n:D6}");

    private LineEditor Editor() => new(_project, _clock);

    [Fact]
    public void Desfazer_um_lote_nao_apaga_a_marca_soa_estranho_posta_antes_nem_depois()
    {
        _project.SaveText("a", [Linha("a", 1, "Oi").WithOdd(true), Linha("a", 2, "Olá")]);
        var alvos = new[] { ("a", "a:000001"), ("a", "a:000002") };

        var feito = _store.Record("Usar em todos", () => Editor().SetTranslationFor(alvos, "Oi, tudo bem?"));
        Editor().SetOdd("a", "a:000002", true);
        Assert.True(Gravada("a", 1).IsOdd());

        var desfeito = _store.Undo(feito.Point!.Id);

        Assert.Equal(2, desfeito.Restored);
        Assert.Equal(0, desfeito.EditedSince);
        Assert.Equal("Olá", Gravada("a", 2).Translation);
        Assert.Equal("Oi", Gravada("a", 1).Translation);
        Assert.True(Gravada("a", 1).IsOdd(), "a marca de antes do lote continua");
        Assert.True(Gravada("a", 2).IsOdd(), "a marca posta depois do lote continua");
    }

    [Fact]
    public void Usar_em_todos_e_desfeito_com_o_estado_de_antes_inclusive_a_origem()
    {
        _project.SaveText("a", [Linha("a", 1, "Oi"), Linha("a", 2, "Olá", TranslationStatus.Approved), Pendente("a", 3)]);
        _project.SaveText("b", [Linha("b", 1, "Alô")]);
        var alvos = new[] { ("a", "a:000001"), ("a", "a:000002"), ("a", "a:000003"), ("b", "b:000001") };

        var feito = _store.Record("Usar em todos", () => Editor().SetTranslationFor(alvos, "Oi, tudo bem?"));

        Assert.Equal(4, feito.Result.Changes.Count);
        Assert.Equal(4, feito.Point!.LineCount);
        Assert.Equal(["a", "b"], feito.Point.Scripts);
        Assert.Equal("Oi, tudo bem?", Gravada("a", 2).Translation);

        var desfeito = _store.Undo(feito.Point.Id);

        Assert.Equal(new RestoreUndoResult(4, 0, 0), desfeito);
        Assert.Equal(("Oi", TranslationStatus.Translated), (Gravada("a", 1).Translation, Gravada("a", 1).Status));
        Assert.Equal(("Olá", TranslationStatus.Approved), (Gravada("a", 2).Translation, Gravada("a", 2).Status));
        var vazia = Gravada("a", 3);
        Assert.Equal((null, TranslationStatus.Untranslated, null), (vazia.Translation, vazia.Status, vazia.Origin));
        Assert.Equal(new TranslationOrigin("anthropic", "claude-opus-5-5", Agora.AddDays(-1)), Gravada("a", 1).Origin);
        Assert.Equal("Alô", Gravada("b", 1).Translation);
        Assert.Null(_store.Find(feito.Point.Id));
    }

    [Fact]
    public void Substituicao_e_desfeita()
    {
        _project.SaveText("a", [Linha("a", 1, "Misaki chegou."), Linha("a", 2, "Nada.")]);
        _project.SaveText("b", [Linha("b", 1, "Oi, Misaki!")]);
        var previa = ProjectSearch.PreviewReplace(_project, new SearchOptions("Misaki"), "Aoi", TestContext.Current.CancellationToken);

        var feito = _store.Record("Substituir \"Misaki\" por \"Aoi\"", () => ProjectSearch.Apply(_project, previa, _clock));

        Assert.Equal(2, feito.Result.Applied);
        Assert.Equal(2, feito.Point!.LineCount);
        Assert.Equal("Aoi chegou.", Gravada("a", 1).Translation);

        var desfeito = _store.Undo(feito.Point.Id);

        Assert.Equal(new RestoreUndoResult(2, 0, 0), desfeito);
        Assert.Equal("Misaki chegou.", Gravada("a", 1).Translation);
        Assert.Equal("Oi, Misaki!", Gravada("b", 1).Translation);
        Assert.Equal("anthropic", Gravada("a", 1).Origin!.Provider);
    }

    [Fact]
    public void Corrigir_com_um_clique_e_desfeito()
    {
        _project.SaveText("a", [Linha("a", 1, "Olá,   mundo"), Linha("a", 2, "Tudo certo.")]);

        var feito = _store.Record("Corrigir com um clique", () => QaFixer.Apply(_project, QaEngine.Default));

        Assert.NotEmpty(feito.Result.Fixes);
        Assert.Equal(1, feito.Point!.LineCount);
        Assert.Equal("Olá, mundo", Gravada("a", 1).Translation);

        var desfeito = _store.Undo(feito.Point.Id);

        Assert.Equal(new RestoreUndoResult(1, 0, 0), desfeito);
        Assert.Equal("Olá,   mundo", Gravada("a", 1).Translation);
    }

    [Fact]
    public void Traducao_da_IA_so_guarda_as_linhas_da_IA_e_nao_a_edicao_feita_durante_o_lote()
    {
        _project.SaveText("a", [Pendente("a", 1), Pendente("a", 2), Pendente("a", 3)]);

        var feito = _store.Record(
            "Traduzindo o jogo",
            () =>
            {
                var ia = new TranslationOrigin("anthropic", "claude-opus-5-5", Agora);
                var unidades = _project.LoadText("a").ToList();
                unidades[0] = unidades[0] with { Translation = "Um", Status = TranslationStatus.Translated, Origin = ia };
                unidades[2] = unidades[2] with { Translation = "Três", Status = TranslationStatus.Translated, Origin = ia };
                _project.SaveText("a", unidades);
                Editor().SetTranslation("a", "a:000002", "Dois, à mão");
                return 0;
            },
            include: RestorePointStore.IsAiWrite);

        Assert.Equal(2, feito.Point!.LineCount);

        var desfeito = _store.Undo(feito.Point.Id);

        Assert.Equal(new RestoreUndoResult(2, 0, 0), desfeito);
        Assert.Equal(TranslationStatus.Untranslated, Gravada("a", 1).Status);
        Assert.Equal("Dois, à mão", Gravada("a", 2).Translation);
        Assert.Equal(TranslationStatus.Untranslated, Gravada("a", 3).Status);
    }

    [Fact]
    public void Linha_editada_depois_do_lote_fica_como_esta_e_o_desfazer_conta_quantas()
    {
        _project.SaveText("a", [Linha("a", 1, "Oi"), Linha("a", 2, "Olá"), Linha("a", 3, "Alô")]);
        var alvos = new[] { ("a", "a:000001"), ("a", "a:000002"), ("a", "a:000003") };
        var feito = _store.Record("Usar em todos", () => Editor().SetTranslationFor(alvos, "Igual"));
        Editor().SetTranslation("a", "a:000002", "Editada depois");
        Editor().SetNotes("a", "a:000003", "nota nova");

        var desfeito = _store.Undo(feito.Point!.Id);

        Assert.Equal(new RestoreUndoResult(2, 1, 0), desfeito);
        Assert.Equal("Oi", Gravada("a", 1).Translation);
        Assert.Equal("Editada depois", Gravada("a", 2).Translation);
        Assert.Equal("Alô", Gravada("a", 3).Translation);
        Assert.Equal("nota nova", Gravada("a", 3).Notes);
    }

    [Fact]
    public void Linha_que_nao_existe_mais_conta_como_ausente()
    {
        _project.SaveText("a", [Linha("a", 1, "Oi"), Linha("a", 2, "Olá")]);
        var feito = _store.Record("Usar em todos", () => Editor().SetTranslationFor([("a", "a:000001"), ("a", "a:000002")], "Igual"));
        _project.SaveText("a", [Gravada("a", 1)]);

        var desfeito = _store.Undo(feito.Point!.Id);

        Assert.Equal(new RestoreUndoResult(1, 0, 1), desfeito);
        Assert.Equal("Oi", Gravada("a", 1).Translation);
    }

    [Fact]
    public void Operacao_sem_mudanca_nao_deixa_ponto()
    {
        _project.SaveText("a", [Linha("a", 1, "Igual")]);

        var feito = _store.Record("Usar em todos", () => Editor().SetTranslationFor([("a", "a:000001")], "Igual"));

        Assert.Null(feito.Point);
        Assert.Empty(_store.List());
    }

    [Fact]
    public void Operacao_que_falha_no_meio_ainda_deixa_o_ponto_do_que_gravou()
    {
        _project.SaveText("a", [Linha("a", 1, "Oi")]);

        Assert.Throws<InvalidOperationException>(() => _store.Record<int>(
            "Lote que falha",
            () =>
            {
                Editor().SetTranslation("a", "a:000001", "Mudou");
                throw new InvalidOperationException("caiu");
            }));

        var ponto = Assert.Single(_store.List());
        Assert.Equal(1, ponto.LineCount);
        Assert.Equal(1, _store.Undo(ponto.Id).Restored);
        Assert.Equal("Oi", Gravada("a", 1).Translation);
    }

    [Fact]
    public void Ponto_sobrevive_a_outra_instancia_e_some_depois_de_desfeito()
    {
        _project.SaveText("a", [Linha("a", 1, "Oi")]);
        var feito = _store.Record("Usar em todos", () => Editor().SetTranslationFor([("a", "a:000001")], "Outra"));

        var outra = new RestorePointStore(ProjectFolder.Open(_project.Root), _clock);
        var listado = Assert.Single(outra.List());

        Assert.Equal((feito.Point!.Id, feito.Point.CreatedAt, feito.Point.LineCount), (listado.Id, listado.CreatedAt, listado.LineCount));
        Assert.Equal(feito.Point.Scripts, listado.Scripts);
        Assert.Equal("Usar em todos", listado.Title);
        outra.Undo(listado.Id);
        Assert.Empty(_store.List());
        var erro = Assert.Throws<VNForgeException>(() => _store.Undo(listado.Id));
        Assert.Contains("não existe mais", erro.Message);
    }

    [Fact]
    public void Lista_vem_do_mais_novo_para_o_mais_antigo()
    {
        _project.SaveText("a", [Linha("a", 1, "Oi")]);
        var primeiro = _store.Record("Primeiro", () => Editor().SetTranslationFor([("a", "a:000001")], "Um")).Point!;
        _clock.Advance(TimeSpan.FromMinutes(5));
        var segundo = _store.Record("Segundo", () => Editor().SetTranslationFor([("a", "a:000001")], "Dois")).Point!;

        Assert.Equal([segundo.Id, primeiro.Id], _store.List().Select(p => p.Id));
    }

    [Fact]
    public void Ponto_mais_velho_que_o_prazo_sai_quando_um_novo_e_gravado()
    {
        _project.SaveText("a", [Linha("a", 1, "Oi")]);
        var velho = _store.Record("Velho", () => Editor().SetTranslationFor([("a", "a:000001")], "Um")).Point!;
        _clock.Advance(RestorePointStore.Retention + TimeSpan.FromDays(1));

        var novo = _store.Record("Novo", () => Editor().SetTranslationFor([("a", "a:000001")], "Dois")).Point!;

        Assert.Equal([novo.Id], _store.List().Select(p => p.Id));
        Assert.Null(_store.Find(velho.Id));
    }

    [Fact]
    public void Pasta_sem_manifesto_nao_aparece_na_lista()
    {
        Directory.CreateDirectory(Path.Combine(_store.StoreDirectory, "20260101-000000-abcdef"));

        Assert.Empty(_store.List());
    }

    [Fact]
    public void Id_com_separador_de_pasta_e_recusado()
    {
        Assert.Throws<VNForgeException>(() => _store.Undo("..\\fora"));
    }

    [Fact]
    public void Desfazer_nao_regrava_o_script_quando_nenhuma_linha_volta()
    {
        _project.SaveText("a", [Linha("a", 1, "Oi")]);
        var feito = _store.Record("Usar em todos", () => Editor().SetTranslationFor([("a", "a:000001")], "Outra"));
        Editor().SetTranslation("a", "a:000001", "Editada");
        var antes = File.GetLastWriteTimeUtc(_project.TextPath("a"));

        var desfeito = _store.Undo(feito.Point!.Id);

        Assert.Equal(new RestoreUndoResult(0, 1, 0), desfeito);
        Assert.Equal(antes, File.GetLastWriteTimeUtc(_project.TextPath("a")));
        Assert.NotNull(_store.Find(feito.Point.Id));
    }

    [Fact]
    public void Aprovar_a_mao_durante_o_lote_da_IA_uma_linha_com_origem_da_IA_nao_entra_no_ponto()
    {
        _project.SaveText("a", [Pendente("a", 1), Linha("a", 2, "Dois")]);

        var feito = _store.Record(
            "Traduzindo o jogo",
            () =>
            {
                var unidades = _project.LoadText("a").ToList();
                unidades[0] = unidades[0] with { Translation = "Um", Status = TranslationStatus.Machine, Origin = new TranslationOrigin("anthropic", "claude-opus-5-5", Agora) };
                _project.SaveText("a", unidades);
                Editor().Approve("a", "a:000002");
                return 0;
            },
            include: RestorePointStore.IsAiWrite);

        Assert.Equal(1, feito.Point!.LineCount);
        var desfeito = _store.Undo(feito.Point.Id);

        Assert.Equal(new RestoreUndoResult(1, 0, 0), desfeito);
        Assert.Equal(TranslationStatus.Approved, Gravada("a", 2).Status);
    }

    [Fact]
    public void Reedicao_a_mao_com_o_mesmo_texto_e_status_nao_e_revertida()
    {
        _project.SaveText("a", [Linha("a", 1, "Oi")]);
        var feito = _store.Record("Usar em todos", () => Editor().SetTranslationFor([("a", "a:000001")], "Igual"));
        _clock.Advance(TimeSpan.FromMinutes(5));
        var linha = Gravada("a", 1);
        _project.SaveText("a", [linha with { Origin = new TranslationOrigin("manual", null, _clock.GetUtcNow()) }]);

        var desfeito = _store.Undo(feito.Point!.Id);

        Assert.Equal(new RestoreUndoResult(0, 1, 0), desfeito);
        Assert.Equal("Igual", Gravada("a", 1).Translation);
    }

    [Fact]
    public void Lote_curto_so_guarda_as_linhas_que_a_propria_operacao_devolve_e_ignora_notas()
    {
        _project.SaveText("a", [Linha("a", 1, "Oi"), Linha("a", 2, "Olá"), Linha("a", 3, "Alô")]);

        var feito = _store.Record(
            "Usar em todos",
            () =>
            {
                var resultado = Editor().SetTranslationFor([("a", "a:000001")], "Igual");
                Editor().SetTranslation("a", "a:000002", "Outra operação ao mesmo tempo");
                Editor().SetNotes("a", "a:000003", "nota nova");
                return resultado;
            },
            changedLines: r => r.Changes.Select(c => (c.ScriptKey, c.Saved.Id)));

        Assert.Equal(1, feito.Point!.LineCount);
        Assert.Equal(new RestoreUndoResult(1, 0, 0), _store.Undo(feito.Point.Id));
        Assert.Equal("Oi", Gravada("a", 1).Translation);
        Assert.Equal("Outra operação ao mesmo tempo", Gravada("a", 2).Translation);
    }

    [Fact]
    public void Mudar_so_as_notas_nao_gera_ponto()
    {
        _project.SaveText("a", [Linha("a", 1, "Oi")]);

        var feito = _store.Record("Notas", () => Editor().SetNotes("a", "a:000001", "nota nova"));

        Assert.Null(feito.Point);
    }

    [Fact]
    public void Ajuste_de_formatacao_trocado_pelo_lote_volta_no_Desfazer()
    {
        _project.SaveText("a", [Linha("a", 1, "Oi")]);
        _project.SaveFormatting(FormattingProfile.Default);
        var antes = File.ReadAllText(_project.FormattingPath);
        var outro = FormattingProfile.Default with { Wrap = new WrapRule(MaxWidth: 33) };

        var feito = _store.Record("Corrigir com um clique", () =>
        {
            _project.SaveFormatting(outro);
            return 0;
        }, trackFormatting: true);

        Assert.True(feito.Point!.Formatting);
        Assert.Equal(0, feito.Point.LineCount);
        Assert.NotEqual(antes, File.ReadAllText(_project.FormattingPath));

        var desfeito = _store.Undo(feito.Point.Id);

        Assert.True(desfeito.FormattingRestored);
        Assert.Equal(antes, File.ReadAllText(_project.FormattingPath));
        Assert.Null(_store.Find(feito.Point.Id));
    }

    [Fact]
    public void Traducao_da_IA_desfeita_devolve_a_duvida_de_antes_inclusive_nenhuma()
    {
        var antiga = new TranslationDoubt(DoubtKind.Tone, "Ironia ou carinho?");
        var nova = new TranslationDoubt(DoubtKind.Pun, "Trocadilho no original.");
        _project.SaveText("a", [Pendente("a", 1), Linha("a", 2, "Aham.") with { Doubt = antiga }, Linha("a", 3, "Certo.") with { Doubt = antiga }]);

        var feito = _store.Record(
            "Traduzindo o jogo",
            () =>
            {
                var ia = new TranslationOrigin("anthropic", "claude-opus-5-5", Agora);
                var unidades = _project.LoadText("a").ToList();
                unidades[0] = unidades[0] with { Translation = "Um", Status = TranslationStatus.Machine, Origin = ia, Doubt = nova };
                unidades[1] = unidades[1] with { Translation = "Dois", Status = TranslationStatus.Machine, Origin = ia, Doubt = nova };
                unidades[2] = unidades[2] with { Translation = "Três", Status = TranslationStatus.Machine, Origin = ia, Doubt = null };
                _project.SaveText("a", unidades);
                return 0;
            },
            include: RestorePointStore.IsAiWrite);

        Assert.Equal(nova, Gravada("a", 1).Doubt);

        var desfeito = _store.Undo(feito.Point!.Id);

        Assert.Equal(new RestoreUndoResult(3, 0, 0), desfeito);
        Assert.Null(Gravada("a", 1).Doubt);
        Assert.Equal(antiga, Gravada("a", 2).Doubt);
        Assert.Equal(antiga, Gravada("a", 3).Doubt);
    }

    [Fact]
    public void Ponto_gravado_antes_do_campo_doubt_existir_desfaz_sem_dar_duvida_a_linha()
    {
        var nova = new TranslationDoubt(DoubtKind.Pun, "Trocadilho no original.");
        _project.SaveText("a", [Pendente("a", 1)]);
        var feito = _store.Record(
            "Traduzindo o jogo",
            () =>
            {
                var ia = new TranslationOrigin("anthropic", "claude-opus-5-5", Agora);
                _project.SaveText("a", [.. _project.LoadText("a").Select(u => u with { Translation = "Um", Status = TranslationStatus.Machine, Origin = ia, Doubt = nova })]);
                return 0;
            },
            include: RestorePointStore.IsAiWrite);
        var antes = Path.Combine(_store.StoreDirectory, feito.Point!.Id, "a.antes.json");
        Assert.DoesNotContain("doubt", File.ReadAllText(antes), StringComparison.Ordinal);

        var desfeito = _store.Undo(feito.Point.Id);

        Assert.Equal(new RestoreUndoResult(1, 0, 0), desfeito);
        Assert.Null(Gravada("a", 1).Doubt);
        Assert.Equal(TranslationStatus.Untranslated, Gravada("a", 1).Status);
    }

    [Fact]
    public void Lote_que_nao_grava_duvida_devolve_a_que_a_linha_tinha()
    {
        var antiga = new TranslationDoubt(DoubtKind.Tone, "Ironia ou carinho?");
        _project.SaveText("a", [Linha("a", 1, "Oi") with { Doubt = antiga }]);

        var feito = _store.Record("Usar em todos", () => Editor().SetTranslationFor([("a", "a:000001")], "Oi, tudo bem?"));

        Assert.Null(Gravada("a", 1).Doubt);

        _store.Undo(feito.Point!.Id);

        Assert.Equal(antiga, Gravada("a", 1).Doubt);
    }

    [Fact]
    public void Ajuste_de_formatacao_mudado_depois_do_lote_fica_como_esta_e_o_ponto_continua()
    {
        _project.SaveText("a", [Linha("a", 1, "Oi")]);
        var feito = _store.Record("Corrigir com um clique", () =>
        {
            _project.SaveFormatting(FormattingProfile.Default with { Wrap = new WrapRule(MaxWidth: 33) });
            return 0;
        }, trackFormatting: true);
        var depois = FormattingProfile.Default with { Wrap = new WrapRule(MaxWidth: 44) };
        _project.SaveFormatting(depois);
        var atual = File.ReadAllText(_project.FormattingPath);

        var desfeito = _store.Undo(feito.Point!.Id);

        Assert.True(desfeito.FormattingKept);
        Assert.False(desfeito.FormattingRestored);
        Assert.Equal(atual, File.ReadAllText(_project.FormattingPath));
        Assert.NotNull(_store.Find(feito.Point.Id));
    }
}
