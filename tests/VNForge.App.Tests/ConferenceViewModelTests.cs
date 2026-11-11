using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class ConferenceViewModelTests
{
    private static TextUnit Unit(string key, int n, string? translation, TranslationStatus status) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, null, $"原文{n}", translation, status);

    /// <summary>Dois scripts: espaço duplo (aviso, corrigível) em duas linhas, uma linha aprovada sem texto (erro) e uma pendente (aviso).</summary>
    private static (ConferenceViewModel Vm, ProjectFolder Folder) Open(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001",
        [
            Unit("yst00001", 0, "olá  mundo", TranslationStatus.Translated),
            Unit("yst00001", 1, "tudo certo", TranslationStatus.Translated),
            Unit("yst00001", 2, null, TranslationStatus.Approved),
            Unit("yst00001", 3, null, TranslationStatus.Untranslated),
        ]);
        folder.SaveText("yst00002", [Unit("yst00002", 5, "a  b  c", TranslationStatus.Reviewed)]);
        return (new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) }, folder);
    }

    private static async Task<(ConferenceViewModel Vm, ProjectFolder Folder)> Loaded(TempFolder temp)
    {
        var opened = Open(temp);
        await opened.Vm.RefreshAsync();
        return opened;
    }

    private static IEnumerable<ConferenceIssueRowViewModel> IssueRows(ConferenceViewModel vm) => vm.Rows.OfType<ConferenceIssueRowViewModel>();

    [Fact]
    public async Task Sem_projeto_a_lista_fica_vazia_e_nao_quebra()
    {
        var vm = new ConferenceViewModel();

        await vm.RefreshAsync();

        Assert.Empty(vm.Rows);
        Assert.False(vm.FixAllCommand.CanExecute(null));
        Assert.Equal("Abra um projeto para conferir as traduções.", vm.EmptyMessage);
    }

    private static async Task<(ConferenceViewModel Vm, ProjectFolder Folder)> LoadedCp1252(TempFolder temp)
    {
        var folder = ProjectFolder.Create(temp.SubFolder("real"), new ProjectConfig(
            "Real",
            new GameConfig("D:/Jogos/Real", "Real.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default));
        folder.SaveText("story04",
        [
            new TextUnit("story04:000064", TextUnitKind.Dialogue, null, "～朝～", "～Manhã～", TranslationStatus.Machine),
            new TextUnit("story04:000065", TextUnitKind.Dialogue, null, "原文", "Sem † solução", TranslationStatus.Translated),
        ]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(folder.Root) };
        await vm.RefreshAsync();
        return (vm, folder);
    }

    [Fact]
    public async Task Pontuacao_japonesa_fora_da_codificacao_ganha_correcao_de_um_clique_e_o_detalhe_diz_qual_caractere_e()
    {
        using var temp = new TempFolder();
        var (vm, _) = await LoadedCp1252(temp);

        var til = IssueRows(vm).Single(r => r.UnitId == "story04:000064");
        var estrela = IssueRows(vm).Single(r => r.UnitId == "story04:000065");

        Assert.True(til.HasProposal);
        Assert.Equal("~Manhã~", til.ProposedText);
        Assert.Equal("O ～ (til japonês, U+FF5E) não existe na acentuação do projeto. Use ~ no lugar do til japonês (～).", til.Detail);
        Assert.False(estrela.HasProposal);
        Assert.Equal("O † não existe na acentuação do projeto.", estrela.Detail);
    }

    [Fact]
    public async Task Corrigir_tudo_troca_a_pontuacao_japonesa_e_deixa_o_que_nao_tem_substituto()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await LoadedCp1252(temp);

        await vm.FixAllCommand.ExecuteAsync(null);

        Assert.Equal(["~Manhã~", "Sem † solução"], folder.LoadText("story04").Select(u => u.Translation));
        Assert.Equal(["story04:000065"], IssueRows(vm).Select(r => r.UnitId));
    }

    private static QaIssue Unencodable(int codePoint, string unitId = "yst00001:000001")
    {
        var text = char.ConvertFromUtf32(codePoint);
        return new QaIssue("caractere-nao-codificavel", QaSeverity.Error, unitId, "yst00001", $"o caractere '{text}' (U+{codePoint:X4}) não existe na estratégia 'cp1252'.");
    }

    [Fact]
    public async Task Releitura_externa_durante_uma_correcao_espera_ela_acabar_e_mostra_o_resultado()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);
        Assert.Contains(IssueRows(vm), r => r.RuleId == "espaco-duplo");

        var fix = vm.FixAllCommand.ExecuteAsync(null);
        var deferred = vm.RefreshAfterExternalChangeAsync();
        if (vm.IsFixing)
        {
            Assert.False(deferred.IsCompleted);
        }

        await fix;
        await deferred;

        Assert.False(vm.IsBusy);
        Assert.DoesNotContain(IssueRows(vm), r => r.RuleId == "espaco-duplo");
    }

    [Theory]
    [InlineData(0x200B)] // espaço de largura zero
    [InlineData(0x00A0)] // espaço sem quebra
    [InlineData(0x0007)] // controle
    [InlineData(0x3000)] // espaço ideográfico
    public void Caractere_invisivel_aparece_pelo_codigo(int codePoint)
    {
        var issue = Unencodable(codePoint);

        var row = new ConferenceIssueRowViewModel(issue);

        var nome = codePoint == 0x3000 ? " (espaço de largura dupla)" : string.Empty;
        var dica = codePoint == 0x3000 ? " Use um espaço comum no lugar do espaço de largura dupla (U+3000)." : string.Empty;
        Assert.Equal($"O U+{codePoint:X4}{nome} não existe na acentuação do projeto.{dica}", row.Detail);
    }

    [Fact]
    public async Task Pela_tela_a_linha_agrupada_mostra_todos_os_caracteres_do_grupo()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveConfig(folder.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        folder.SaveText("yst00001", [Unit("yst00001", 0, "Oi。 ok～ ​", TranslationStatus.Translated)]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };

        await vm.RefreshAsync();

        var row = Assert.Single(IssueRows(vm), r => r.RuleId == "caractere-nao-codificavel");
        Assert.Equal(3, row.Count);
        Assert.Equal("O 。 (ponto final japonês, U+3002), o ～ (til japonês, U+FF5E) e o U+200B não existem na acentuação do projeto. Use . no lugar do ponto final japonês (。), ~ no lugar do til japonês (～).", row.Detail);
        Assert.True(row.HasCharacterList);

        // O Painel e o filtro "Travam o teste" contam o mesmo: erros do QA, sem agrupar.
        vm.SelectFilterCommand.Execute(vm.Filters.Single(f => f.Kind == ConferenceFilterKind.Blocking));
        Assert.Equal(ProjectDashboard.Read(folder).Qa!.Errors, IssueRows(vm).Sum(r => r.Count));
    }

    [Fact]
    public async Task Esta_certo_assim_tira_o_aviso_da_lista_e_erro_nao_tem_o_botao()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);
        var warning = IssueRows(vm).First(r => r.RuleId == "espaco-duplo");

        Assert.True(vm.MarkOkCommand.CanExecute(warning));
        await vm.MarkOkCommand.ExecuteAsync(warning);

        Assert.DoesNotContain(IssueRows(vm), r => r.UnitId == warning.UnitId && r.RuleId == warning.RuleId);
        Assert.Equal("1 aviso marcado como certo.", vm.SilencedSummary);
        Assert.False(IssueRows(vm).Single(r => r.RuleId == "traducao-vazia").CanMarkOk);
    }

    [Fact]
    public async Task Cena_sem_nome_vira_Cena_N_pela_posicao_no_jogo_e_nunca_mostra_o_id()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);
        folder.SaveScriptNames(ScriptNames.From([new ScriptNameEntry("yst00001", null, "Manhã de chuva")]));
        await vm.RefreshAsync();

        var texts = IssueRows(vm).SelectMany(r => new[] { r.Where, r.DetailTitle }).ToList();

        Assert.Contains("Cena 2 · linha 1", texts);
        Assert.Contains("Manhã de chuva · linha 1", texts);
        Assert.All(texts, t => Assert.DoesNotContain("yst000", t, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Titulo_do_jogo_que_a_estrategia_nao_grava_aparece_como_erro_do_titulo_e_nao_como_cena()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);
        var config = folder.LoadConfig();
        folder.SaveConfig(config with
        {
            Encoding = new EncodingConfig("sjis"),
            Game = config.Game with { Title = new GameTitleConfig("原題", "Título") },
        });
        await vm.RefreshAsync();

        var row = IssueRows(vm).Single(r => r.RuleId == GameTitleEncodingRule.RuleId);

        Assert.True(row.IsError);
        Assert.False(row.CanMarkOk);
        Assert.Equal("Título do jogo", row.Where);
        Assert.Equal("Título do jogo fora da codificação", row.Title);
        Assert.Equal("Título do jogo", row.LineLabel);
    }

    [Fact]
    public void Excerpt_longo_nao_deixa_barra_solta_no_corte()
    {
        var before = new string('a', 59) + @"\p fim";
        var after = new string('a', 59) + @"\p  fim";

        var excerpt = ConferenceIssueRowViewModel.Excerpt(before, after);

        Assert.StartsWith("“" + new string('a', 59) + "…", excerpt);
        Assert.DoesNotContain('\\', excerpt);
        Assert.Equal(new string('a', 59) + "…”", excerpt[1..]);
    }

    [Fact]
    public void Excerpt_longo_com_marcador_inteiro_antes_do_corte_o_preserva()
    {
        var before = new string('a', 50) + @"\p" + new string('b', 20) + "  c";
        var after = new string('a', 50) + @"\p" + new string('b', 20) + " c";

        var excerpt = ConferenceIssueRowViewModel.Excerpt(before, after);

        Assert.Contains(@"\p", excerpt, StringComparison.Ordinal);
        Assert.EndsWith("…”", excerpt, StringComparison.Ordinal);
    }

    [Fact]
    public void Chave_sem_fechar_nao_vira_selo_e_cai_no_texto_por_extenso()
    {
        var issue = new QaIssue("espaco-antes-pontuacao", QaSeverity.Warning, "yst00001:000001", "yst00001", @"fala\p {nome sem fechar");
        var row = new ConferenceIssueRowViewModel(issue, labels: new ConferenceRowLabels(SceneName: "Cena 1"));

        Assert.Equal(@"Fala(virar a página) {nome sem fechar", row.DetailMarked);
    }

    [Fact]
    public async Task O_botao_de_destaque_do_cabecalho_e_corrigir_com_um_clique_e_sem_correcao_conferir_de_novo()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        Assert.True(vm.HasFixable);
        Assert.False(vm.RefreshIsPrimary);

        var root = SampleProjects.Create(temp.SubFolder("sem-correcao"), "Sem correção", TranslationStatus.Approved);
        ProjectFolder.Open(root).SaveText("yst00001", [Unit("yst00001", 0, null, TranslationStatus.Approved)]);
        var none = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };
        await none.RefreshAsync();

        Assert.False(none.HasFixable);
        Assert.True(none.RefreshIsPrimary);
    }

    [Fact]
    public async Task Marcar_como_ok_grava_o_flag_e_tira_o_aviso_da_lista()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        var changed = 0;
        vm.TextsChanged += (_, _) => changed++;
        var row = IssueRows(vm).First(r => r.UnitId == "yst00001:000000" && r.RuleId == "espaco-duplo");

        await vm.MarkOkCommand.ExecuteAsync(row);

        Assert.Contains("espaco-duplo", folder.LoadText("yst00001").Single(u => u.Id == "yst00001:000000").Flags);
        Assert.DoesNotContain(IssueRows(vm), r => r.UnitId == "yst00001:000000" && r.RuleId == "espaco-duplo");
        Assert.Equal(1, changed);
        Assert.Contains("1 aviso marcado como certo", vm.SilencedSummary);

        // O que a tela mostra continua igual ao vnf check depois da marca: as mesmas ocorrências, sem as silenciadas.
        var expected = QaEngine.Default.Run(QaContext.FromProject(folder));
        Assert.Equal(expected.Issues.Count, IssueRows(vm).Sum(r => r.Count));
        Assert.Single(expected.Silenced);
    }

    [Fact]
    public async Task Corrigir_tudo_roda_todos_os_fixers_e_avisa_quando_nao_ha_o_que_corrigir()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);

        await vm.FixAllCommand.ExecuteAsync(null);
        Assert.Equal("2 correções aplicadas.", vm.NoticeMessage);
        Assert.DoesNotContain(QaEngine.Default.Run(QaContext.FromProject(folder)).Issues, i => i.RuleId == "espaco-duplo");

        var changed = 0;
        vm.TextsChanged += (_, _) => changed++;
        await vm.FixAllCommand.ExecuteAsync(null);

        Assert.Equal("Nada a corrigir com um clique.", vm.NoticeMessage);
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Corrigir_respeita_o_ok_marcado_pelo_revisor()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        await vm.MarkOkCommand.ExecuteAsync(IssueRows(vm).First(r => r.UnitId == "yst00001:000000" && r.RuleId == "espaco-duplo"));

        await vm.FixAllCommand.ExecuteAsync(null);

        Assert.Equal("olá  mundo", folder.LoadText("yst00001")[0].Translation);
        Assert.Equal("a b c", folder.LoadText("yst00002")[0].Translation);
        Assert.Equal("1 correção aplicada.", vm.NoticeMessage);
    }

    [Fact]
    public async Task Projeto_ilegivel_mostra_o_erro_e_a_lista_fica_vazia()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);
        File.WriteAllText(folder.TextPath("yst00001"), "{ isto não é json");

        await vm.RefreshAsync();

        Assert.Empty(vm.Rows);
        Assert.StartsWith("Não foi possível conferir o projeto:", vm.ErrorMessage);
    }

    [Fact]
    public async Task Trocar_de_projeto_limpa_a_lista_anterior()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);
        Assert.NotEmpty(vm.Rows);

        vm.Project = null;

        Assert.Empty(vm.Rows);
        Assert.Equal("Abra um projeto para conferir as traduções.", vm.EmptyMessage);
    }

    [Fact]
    public async Task Falha_no_segundo_script_mantem_o_primeiro_corrigido_avisa_e_reconfere()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "O bloqueio de arquivo por FileShare só vale no Windows.");
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        var changed = 0;
        vm.TextsChanged += (_, _) => changed++;

        // Leitura compartilhada deixa o Core ler, mas impede o File.Move que regrava o segundo script.
        using (new FileStream(folder.TextPath("yst00002"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await vm.FixAllCommand.ExecuteAsync(null);
        }

        Assert.Equal("olá mundo", folder.LoadText("yst00001")[0].Translation);
        Assert.Equal("a  b  c", folder.LoadText("yst00002")[0].Translation);
        Assert.StartsWith("Não foi possível corrigir com um clique:", vm.ErrorMessage);
        Assert.EndsWith("continuam gravados.", vm.ErrorMessage);
        Assert.Null(vm.NoticeMessage);
        Assert.Equal(1, changed);
        Assert.False(vm.IsBusy);
        Assert.DoesNotContain(IssueRows(vm), r => r.UnitId == "yst00001:000000" && r.RuleId == "espaco-duplo");
        Assert.Contains(IssueRows(vm), r => r.UnitId == "yst00002:000005" && r.RuleId == "espaco-duplo");
    }

    [Fact]
    public async Task Marcar_como_ok_em_linha_que_sumiu_mostra_o_erro_e_mantem_o_aviso()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);
        var before = IssueRows(vm).Count();
        var missing = new ConferenceIssueRowViewModel(new QaIssue("espaco-duplo", QaSeverity.Warning, "yst00001:000099", "yst00001", "x"));

        await vm.MarkOkCommand.ExecuteAsync(missing);

        Assert.StartsWith("Não foi possível registrar o aviso como certo:", vm.ErrorMessage);
        Assert.EndsWith("O aviso continua na lista.", vm.ErrorMessage);
        Assert.Equal(before, IssueRows(vm).Count());
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task A_lista_poe_erros_primeiro_depois_por_script_e_linha_e_a_nao_traduzida_por_ultimo()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        Assert.Equal(
            [("traducao-vazia", "yst00001:000002"), ("espaco-duplo", "yst00001:000000"), ("espaco-duplo", "yst00002:000005"), ("nao-traduzida", "yst00001:000003")],
            IssueRows(vm).Select(r => (r.RuleId, r.UnitId)));
        Assert.Equal(["Erro", "Aviso", "Aviso", "Aviso"], IssueRows(vm).Select(r => r.SeverityLabel));
    }

    [Fact]
    public async Task Nao_traduzida_vira_uma_linha_por_script_com_a_contagem()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [Unit("yst00001", 0, null, TranslationStatus.Untranslated), Unit("yst00001", 1, null, TranslationStatus.Untranslated), Unit("yst00001", 2, "ok", TranslationStatus.Approved)]);
        folder.SaveText("yst00002", [Unit("yst00002", 0, null, TranslationStatus.Untranslated)]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };

        await vm.RefreshAsync();

        var rows = IssueRows(vm).ToList();
        Assert.Equal(["yst00001", "yst00002"], rows.Select(r => r.LineLabel));
        Assert.Equal([2, 1], rows.Select(r => r.Count));
        Assert.Equal("Não traduzida", rows[0].Title);
        Assert.Equal("2 linhas sem tradução nesta cena.", rows[0].Detail);
        Assert.Equal("1 linha sem tradução nesta cena.", rows[1].Detail);
        Assert.All(rows, r => Assert.False(r.CanMarkOk));
        Assert.Equal("3", vm.Filters[0].CountLabel);
        Assert.Equal("Mostrando 3 de 3 problemas abertos · agrupados por cena", vm.ListFooter);

        // Abrir no editor leva à primeira linha sem tradução do script.
        LineRequest? received = null;
        vm.LineRequested += (_, request) => received = request;
        vm.OpenLineCommand.Execute(rows[0]);
        Assert.Equal(new LineRequest("yst00001", "yst00001:000000"), received);
    }

    [Fact]
    public async Task Cada_problema_com_corretor_traz_a_correcao_proposta_e_o_resto_nao()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        var spaced = IssueRows(vm).Single(r => r.UnitId == "yst00001:000000" && r.RuleId == "espaco-duplo");
        Assert.True(spaced.HasProposal);
        Assert.Equal("olá mundo", spaced.ProposedText);
        Assert.Equal("Um clique", spaced.Tag);
        Assert.Equal("“olá  mundo”", spaced.Detail);
        Assert.Equal("olá  mundo", spaced.Translation);
        Assert.Equal("原文0", spaced.Original);
        Assert.Equal("Espaço duplo", spaced.Title);
        Assert.Equal("yst00001:0000", spaced.LineLabel);

        var empty = IssueRows(vm).Single(r => r.RuleId == "traducao-vazia");
        Assert.False(empty.HasProposal);
        Assert.Equal(string.Empty, empty.Tag);
        Assert.Equal("Tradução vazia", empty.Title);
        Assert.Equal("Tradução vazia (status approved)", empty.Detail);
        Assert.False(empty.HasTranslation);

        Assert.Equal("Corrigir 2 com um clique", vm.FixAllLabel);
    }

    [Fact]
    public async Task Aplicar_correcao_grava_so_a_linha_avisa_reconfere_e_passa_para_o_proximo()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        var changed = 0;
        vm.TextsChanged += (_, _) => changed++;
        var row = IssueRows(vm).Single(r => r.UnitId == "yst00001:000000" && r.RuleId == "espaco-duplo");
        vm.SelectedRow = row;

        await vm.ApplyFixCommand.ExecuteAsync(row);

        Assert.Equal("olá mundo", folder.LoadText("yst00001")[0].Translation);
        Assert.Equal("a  b  c", folder.LoadText("yst00002")[0].Translation);
        Assert.Equal("1 correção aplicada.", vm.NoticeMessage);
        Assert.Equal(1, changed);
        Assert.DoesNotContain(IssueRows(vm), r => r.UnitId == "yst00001:000000");
        Assert.Equal("Corrigir 1 com um clique", vm.FixAllLabel);
        Assert.NotNull(vm.SelectedRow);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Aplicar_correcao_em_problema_sem_proposta_ou_sem_alvo_nao_faz_nada()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        var changed = 0;
        vm.TextsChanged += (_, _) => changed++;
        var empty = IssueRows(vm).Single(r => r.RuleId == "traducao-vazia");

        Assert.False(vm.ApplyFixCommand.CanExecute(empty));
        Assert.False(vm.ApplyFixCommand.CanExecute(null));
        await vm.ApplyFixCommand.ExecuteAsync(empty);
        await vm.ApplyFixCommand.ExecuteAsync(null);
        await vm.MarkOkCommand.ExecuteAsync(null);
        vm.OpenLineCommand.Execute(null);
        vm.SelectFilterCommand.Execute(null);

        Assert.Equal(0, changed);
        Assert.Null(vm.NoticeMessage);
        Assert.Equal("olá  mundo", folder.LoadText("yst00001")[0].Translation);
        Assert.Equal(4, IssueRows(vm).Count());
    }

    [Fact]
    public async Task Clicar_no_chip_ja_marcado_mantem_o_filtro_e_o_estado_do_chip()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);
        var all = vm.Filters[0];
        var before = vm.Rows;

        all.IsSelected = false; // o ToggleButton desmarca o chip sozinho ao clique
        vm.SelectFilterCommand.Execute(all);

        Assert.True(all.IsSelected);
        Assert.Same(before, vm.Rows);
    }

    [Fact]
    public void Toda_regra_do_QA_tem_titulo_e_explicacao_e_categoria_conhecida()
    {
        foreach (var rule in QaEngine.Default.Rules)
        {
            Assert.True(ConferenceRuleTexts.Has(rule.Id), $"Falta o texto da regra {rule.Id}.");
            Assert.NotEqual(QaCategories.Other, QaCategories.Of(rule.Id));
        }

        Assert.Equal("regra-nova", ConferenceRuleTexts.Title("regra-nova"));
        Assert.Equal("Faz algo.", ConferenceRuleTexts.Explanation("regra-nova", "Faz algo."));
    }

    [Theory]
    [InlineData("yst00012:000042", "yst00012:0042")]
    [InlineData("yst00012:123456", "yst00012:123456")]
    [InlineData("sem-dois-pontos", "sem-dois-pontos")]
    [InlineData("yst:abc", "yst:abc")]
    public void A_linha_aparece_em_mono_com_quatro_digitos(string unitId, string expected)
    {
        Assert.Equal(expected, ConferenceIssueRowViewModel.FormatLine(unitId));
    }

    private sealed class ThrowingRule : IQaRule
    {
        public string Id => "regra-quebrada";

        public string Description => "Sempre lança.";

        public IEnumerable<QaIssue> Check(QaContext context) => throw new InvalidOperationException("regra quebrada.");
    }

    [Fact]
    public async Task Os_pedidos_da_pagina_avisam_quem_escuta_e_nao_falham_sem_ninguem()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);
        var pedido = new LineRequest("yst00001", "yst00001:000002");
        vm.TestCommand.Execute(null);
        vm.OpenLineByIdCommand.Execute(pedido);
        vm.OpenLineByIdCommand.Execute(null);
        var avisos = new List<string>();
        vm.TestRequested += (_, _) => avisos.Add("teste");
        vm.LineRequested += (_, request) => avisos.Add($"linha {request.UnitId}");

        vm.TestCommand.Execute(null);
        vm.OpenLineByIdCommand.Execute(pedido);
        vm.OpenLineByIdCommand.Execute(null);

        Assert.Equal(["teste", "linha yst00001:000002"], avisos);
    }
}
