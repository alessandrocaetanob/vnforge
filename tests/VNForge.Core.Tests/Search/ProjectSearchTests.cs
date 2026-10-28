using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Projects;
using VNForge.Core.Search;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Search;

public class ProjectSearchTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static TextUnit Linha(string script, int n, string? traducao, TranslationStatus status = TranslationStatus.Translated, string? original = null) =>
        new($"{script}:{n:D6}", TextUnitKind.Dialogue, null, original ?? $"原文{n}", traducao, status)
        {
            Origin = traducao is null ? null : new TranslationOrigin("anthropic", "claude-opus-5-5", Agora.AddDays(-1)),
        };

    private static Dictionary<string, IReadOnlyList<TextUnit>> Textos(params (string Script, TextUnit[] Linhas)[] scripts) =>
        scripts.ToDictionary(s => s.Script, s => (IReadOnlyList<TextUnit>)s.Linhas);

    private static ProjectFolder NovoProjeto(string raiz) => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static SearchResult Achar(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> textos, SearchOptions opcoes) =>
        ProjectSearch.Find(textos, opcoes, TestContext.Current.CancellationToken);

    private static ReplacePreview Previa(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> textos, SearchOptions opcoes, string troca) =>
        ProjectSearch.PreviewReplace(textos, opcoes, troca, TestContext.Current.CancellationToken);

    private static ReplacePreview Previa(ProjectFolder projeto, SearchOptions opcoes, string troca) =>
        ProjectSearch.PreviewReplace(projeto, opcoes, troca, TestContext.Current.CancellationToken);

    [Fact]
    public void Busca_literal_acha_as_linhas_em_todos_os_scripts_na_ordem()
    {
        var textos = Textos(
            ("b", [Linha("b", 1, "Oi, Misaki!"), Linha("b", 2, "Nada aqui.")]),
            ("a", [Linha("a", 1, "Misaki chegou. Misaki sorriu.")]));

        var achados = Achar(textos, new SearchOptions("Misaki")).Hits;

        Assert.Equal([("a", 1), ("b", 1)], achados.Select(h => (h.ScriptKey, h.Number)));
        Assert.Equal([new TextRange(0, 6), new TextRange(15, 6)], achados[0].Matches);
    }

    [Fact]
    public void Busca_literal_trata_os_metacaracteres_como_texto()
    {
        var textos = Textos(("a", [Linha("a", 1, "Quanto? 3.5"), Linha("a", 2, "Quantos")]));

        var achados = Achar(textos, new SearchOptions("o? 3.5")).Hits;

        Assert.Equal(["a:000001"], achados.Select(h => h.UnitId));
    }

    [Fact]
    public void Maiusculas_so_contam_com_MatchCase()
    {
        var textos = Textos(("a", [Linha("a", 1, "misaki"), Linha("a", 2, "Misaki")]));

        Assert.Equal(2, Achar(textos, new SearchOptions("MISAKI")).Hits.Count);
        Assert.Equal(["a:000002"], Achar(textos, new SearchOptions("Misaki", MatchCase: true)).Hits.Select(h => h.UnitId));
    }

    [Fact]
    public void Palavra_inteira_nao_casa_dentro_de_outra_palavra()
    {
        var textos = Textos(("a", [Linha("a", 1, "Ela foi."), Linha("a", 2, "Aquela foi."), Linha("a", 3, "ela_x ele")]));

        var achados = Achar(textos, new SearchOptions("ela", WholeWord: true)).Hits;

        Assert.Equal(["a:000001"], achados.Select(h => h.UnitId));
    }

    [Fact]
    public void Palavra_inteira_cola_em_marcador_de_controle_como_separador()
    {
        var textos = Textos(("a", [Linha("a", 1, @"Oi\nela foi.\pEla {EF:01}ela"), Linha("a", 2, @"Oi\nelas")]));

        var achados = Achar(textos, new SearchOptions("ela", WholeWord: true)).Hits;

        Assert.Equal([new TextRange(4, 3), new TextRange(14, 3), new TextRange(25, 3)], Assert.Single(achados).Matches);
        Assert.Equal(["a:000001"], achados.Select(h => h.UnitId));
    }

    [Fact]
    public void Palavra_inteira_trata_o_acento_combinante_como_parte_da_palavra()
    {
        var textos = Textos(("a", [Linha("a", 1, "ela\u0301 e ela"), Linha("a", 2, "e\u0301ela")]));

        var achados = Achar(textos, new SearchOptions("ela", WholeWord: true)).Hits;

        Assert.Equal([new TextRange(7, 3)], Assert.Single(achados).Matches);
    }

    [Fact]
    public void Palavra_inteira_le_o_vizinho_fora_do_plano_basico_pelo_par_substituto()
    {
        var textos = Textos(("a", [Linha("a", 1, "😀ela"), Linha("a", 2, "𝐀ela")]));

        var achados = Achar(textos, new SearchOptions("ela", WholeWord: true)).Hits;

        Assert.Equal(["a:000001"], achados.Select(h => h.UnitId));
    }

    [Fact]
    public void Palavra_inteira_preserva_a_substituicao_ao_lado_de_marcador()
    {
        var textos = Textos(("a", [Linha("a", 1, @"Oi\nela foi.")]));

        var previa = Previa(textos, new SearchOptions("ela", WholeWord: true), "Aoi");

        Assert.Equal(@"Oi\nAoi foi.", Assert.Single(previa.Items).After);
    }

    [Fact]
    public void Regex_acha_por_padrao_e_ignora_ocorrencia_vazia()
    {
        var textos = Textos(("a", [Linha("a", 1, "Dia 12 e dia 7."), Linha("a", 2, "Sem número.")]));

        var achados = Achar(textos, new SearchOptions(@"\d+", UseRegex: true)).Hits;

        Assert.Equal([new TextRange(4, 2), new TextRange(13, 1)], Assert.Single(achados).Matches);
        Assert.Empty(Achar(textos, new SearchOptions("x*", UseRegex: true)).Hits);
    }

    [Fact]
    public void Padrao_que_so_casa_vazio_e_contado_na_busca_e_na_previa()
    {
        var textos = Textos(("a", [Linha("a", 1, "Um"), Linha("a", 2, "Dois"), Linha("a", 3, "Três 3")]));
        var opcoes = new SearchOptions("^", UseRegex: true);

        var busca = Achar(textos, opcoes);
        var previa = Previa(textos, opcoes, ">");

        Assert.Empty(busca.Hits);
        Assert.Equal(3, busca.EmptyMatchLines);
        Assert.True(previa.IsEmpty);
        Assert.Equal(3, previa.EmptyMatchLines);
        Assert.Equal(2, Achar(textos, new SearchOptions(@"\d*", UseRegex: true)).EmptyMatchLines);
    }

    [Fact]
    public void Busca_nao_conta_ocorrencia_dentro_de_marcador_como_a_previa_nao_conta()
    {
        var textos = Textos(("a", [Linha("a", 1, @"Oi\n x n"), Linha("a", 2, @"Só\p"), Linha("a", 3, "{EF:01} ef")]));

        var n = Achar(textos, new SearchOptions("n"));
        var p = Achar(textos, new SearchOptions("p"));
        var ef = Achar(textos, new SearchOptions("ef"));

        Assert.Equal([new TextRange(7, 1)], Assert.Single(n.Hits).Matches);
        Assert.Empty(p.Hits);
        Assert.Equal([new TextRange(8, 2)], Assert.Single(ef.Hits).Matches);
        Assert.Equal(n.Hits.Sum(h => h.Matches.Count), Previa(textos, new SearchOptions("n"), "N").TotalReplacements);
    }

    [Fact]
    public void Busca_na_traducao_ignora_linha_sem_traducao_utilizavel()
    {
        var textos = Textos(("a",
        [
            Linha("a", 1, null, TranslationStatus.Untranslated),
            new TextUnit("a:000002", TextUnitKind.Dialogue, null, "原文", "Misaki antiga", TranslationStatus.Untranslated) { PreviousSource = "旧" },
            Linha("a", 3, "Misaki"),
        ]));

        Assert.Equal(["a:000003"], Achar(textos, new SearchOptions("Misaki")).Hits.Select(h => h.UnitId));
    }

    [Fact]
    public void Busca_no_original_acha_mesmo_sem_traducao()
    {
        var textos = Textos(("a", [Linha("a", 1, null, TranslationStatus.Untranslated, original: "美咲が来た"), Linha("a", 2, "x", original: "別")]));

        var achados = Achar(textos, new SearchOptions("美咲", Field: SearchField.Source)).Hits;

        Assert.Equal(["a:000001"], achados.Select(h => h.UnitId));
    }

    [Fact]
    public void Texto_vazio_e_regex_invalida_viram_SearchException_em_portugues()
    {
        var textos = Textos(("a", [Linha("a", 1, "x")]));

        Assert.Contains("Digite", Assert.Throws<SearchException>(() => Achar(textos, new SearchOptions(""))).Message);
        Assert.Contains("inválida", Assert.Throws<SearchException>(() => Achar(textos, new SearchOptions("(", UseRegex: true))).Message);
    }

    [Fact]
    public void Regex_catastrofica_estoura_o_tempo_limite_em_vez_de_travar()
    {
        var textos = Textos(("a", [Linha("a", 1, new string('a', 64) + "!")]));
        var opcoes = new SearchOptions("(a+)+$", UseRegex: true) { MatchTimeout = TimeSpan.FromMilliseconds(50) };

        var erro = Assert.Throws<SearchException>(() => Achar(textos, opcoes));

        Assert.Contains("demorou demais", erro.Message);
        Assert.Throws<SearchException>(() => Previa(textos, opcoes, "x"));
    }

    [Fact]
    public void Token_cancelado_interrompe_a_busca_e_a_previa()
    {
        var textos = Textos(("a", [Linha("a", 1, "x")]));
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();

        Assert.Throws<OperationCanceledException>(() => ProjectSearch.Find(textos, new SearchOptions("x"), cancelamento.Token));
        Assert.Throws<OperationCanceledException>(() => ProjectSearch.PreviewReplace(textos, new SearchOptions("x"), "y", cancelamento.Token));
    }

    [Fact]
    public void Previa_lista_cada_linha_alterada_sem_gravar()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        projeto.SaveText("a", [Linha("a", 1, "Misaki chegou."), Linha("a", 2, "Nada."), Linha("a", 3, "Oi, Misaki e Misaki!")]);
        var antes = File.ReadAllText(projeto.TextPath("a"));

        var previa = Previa(projeto, new SearchOptions("Misaki"), "Aoi");

        Assert.Equal(
            [("a", 1, "Misaki chegou.", "Aoi chegou.", 1), ("a", 3, "Oi, Misaki e Misaki!", "Oi, Aoi e Aoi!", 2)],
            previa.Items.Select(i => (i.ScriptKey, i.Number, i.Before, i.After, i.Count)));
        Assert.Equal(3, previa.TotalReplacements);
        Assert.Equal(antes, File.ReadAllText(projeto.TextPath("a")));
    }

    [Fact]
    public void Previa_conta_as_linhas_aprovadas_e_revisadas_que_perdem_o_status()
    {
        var textos = Textos(("a",
        [
            Linha("a", 1, "x", TranslationStatus.Approved),
            Linha("a", 2, "x", TranslationStatus.Approved),
            Linha("a", 3, "x", TranslationStatus.Reviewed),
            Linha("a", 4, "x", TranslationStatus.Machine),
            Linha("a", 5, "y", TranslationStatus.Approved),
        ]));

        var previa = Previa(textos, new SearchOptions("x"), "z");

        Assert.Equal(2, previa.ApprovedLines);
        Assert.Equal(1, previa.ReviewedLines);
        Assert.Equal(4, previa.Items.Count);
    }

    [Fact]
    public void Regex_expande_grupos_na_substituicao_e_o_modo_literal_nao()
    {
        var textos = Textos(("a", [Linha("a", 1, "Dia 12.")]));

        var regex = Previa(textos, new SearchOptions(@"Dia (\d+)", UseRegex: true), "Dia #$1");
        var literal = Previa(textos, new SearchOptions("Dia 12"), "Dia #$1");

        Assert.Equal("Dia #12.", Assert.Single(regex.Items).After);
        Assert.Equal("Dia #$1.", Assert.Single(literal.Items).After);
    }

    [Fact]
    public void Regex_aceita_grupo_nomeado_e_cifrao_duplo_na_substituicao()
    {
        var textos = Textos(("a", [Linha("a", 1, "Dia 12.")]));

        var previa = Previa(textos, new SearchOptions(@"(?<n>\d+)", UseRegex: true), "[${n}] $$");

        Assert.Equal("Dia [12] $.", Assert.Single(previa.Items).After);
    }

    [Fact]
    public void Substituicao_nao_toca_o_marcador_de_controle()
    {
        var textos = Textos(("a", [Linha("a", 1, @"Olá n\nn \p"), Linha("a", 2, @"Só \p aqui")]));

        var previa = Previa(textos, new SearchOptions("n", UseRegex: true), "N");

        Assert.Equal(@"Olá N\nN \p", Assert.Single(previa.Items, i => i.Number == 1).After);
        Assert.Equal(0, previa.SkippedLines);
        var marcadores = Previa(textos, new SearchOptions(@"\\p", UseRegex: true), "X");
        Assert.True(marcadores.IsEmpty);
        Assert.Equal(2, marcadores.ProtectedMatches);
    }

    [Fact]
    public void Ocorrencia_que_atravessa_um_marcador_fica_de_fora()
    {
        var textos = Textos(("a", [Linha("a", 1, @"Oi\nmundo e Oi mundo")]));

        var previa = Previa(textos, new SearchOptions(@"Oi.nmundo", UseRegex: true), "Tchau");
        var simples = Previa(textos, new SearchOptions(@"Oi mundo"), "Tchau");

        Assert.True(previa.IsEmpty);
        Assert.Equal(1, previa.ProtectedMatches);
        Assert.Equal(@"Oi\nmundo e Tchau", Assert.Single(simples.Items).After);
    }

    [Fact]
    public void Marcador_chave_e_barra_dupla_tambem_sao_protegidos()
    {
        var textos = Textos(("a", [Linha("a", 1, @"{EF:01}EF e \\ EF")]));

        var previa = Previa(textos, new SearchOptions("EF"), "zz");

        Assert.Equal(@"{EF:01}zz e \\ zz", Assert.Single(previa.Items).After);
        Assert.Equal(1, previa.ProtectedMatches);
    }

    [Fact]
    public void Troca_que_quebraria_a_sintaxe_do_marcador_deixa_a_linha_como_esta_e_diz_qual()
    {
        var textos = Textos(("a", [Linha("a", 1, @"x\n y"), Linha("a", 2, "x ok")]));

        var barra = Previa(textos, new SearchOptions("x"), @"\");
        var chave = Previa(textos, new SearchOptions("x"), "{aberta");
        var valido = Previa(textos, new SearchOptions("x"), @"\p");

        Assert.Empty(barra.Items);
        Assert.Equal([new SkippedLine("a", 1, SkipReason.ReplacementBreaksMarkers), new SkippedLine("a", 2, SkipReason.ReplacementBreaksMarkers)], barra.Skipped);
        Assert.Equal(2, barra.BrokenByReplacementLines);
        Assert.Equal(0, barra.MalformedLines);
        Assert.Empty(chave.Items);
        Assert.Equal(@"\p ok", valido.Items.Single(i => i.Number == 2).After);
    }

    [Fact]
    public void Linha_com_marcador_malformado_nao_e_alterada_e_o_motivo_e_outro()
    {
        var textos = Textos(("a", [Linha("a", 1, @"abc \q abc"), Linha("a", 2, @"abc \q"), Linha("a", 3, @"sem \q aqui")]));

        var previa = Previa(textos, new SearchOptions("abc"), "x");

        Assert.True(previa.IsEmpty);
        Assert.Equal([new SkippedLine("a", 1, SkipReason.MalformedMarkers), new SkippedLine("a", 2, SkipReason.MalformedMarkers)], previa.Skipped);
        Assert.Equal(2, previa.MalformedLines);
        Assert.Equal(0, previa.BrokenByReplacementLines);
    }

    [Fact]
    public void Substituir_por_texto_igual_nao_gera_item()
    {
        var textos = Textos(("a", [Linha("a", 1, "Misaki")]));

        Assert.True(Previa(textos, new SearchOptions("Misaki"), "Misaki").IsEmpty);
    }

    [Fact]
    public void Previa_do_original_e_recusada()
    {
        var textos = Textos(("a", [Linha("a", 1, "x")]));

        Assert.Contains("original", Assert.Throws<SearchException>(() => Previa(textos, new SearchOptions("x", Field: SearchField.Source), "y")).Message);
    }

    [Fact]
    public void Aplicar_grava_so_as_linhas_da_previa_e_marca_a_edicao_como_manual()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        projeto.SaveText("a", [Linha("a", 1, "Misaki chegou.", TranslationStatus.Approved), Linha("a", 2, "Nada.", TranslationStatus.Approved)]);
        projeto.SaveText("b", [Linha("b", 1, "Oi, Misaki!")]);
        var previa = Previa(projeto, new SearchOptions("Misaki"), "Aoi");

        var resultado = ProjectSearch.Apply(projeto, previa, new FakeTimeProvider(Agora));

        Assert.Equal(2, resultado.Applied);
        Assert.Empty(resultado.Stale);
        var a = projeto.LoadText("a");
        Assert.Equal("Aoi chegou.", a[0].Translation);
        Assert.Equal(TranslationStatus.Translated, a[0].Status);
        Assert.Equal(new TranslationOrigin("manual", null, Agora), a[0].Origin);
        Assert.Equal(("Nada.", TranslationStatus.Approved), (a[1].Translation, a[1].Status));
        Assert.Equal("Oi, Aoi!", projeto.LoadText("b")[0].Translation);
    }

    [Fact]
    public void Aplicar_encerra_a_duvida_das_linhas_trocadas_e_mantem_a_das_outras()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        var duvida = new TranslationDoubt(DoubtKind.UncertainGender, "Quem fala?");
        projeto.SaveText("a", [Linha("a", 1, "Misaki chegou.", TranslationStatus.Machine) with { Doubt = duvida }, Linha("a", 2, "Nada.", TranslationStatus.Machine) with { Doubt = duvida }]);

        ProjectSearch.Apply(projeto, Previa(projeto, new SearchOptions("Misaki"), "Aoi"));

        var a = projeto.LoadText("a");
        Assert.Null(a[0].Doubt);
        Assert.Equal(duvida, a[1].Doubt);
    }

    [Fact]
    public void Aplicar_preserva_os_campos_da_linha_alem_da_traducao()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        var linha = Linha("a", 1, "Misaki") with { Speaker = "美咲" };
        linha = linha with { Notes = "nota", Flags = ["espaco-duplo"] };
        projeto.SaveText("a", [linha]);

        ProjectSearch.Apply(projeto, Previa(projeto, new SearchOptions("Misaki"), "Aoi"));

        var gravada = projeto.LoadText("a")[0];
        Assert.Equal("nota", gravada.Notes);
        Assert.Equal(["espaco-duplo"], gravada.Flags);
        Assert.Equal(("美咲", "原文1", TextUnitKind.Dialogue), (gravada.Speaker, gravada.Source, gravada.Kind));
    }

    [Fact]
    public void Aplicar_nao_grava_linha_editada_depois_da_previa()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        projeto.SaveText("a", [Linha("a", 1, "Misaki chegou."), Linha("a", 2, "Misaki saiu.")]);
        var previa = Previa(projeto, new SearchOptions("Misaki"), "Aoi");
        new LineEditor(projeto, TimeProvider.System).SetTranslation("a", "a:000001", "Editada à mão.");

        var resultado = ProjectSearch.Apply(projeto, previa);

        Assert.Equal(1, resultado.Applied);
        Assert.Equal(["a:000001"], resultado.Stale.Select(i => i.UnitId));
        var lidas = projeto.LoadText("a");
        Assert.Equal("Editada à mão.", lidas[0].Translation);
        Assert.Equal("Aoi saiu.", lidas[1].Translation);
    }

    [Fact]
    public void Aplicar_previa_vazia_nao_altera_o_arquivo()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        projeto.SaveText("a", [Linha("a", 1, "Misaki")]);
        var antes = File.ReadAllText(projeto.TextPath("a"));

        var resultado = ProjectSearch.Apply(projeto, Previa(projeto, new SearchOptions("nada"), "x"));

        Assert.Equal(0, resultado.Applied);
        Assert.Equal(antes, File.ReadAllText(projeto.TextPath("a")));
    }

    [Fact]
    public void Aplicar_que_falha_no_segundo_script_deixa_o_primeiro_gravado_e_lanca()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        projeto.SaveText("a", [Linha("a", 1, "Misaki")]);
        projeto.SaveText("b", [Linha("b", 1, "Misaki")]);
        var previa = Previa(projeto, new SearchOptions("Misaki"), "Aoi");
        File.Delete(projeto.TextPath("b"));

        Assert.Throws<TextFileException>(() => ProjectSearch.Apply(projeto, previa));

        Assert.Equal("Aoi", projeto.LoadText("a")[0].Translation);
    }
}
