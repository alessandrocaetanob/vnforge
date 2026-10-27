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

    [Fact]
    public async Task Filtro_de_erros_bate_com_vnf_check_severity_error()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);

        vm.SelectFilterCommand.Execute(vm.Filters.Single(f => f.Kind == ConferenceFilterKind.Blocking));

        var expected = QaEngine.Default.Run(QaContext.FromProject(folder), new QaOptions(null, QaSeverity.Error)).Issues;
        Assert.NotEmpty(expected);
        Assert.Equal(expected.Select(i => (i.RuleId, i.UnitId)), IssueRows(vm).Select(r => (r.RuleId, r.UnitId)));
        Assert.All(IssueRows(vm), r => Assert.True(r.IsError));
        Assert.Equal(expected.Count, IssueRows(vm).Sum(r => r.Count));
    }

    [Fact]
    public async Task Os_totais_dos_filtros_sao_do_projeto_inteiro_e_nao_mudam_com_o_filtro()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        Assert.Equal(["Todos", "Travam o teste", "Um clique", "Não cabe na caixa", "Palavras e nomes", "Acentos"], vm.Filters.Select(f => f.Label));
        Assert.Equal([5, 1, 2, 0, 0, 0], vm.Filters.Select(f => f.Count));
        Assert.Equal(4, IssueRows(vm).Count());

        vm.SelectFilterCommand.Execute(vm.Filters.Single(f => f.Kind == ConferenceFilterKind.OneClick));

        // Dois espaços duplos na mesma linha são um problema só, com a correção de um clique.
        Assert.Equal(2, IssueRows(vm).Count());
        Assert.All(IssueRows(vm), r => Assert.True(r.HasProposal));
        Assert.Equal([5, 1, 2, 0, 0, 0], vm.Filters.Select(f => f.Count));
        Assert.Equal([false, false, true, false, false, false], vm.Filters.Select(f => f.IsSelected));
        Assert.Equal("Mostrando 2 de 2 correções de um clique · agrupadas por cena", vm.ListFooter);
    }

    [Fact]
    public async Task Filtro_sem_problemas_explica_que_e_o_filtro()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        vm.SelectFilterCommand.Execute(vm.Filters.Single(f => f.Kind == ConferenceFilterKind.Accents));

        Assert.True(vm.IsEmpty);
        Assert.Null(vm.SelectedRow);
        Assert.Equal("Nenhum problema com este filtro.", vm.EmptyMessage);

        vm.SelectFilterCommand.Execute(vm.Filters[0]);
        Assert.Equal(4, IssueRows(vm).Count());
    }

    [Theory]
    [InlineData("glossario-diferente", ConferenceFilterKind.WordsAndNames)]
    [InlineData("personagem-diferente", ConferenceFilterKind.WordsAndNames)]
    [InlineData("traducao-inconsistente", ConferenceFilterKind.WordsAndNames)]
    [InlineData("caractere-nao-codificavel", ConferenceFilterKind.Accents)]
    [InlineData("caixa-acima-do-limite", ConferenceFilterKind.Box)]
    [InlineData("espaco-duplo", ConferenceFilterKind.All)]
    public void Cada_filtro_pega_as_regras_do_assunto_dele(string ruleId, ConferenceFilterKind expected)
    {
        var row = new ConferenceIssueRowViewModel(new QaIssue(ruleId, QaSeverity.Warning, "yst00001:000001", "yst00001", "x"));

        var passing = new[] { ConferenceFilterKind.WordsAndNames, ConferenceFilterKind.Accents, ConferenceFilterKind.Box }
            .Where(kind => new ConferenceFilterViewModel(kind, kind.ToString()).Passes(row));

        Assert.Equal(expected == ConferenceFilterKind.All ? [] : [expected], passing);
    }

    [Fact]
    public async Task Cada_problema_diz_a_cena_pelo_nome_e_a_linha()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);
        folder.SaveScriptNames(ScriptNames.From([new ScriptNameEntry("yst00001", null, "Manhã de chuva")]));
        await vm.RefreshAsync();

        var named = IssueRows(vm).Single(r => r.UnitId == "yst00001:000000" && r.RuleId == "espaco-duplo");
        var unnamed = IssueRows(vm).Single(r => r.UnitId == "yst00002:000005" && r.RuleId == "espaco-duplo");

        Assert.Equal("Manhã de chuva · linha 1", named.Where);
        Assert.Equal("Cena 2 · linha 1", unnamed.Where);
        Assert.Equal("yst00001:0000", named.LineLabel);
        Assert.Equal("Manhã de chuva", IssueRows(vm).Single(r => r.IsScriptSummary).Where);
        Assert.Equal("Não traduzida em Manhã de chuva", IssueRows(vm).Single(r => r.IsScriptSummary).DetailTitle);
    }

    [Fact]
    public async Task Marcador_que_falta_vira_frase_comum_sem_o_marcador_cru()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [new TextUnit("yst00001:000001", TextUnitKind.Dialogue, null, @"「行こう」\p", "Vamos", TranslationStatus.Reviewed)]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };

        await vm.RefreshAsync();

        var row = IssueRows(vm).Single(r => r.RuleId == "marcador-diferente");
        Assert.Equal("Falta um marcador do original", row.Title);
        Assert.Equal("Falta virar a página.", row.Detail);
        Assert.DoesNotContain("diferentes do original", row.Title, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\", row.Explanation, StringComparison.Ordinal);
        Assert.Equal(@"「行こう」\p", row.Original);
    }

    [Fact]
    public async Task Corrigir_e_ir_para_o_proximo_aplica_a_correcao_e_abre_o_que_ficou_no_lugar()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        vm.SelectedRow = IssueRows(vm).Single(r => r.UnitId == "yst00001:000000" && r.RuleId == "espaco-duplo");

        Assert.True(vm.FixAndNextCommand.CanExecute(null));
        await vm.FixAndNextCommand.ExecuteAsync(null);

        Assert.Equal("olá mundo", folder.LoadText("yst00001")[0].Translation);
        Assert.Equal("1 correção aplicada.", vm.NoticeMessage);
        Assert.Equal(("espaco-duplo", "yst00002:000005"), (vm.SelectedRow?.RuleId, vm.SelectedRow?.UnitId));
        Assert.Equal(vm.Rows[1], vm.SelectedRow);
    }

    [Fact]
    public async Task A_linha_e_a_posicao_no_script_como_no_editor_mesmo_com_ids_que_nao_sao_sequenciais()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00013", [Unit("yst00013", 0, "um", TranslationStatus.Translated), Unit("yst00013", 50, "dois", TranslationStatus.Translated), Unit("yst00013", 137, "três  vezes", TranslationStatus.Translated)]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };

        await vm.RefreshAsync();

        var row = IssueRows(vm).Single(r => r.RuleId == "espaco-duplo");
        var editor = ProjectLines.Read(folder).LinesOf("yst00013").Single(l => l.Unit.Id == row.UnitId);
        Assert.Equal(3, editor.Number);
        Assert.Equal(editor.Number, row.LineNumber);
        Assert.Equal("Cena 2 · linha 3", row.Where);
        Assert.Equal("yst00013:0137", row.LineLabel);
    }

    [Fact]
    public async Task Dentro_da_cena_a_lista_sai_em_ordem_de_linha_e_nao_em_ordem_de_texto_do_id()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        // Ids sem zeros à esquerda: em ordem de texto "10" e "100" viriam antes de "9".
        folder.SaveText("yst00013", new[] { "9", "10", "100" }.Select(n => new TextUnit($"yst00013:{n}", TextUnitKind.Dialogue, null, $"原文{n}", "dois  espaços", TranslationStatus.Translated)).ToList());
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };

        await vm.RefreshAsync();

        Assert.Equal([1, 2, 3], IssueRows(vm).Where(r => r.RuleId == "espaco-duplo").Select(r => r.LineNumber));
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
            new TextUnit("story04:000065", TextUnitKind.Dialogue, null, "原文", "Sem ★ solução", TranslationStatus.Translated),
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
        Assert.Equal("O ～ (til japonês, U+FF5E) não existe na acentuação do projeto. Use ~ no lugar do ～.", til.Detail);
        Assert.False(estrela.HasProposal);
        Assert.Equal("O ★ não existe na acentuação do projeto.", estrela.Detail);
    }

    [Fact]
    public async Task Corrigir_tudo_troca_a_pontuacao_japonesa_e_deixa_o_que_nao_tem_substituto()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await LoadedCp1252(temp);

        await vm.FixAllCommand.ExecuteAsync(null);

        Assert.Equal(["~Manhã~", "Sem ★ solução"], folder.LoadText("story04").Select(u => u.Translation));
        Assert.Equal(["story04:000065"], IssueRows(vm).Select(r => r.UnitId));
    }

    [Fact]
    public void Linha_que_veio_do_rascunho_da_IA_diz_isso_na_lista_e_no_detalhe()
    {
        var issue = new QaIssue("caractere-nao-codificavel", QaSeverity.Error, "yst00002:000049", "yst00002", "o caractere '。' (U+3002) não existe na estratégia 'cp1252'.");
        var unit = new TextUnit(issue.UnitId, TextUnitKind.Dialogue, null, "原文", "Oi。", TranslationStatus.Machine);

        var row = new ConferenceIssueRowViewModel(issue, unit: unit);

        Assert.True(row.IsMachineDraft);
        Assert.Equal("Rascunho da IA, ainda não revisado", row.OriginNote);
        Assert.Equal("Rascunho da IA, ainda não revisado", row.TranslationLabel);
    }

    [Theory]
    [InlineData(TranslationStatus.Translated)]
    [InlineData(TranslationStatus.Reviewed)]
    [InlineData(TranslationStatus.Approved)]
    public void Linha_de_gente_continua_com_o_rotulo_de_sempre(TranslationStatus status)
    {
        var issue = new QaIssue("espaco-duplo", QaSeverity.Warning, "yst00002:000049", "yst00002", "dois espaços");
        var unit = new TextUnit(issue.UnitId, TextUnitKind.Dialogue, null, "原文", "a  b", status);

        var row = new ConferenceIssueRowViewModel(issue, unit: unit);

        Assert.False(row.IsMachineDraft);
        Assert.Equal(string.Empty, row.OriginNote);
        Assert.Equal("Tradução de hoje", row.TranslationLabel);
    }

    [Fact]
    public void O_resumo_de_nao_traduzidas_nao_fala_de_rascunho()
    {
        var issue = new QaIssue(UntranslatedQaRule.RuleId, QaSeverity.Warning, "yst00002:000001", "yst00002", "sem tradução");
        var unit = new TextUnit(issue.UnitId, TextUnitKind.Dialogue, null, "原文", "x", TranslationStatus.Machine);

        var row = new ConferenceIssueRowViewModel(issue, unit: unit, count: 5);

        Assert.False(row.IsMachineDraft);
        Assert.Equal(string.Empty, row.OriginNote);
    }

    private static QaIssue Unencodable(int codePoint, string unitId = "yst00001:000001")
    {
        var text = char.ConvertFromUtf32(codePoint);
        return new QaIssue("caractere-nao-codificavel", QaSeverity.Error, unitId, "yst00001", $"o caractere '{text}' (U+{codePoint:X4}) não existe na estratégia 'cp1252'.");
    }

    [Fact]
    public void Linha_com_varios_caracteres_fora_da_acentuacao_lista_todos_e_nao_so_o_primeiro()
    {
        QaIssue[] issues = [Unencodable(0x3002), Unencodable(0xFF5E), Unencodable(0x2605)];

        var row = new ConferenceIssueRowViewModel(issues[0], count: 3, occurrences: issues);

        Assert.Equal("O 。 (ponto final japonês, U+3002), o ～ (til japonês, U+FF5E) e o ★ não existem na acentuação do projeto. Use . no lugar do 。, ~ no lugar do ～.", row.Detail);
        Assert.DoesNotContain("(+", row.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Frase_dos_caracteres_nao_muda_quando_a_linha_tem_correcao_proposta()
    {
        var issue = Unencodable(0x3002);
        var row = new ConferenceIssueRowViewModel(issue, new QaFix(issue.RuleId, issue.UnitId, issue.ScriptKey, "a。", "a."));

        Assert.Equal("O 。 (ponto final japonês, U+3002) não existe na acentuação do projeto. Use . no lugar do 。.", row.CharacterSentence);
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

    [Fact]
    public async Task Releitura_externa_com_leitura_em_curso_vale_a_mais_nova()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);
        var first = vm.RefreshAsync();
        folder.SaveText("yst00002", [Unit("yst00002", 5, "a b c", TranslationStatus.Reviewed)]);

        await vm.RefreshAfterExternalChangeAsync();
        await first;

        Assert.False(vm.IsBusy);
        Assert.All(IssueRows(vm).Where(r => r.ScriptKey == "yst00002"), r => Assert.NotEqual("espaco-duplo", r.RuleId));
    }

    [Fact]
    public void Dois_caracteres_fora_da_acentuacao_ligam_com_e()
    {
        QaIssue[] issues = [Unencodable(0x3002), Unencodable(0xFF5E)];

        var row = new ConferenceIssueRowViewModel(issues[0], count: 2, occurrences: issues);

        Assert.Equal("O 。 (ponto final japonês, U+3002) e o ～ (til japonês, U+FF5E) não existem na acentuação do projeto. Use . no lugar do 。, ~ no lugar do ～.", row.Detail);
    }

    [Fact]
    public void Caractere_repetido_na_linha_aparece_uma_vez_so()
    {
        QaIssue[] issues = [Unencodable(0x3002), Unencodable(0x3002)];

        var row = new ConferenceIssueRowViewModel(issues[0], count: 2, occurrences: issues);

        Assert.Equal("O 。 (ponto final japonês, U+3002) não existe na acentuação do projeto. Use . no lugar do 。.", row.Detail);
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
        var dica = codePoint == 0x3000 ? " Use um espaço comum no lugar do U+3000." : string.Empty;
        Assert.Equal($"O U+{codePoint:X4}{nome} não existe na acentuação do projeto.{dica}", row.Detail);
    }

    [Fact]
    public void Invisivel_junto_de_visivel_mistura_o_caractere_e_o_codigo()
    {
        QaIssue[] issues = [Unencodable(0x3002), Unencodable(0x200B)];

        var row = new ConferenceIssueRowViewModel(issues[0], count: 2, occurrences: issues);

        Assert.Equal("O 。 (ponto final japonês, U+3002) e o U+200B não existem na acentuação do projeto. Use . no lugar do 。.", row.Detail);
    }

    [Fact]
    public void Emoji_de_dois_chars_aparece_inteiro()
    {
        var issue = Unencodable(0x1F600);

        var row = new ConferenceIssueRowViewModel(issue);

        Assert.Equal("O 😀 não existe na acentuação do projeto.", row.Detail);
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
        Assert.Equal("O 。 (ponto final japonês, U+3002), o ～ (til japonês, U+FF5E) e o U+200B não existem na acentuação do projeto. Use . no lugar do 。, ~ no lugar do ～.", row.Detail);
        Assert.True(row.HasCharacterList);

        // O Painel e o filtro "Travam o teste" contam o mesmo: erros do QA, sem agrupar.
        vm.SelectFilterCommand.Execute(vm.Filters.Single(f => f.Kind == ConferenceFilterKind.Blocking));
        Assert.Equal(ProjectDashboard.Read(folder).Qa!.Errors, IssueRows(vm).Sum(r => r.Count));
    }

    [Fact]
    public void Caractere_fora_da_acentuacao_vira_frase_sem_o_detalhe_tecnico()
    {
        var issue = new QaIssue("caractere-nao-codificavel", QaSeverity.Error, "yst00001:000001", "yst00001", "o caractere '★' (U+2605) não existe na estratégia 'cp1252'.");

        var row = new ConferenceIssueRowViewModel(issue);

        Assert.Equal("O ★ não existe na acentuação do projeto.", row.Detail);
        Assert.DoesNotContain("cp1252", row.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Corrigir_e_ir_para_o_proximo_nao_faz_nada_em_problema_sem_correcao()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        var changed = 0;
        vm.TextsChanged += (_, _) => changed++;
        vm.SelectedRow = IssueRows(vm).Single(r => r.RuleId == "traducao-vazia");

        Assert.False(vm.FixAndNextCommand.CanExecute(null));
        await vm.FixAndNextCommand.ExecuteAsync(null);

        Assert.Equal(0, changed);
        Assert.Equal("olá  mundo", folder.LoadText("yst00001")[0].Translation);
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

    [Theory]
    [InlineData(@"“anda !\p”", "“anda !(virar a página)”")]
    [InlineData(@"o original '「了解」\p' aparece", "o original '「了解」(virar a página)' aparece")]
    [InlineData(@"fala\nseguinte {VAR:nome}", "fala(quebra de linha)seguinte (nome do jogador)")]
    [InlineData(@"a  \{b", "a  (chave)b")]
    [InlineData("sem marcador", "sem marcador")]
    public void A_lista_troca_o_marcador_cru_pelo_nome_em_portugues(string text, string expected)
    {
        Assert.Equal(expected, ConferenceIssueRowViewModel.PlainMarkers(text));
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
    public async Task Cena_N_e_a_mesma_com_qualquer_filtro()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);
        string[] unnamed() => [.. IssueRows(vm).Select(r => $"{r.UnitId}|{r.RuleId}|{r.Where}")];
        var all = unnamed();
        Assert.Contains("yst00002:000005|espaco-duplo|Cena 2 · linha 1", all);

        foreach (var kind in new[] { ConferenceFilterKind.OneClick, ConferenceFilterKind.Blocking })
        {
            vm.SelectFilterCommand.Execute(vm.Filters.Single(f => f.Kind == kind));
            Assert.NotEmpty(unnamed());
            Assert.All(unnamed(), line => Assert.Contains(line, all));
        }
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
        var row = new ConferenceIssueRowViewModel(issue, sceneName: "Cena 1");

        Assert.Equal(@"fala(virar a página) {nome sem fechar", row.DetailMarked);
    }

    [Fact]
    public void A_lista_guarda_o_marcador_cru_para_virar_selo_e_o_texto_por_extenso_para_a_dica()
    {
        var issue = new QaIssue("espaco-antes-pontuacao", QaSeverity.Warning, "yst00001:000001", "yst00001", @"“anda !\p”");
        var row = new ConferenceIssueRowViewModel(issue, sceneName: "Cena 1");

        Assert.Equal(@"“anda !\p”", row.DetailMarked);
        Assert.Equal("“anda !(virar a página)”", row.Detail);
        Assert.Equal(@"“anda !\p”", row.Message);
    }

    [Fact]
    public void Marcador_fora_da_gramatica_do_jogo_nao_vira_selo_e_cai_no_texto_por_extenso()
    {
        var issue = new QaIssue("espaco-antes-pontuacao", QaSeverity.Warning, "yst00001:000001", "yst00001", @"fala\p e \z");
        var row = new ConferenceIssueRowViewModel(issue, sceneName: "Cena 1");

        Assert.Equal(@"fala(virar a página) e \z", row.DetailMarked);
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
    public async Task Sem_erro_a_faixa_diz_que_nada_trava_o_teste()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        ProjectFolder.Open(root).SaveText("yst00001", [Unit("yst00001", 0, "olá  mundo", TranslationStatus.Translated)]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };

        await vm.RefreshAsync();

        Assert.False(vm.HasBlockingErrors);
        Assert.True(vm.HasOnlyWarnings);
        Assert.False(new ConferenceViewModel().HasOnlyWarnings);
    }

    [Fact]
    public void Os_textos_das_regras_nao_mostram_marcador_cru_nem_arquivo()
    {
        foreach (var rule in QaEngine.Default.Rules)
        {
            var text = ConferenceRuleTexts.Explanation(rule.Id) + ConferenceRuleTexts.Title(rule.Id);
            Assert.DoesNotContain(@"\", text, StringComparison.Ordinal);
            Assert.DoesNotContain(".json", text, StringComparison.Ordinal);
            Assert.DoesNotContain("build", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Clicar_na_ocorrencia_pede_a_linha_do_script_certo()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);
        LineRequest? received = null;
        vm.LineRequested += (_, request) => received = request;
        var row = IssueRows(vm).First(r => r.UnitId == "yst00002:000005");

        vm.OpenLineCommand.Execute(row);

        Assert.Equal(new LineRequest("yst00002", "yst00002:000005"), received);
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
    public async Task Erro_nao_pode_ser_marcado_como_ok()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        var row = IssueRows(vm).Single(r => r.RuleId == "traducao-vazia");

        Assert.False(row.CanMarkOk);
        Assert.False(vm.MarkOkCommand.CanExecute(row));
        Assert.Empty(folder.LoadText("yst00001").SelectMany(u => u.Flags));
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
    public async Task Projeto_sem_problemas_mostra_a_mensagem_de_tudo_certo()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Approved);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };

        await vm.RefreshAsync();

        Assert.Empty(vm.Rows);
        Assert.True(vm.IsEmpty);
        Assert.Equal("Nenhum problema encontrado.", vm.EmptyMessage);
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
    public async Task Enquanto_confere_fica_ocupada_e_nenhum_comando_grava()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        using var gate = new ManualResetEventSlim();
        var vm = new ConferenceViewModel(new QaEngine([new GateRule(gate)])) { Project = ProjectSummaryReader.Read(root) };
        var row = new ConferenceIssueRowViewModel(new QaIssue("regra-porta", QaSeverity.Warning, "yst00001:000000", "yst00001", "problema"));

        var running = vm.RefreshAsync();

        Assert.True(vm.IsBusy);
        Assert.False(vm.FixAllCommand.CanExecute(null));
        Assert.False(vm.MarkOkCommand.CanExecute(row));
        Assert.False(vm.OpenLineCommand.CanExecute(row));

        gate.Set();
        await running;

        Assert.False(vm.IsBusy);
        Assert.True(vm.FixAllCommand.CanExecute(null));
        Assert.True(vm.MarkOkCommand.CanExecute(row));
    }

    [Fact]
    public async Task Enquanto_corrige_fica_ocupada_e_nem_conferir_de_novo_nem_ok_gravam()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        var vm = new ConferenceViewModel(new QaEngine([new FixGateRule(entered, gate)])) { Project = ProjectSummaryReader.Read(root) };
        var row = new ConferenceIssueRowViewModel(new QaIssue("regra-porta", QaSeverity.Warning, "yst00001:000000", "yst00001", "problema"));

        var running = vm.FixAllCommand.ExecuteAsync(null);

        Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.True(vm.IsFixing);
        Assert.True(vm.IsBusy);
        Assert.False(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.FixAllCommand.CanExecute(null));
        Assert.False(vm.MarkOkCommand.CanExecute(row));
        Assert.False(vm.OpenLineCommand.CanExecute(row));

        gate.Set();
        await running;

        Assert.False(vm.IsFixing);
        Assert.False(vm.IsBusy);
        Assert.True(vm.RefreshCommand.CanExecute(null));
        Assert.Equal("Nada a corrigir com um clique.", vm.NoticeMessage);
    }

    [Fact]
    public async Task Regra_que_lanca_vira_mensagem_e_nao_deixa_a_pagina_ocupada()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = new ConferenceViewModel(new QaEngine([new ThrowingRule()])) { Project = ProjectSummaryReader.Read(root) };

        await vm.RefreshAsync();

        Assert.False(vm.IsBusy);
        Assert.Empty(vm.Rows);
        Assert.StartsWith("Não foi possível conferir o projeto: regra quebrada.", vm.ErrorMessage);
        Assert.True(vm.FixAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task Reconferencia_que_lanca_depois_de_corrigir_libera_a_pagina_e_mostra_o_erro()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = new ConferenceViewModel(new QaEngine([new ThrowingRule()])) { Project = ProjectSummaryReader.Read(root) };

        await vm.FixAllCommand.ExecuteAsync(null);

        Assert.False(vm.IsBusy);
        Assert.StartsWith("Não foi possível conferir o projeto:", vm.ErrorMessage);
        Assert.Null(vm.NoticeMessage);
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
        Assert.Equal("2 linhas sem tradução neste script.", rows[0].Detail);
        Assert.Equal("1 linha sem tradução neste script.", rows[1].Detail);
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
        Assert.Equal(empty.Message, empty.Detail);
        Assert.False(empty.HasTranslation);

        Assert.Equal("Corrigir 2 com um clique", vm.FixAllLabel);
    }

    [Fact]
    public async Task A_mesma_regra_apontada_duas_vezes_na_linha_e_um_problema_so_com_a_contagem()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        var row = IssueRows(vm).Single(r => r.UnitId == "yst00002:000005");

        Assert.Equal(2, row.Count);
        Assert.Equal("a b c", row.ProposedText);
        Assert.Equal(5, vm.Filters[0].Count);
    }

    [Fact]
    public async Task Faixa_de_erro_aparece_com_erro_aberto_e_conta_os_erros()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        Assert.True(vm.HasBlockingErrors);
        Assert.Equal("1 problema trava o teste no jogo.", vm.BlockingMessage);
        Assert.Equal("1", vm.BlockingCount);
        Assert.Equal("problema trava o teste no jogo.", vm.BlockingRest);

        var root = SampleProjects.Create(temp.SubFolder("outro"), "Outra VN", TranslationStatus.Approved, TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [Unit("yst00001", 0, null, TranslationStatus.Approved), Unit("yst00001", 1, null, TranslationStatus.Reviewed)]);
        var many = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };
        await many.RefreshAsync();
        Assert.Equal("2 problemas travam o teste no jogo.", many.BlockingMessage);
        Assert.Equal(("2", "problemas travam o teste no jogo."), (many.BlockingCount, many.BlockingRest));

        var clean = new ConferenceViewModel { Project = ProjectSummaryReader.Read(SampleProjects.Create(temp.SubFolder("limpo"), "Limpa", TranslationStatus.Approved)) };
        await clean.RefreshAsync();
        Assert.False(clean.HasBlockingErrors);
        Assert.Equal(string.Empty, clean.BlockingMessage);
        Assert.Equal((string.Empty, string.Empty), (clean.BlockingCount, clean.BlockingRest));
    }

    [Fact]
    public async Task O_cabecalho_diz_quando_conferiu_quantas_linhas_e_quantas_regras()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var now = new DateTimeOffset(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);
        var vm = new ConferenceViewModel(clock: new FixedClock(now)) { Project = ProjectSummaryReader.Read(root) };

        Assert.Equal("Ainda não conferido", vm.CheckedSummary);

        await vm.RefreshAsync();

        var rules = QaEngine.Default.Rules.Count;
        Assert.Equal($"Última conferência agora · 1 linha · {rules} regras ativas", vm.CheckedSummary);
    }

    [Fact]
    public async Task Sem_projeto_o_cabecalho_e_a_faixa_ficam_vazios()
    {
        var vm = new ConferenceViewModel();

        await vm.RefreshAsync();

        Assert.Equal(string.Empty, vm.CheckedSummary);
        Assert.False(vm.HasBlockingErrors);
        Assert.Equal("Corrigir com um clique", vm.FixAllLabel);
        Assert.Null(vm.SelectedRow);
        Assert.False(vm.HasSelection);
    }

    [Fact]
    public async Task O_primeiro_problema_vem_selecionado_e_marcar_ok_passa_para_o_vizinho()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        Assert.Equal("traducao-vazia", vm.SelectedRow?.RuleId);
        Assert.True(vm.HasSelection);

        var first = IssueRows(vm).First(r => r.RuleId == "espaco-duplo");
        vm.SelectedRow = first;
        await vm.MarkOkCommand.ExecuteAsync(first);

        // O aviso saiu da lista; no lugar dele fica o que veio depois.
        Assert.Equal(("espaco-duplo", "yst00002:000005"), (vm.SelectedRow?.RuleId, vm.SelectedRow?.UnitId));
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

    [Fact]
    public void O_trecho_sem_espacos_ao_redor_e_cortado()
    {
        var before = "a  " + new string('x', 100);
        var after = "a " + new string('x', 100);

        Assert.Equal("“a  " + new string('x', 57) + "…”", ConferenceIssueRowViewModel.Excerpt(before, after));
    }

    [Theory]
    [InlineData("Tem certeza?  Você vai se molhar.", "Tem certeza? Você vai se molhar.", "“certeza?  Você”")]
    [InlineData("Uma frase bem longa antes do problema, anda !", "Uma frase bem longa antes do problema, anda!", "“anda !”")]
    [InlineData("a  b e depois um trecho comprido sem problema nenhum", "a b e depois um trecho comprido sem problema nenhum", "“a  b”")]
    public void O_trecho_mostra_o_que_a_correcao_muda_com_a_palavra_de_cada_lado(string before, string after, string expected)
    {
        Assert.Equal(expected, ConferenceIssueRowViewModel.Excerpt(before, after));
    }

    private sealed class FixGateRule(ManualResetEventSlim entered, ManualResetEventSlim gate) : IQaRule, IQaFixer
    {
        public string Id => "regra-porta";

        public string Description => "Corrige só depois que o teste liberar.";

        public IEnumerable<QaIssue> Check(QaContext context) => [];

        public string Fix(string translation)
        {
            entered.Set();
            gate.Wait(TimeSpan.FromSeconds(10));
            return translation;
        }
    }

    private sealed class ThrowingRule : IQaRule
    {
        public string Id => "regra-quebrada";

        public string Description => "Sempre lança.";

        public IEnumerable<QaIssue> Check(QaContext context) => throw new InvalidOperationException("regra quebrada.");
    }

    private sealed class GateRule(ManualResetEventSlim gate) : IQaRule
    {
        public string Id => "regra-porta";

        public string Description => "Espera o teste liberar.";

        public IEnumerable<QaIssue> Check(QaContext context)
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            return [];
        }
    }
}
