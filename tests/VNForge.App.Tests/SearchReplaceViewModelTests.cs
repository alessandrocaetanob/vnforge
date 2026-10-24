using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Search;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class SearchReplaceViewModelTests
{
    private static (SearchReplaceViewModel Vm, ProjectFolder Folder) Abrir(TempFolder temp, params TranslationStatus[] statuses)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", statuses);
        var vm = new SearchReplaceViewModel { Project = ProjectSummaryReader.Read(root) };
        return (vm, ProjectFolder.Open(root));
    }

    [Fact]
    public async Task Buscar_lista_as_linhas_com_a_posicao_e_resume()
    {
        using var temp = new TempFolder();
        var (vm, _) = Abrir(temp, TranslationStatus.Translated, TranslationStatus.Untranslated, TranslationStatus.Approved);
        vm.Pattern = "tradução";

        await vm.FindCommand.ExecuteAsync(null);

        Assert.Equal(["yst00001 · linha 1", "yst00001 · linha 3"], vm.Results.Select(r => r.Location));
        Assert.All(vm.Results, r => Assert.False(r.HasAfter));
        Assert.Equal("2 linhas com 2 ocorrências.", vm.Summary);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Sem_ocorrencia_diz_isso_e_a_lista_fica_vazia()
    {
        using var temp = new TempFolder();
        var (vm, _) = Abrir(temp, TranslationStatus.Translated);
        vm.Pattern = "inexistente";

        await vm.FindCommand.ExecuteAsync(null);

        Assert.Empty(vm.Results);
        Assert.False(vm.HasResults);
        Assert.Equal("Nenhuma ocorrência.", vm.Summary);
    }

    [Fact]
    public async Task Regex_invalida_mostra_o_erro_em_portugues_sem_lancar()
    {
        using var temp = new TempFolder();
        var (vm, _) = Abrir(temp, TranslationStatus.Translated);
        vm.Pattern = "(";
        vm.UseRegex = true;

        await vm.FindCommand.ExecuteAsync(null);

        Assert.Contains("inválida", vm.ErrorMessage);
        Assert.Empty(vm.Results);
    }

    [Fact]
    public void Sem_texto_ou_sem_projeto_os_comandos_ficam_desligados()
    {
        var vm = new SearchReplaceViewModel();
        vm.Pattern = "x";

        Assert.False(vm.FindCommand.CanExecute(null));
        Assert.False(vm.PreviewCommand.CanExecute(null));
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Previa_mostra_antes_e_depois_e_nao_grava()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Abrir(temp, TranslationStatus.Translated, TranslationStatus.Translated);
        var antes = File.ReadAllText(folder.TextPath("yst00001"));
        vm.Pattern = "tradução";
        vm.Replacement = "tradução nova";

        await vm.PreviewCommand.ExecuteAsync(null);

        Assert.Equal(["tradução 0", "tradução 1"], vm.Results.Select(r => r.Before));
        Assert.Equal(["tradução nova 0", "tradução nova 1"], vm.Results.Select(r => r.After));
        Assert.True(vm.HasPreview);
        Assert.True(vm.ApplyCommand.CanExecute(null));
        Assert.Contains("Nada foi gravado ainda", vm.Summary);
        Assert.Equal(antes, File.ReadAllText(folder.TextPath("yst00001")));
    }

    [Fact]
    public async Task Aplicar_so_vale_depois_da_previa_e_grava_o_que_ela_mostrou()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Abrir(temp, TranslationStatus.Approved, TranslationStatus.Translated);
        vm.Pattern = "tradução 1";
        vm.Replacement = "TROCADA";
        Assert.False(vm.ApplyCommand.CanExecute(null));
        ReplaceResult? aplicado = null;
        vm.Applied += (_, r) => aplicado = r;

        await vm.PreviewCommand.ExecuteAsync(null);
        await vm.ApplyCommand.ExecuteAsync(null);

        var linhas = folder.LoadText("yst00001");
        Assert.Equal("tradução 0", linhas[0].Translation);
        Assert.Equal("TROCADA", linhas[1].Translation);
        Assert.Equal(1, aplicado?.Applied);
        Assert.Equal("1 linha gravada.", vm.Summary);
        Assert.False(vm.HasPreview);
        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task Mudar_um_campo_depois_da_previa_descarta_a_previa()
    {
        using var temp = new TempFolder();
        var (vm, _) = Abrir(temp, TranslationStatus.Translated);
        vm.Pattern = "tradução";
        vm.Replacement = "x";
        await vm.PreviewCommand.ExecuteAsync(null);
        Assert.True(vm.ApplyCommand.CanExecute(null));

        vm.Replacement = "y";

        Assert.False(vm.HasPreview);
        Assert.False(vm.ApplyCommand.CanExecute(null));
        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task Linha_editada_depois_da_previa_fica_de_fora_e_o_resumo_avisa()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Abrir(temp, TranslationStatus.Translated, TranslationStatus.Translated);
        vm.Pattern = "tradução";
        vm.Replacement = "nova";
        await vm.PreviewCommand.ExecuteAsync(null);
        new LineEditor(folder, TimeProvider.System).SetTranslation("yst00001", "yst00001:000000", "mudou");

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(["mudou", "nova 1"], folder.LoadText("yst00001").Select(u => u.Translation));
        Assert.Contains("1 linha mudou depois da prévia", vm.Summary);
    }

    [Fact]
    public async Task Procurar_no_original_desliga_a_substituicao()
    {
        using var temp = new TempFolder();
        var (vm, _) = Abrir(temp, TranslationStatus.Untranslated);
        vm.Pattern = "原文";
        vm.SearchSource = true;

        await vm.FindCommand.ExecuteAsync(null);

        Assert.Single(vm.Results);
        Assert.False(vm.CanReplace);
        Assert.False(vm.PreviewCommand.CanExecute(null));
    }

    [Fact]
    public async Task Edicao_pendente_no_Editor_que_nao_grava_impede_a_aplicacao()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Abrir(temp, TranslationStatus.Translated);
        vm.Pattern = "tradução";
        vm.Replacement = "nova";
        vm.FlushPendingEdits = () => false;
        await vm.PreviewCommand.ExecuteAsync(null);

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Contains("Editor", vm.ErrorMessage);
        Assert.Equal("tradução 0", folder.LoadText("yst00001")[0].Translation);
    }

    [Fact]
    public void Procurar_no_original_desliga_palavra_inteira_e_nao_a_passa_para_a_busca()
    {
        using var temp = new TempFolder();
        var (vm, _) = Abrir(temp, TranslationStatus.Translated);
        vm.WholeWord = true;
        Assert.True(vm.CanWholeWord);

        vm.SearchSource = true;

        Assert.False(vm.CanWholeWord);
        Assert.False(vm.WholeWord);
    }

    [Fact]
    public async Task Palavra_inteira_vale_na_traducao()
    {
        using var temp = new TempFolder();
        var (vm, _) = Abrir(temp, TranslationStatus.Translated);
        vm.Pattern = "tradu";
        vm.WholeWord = true;

        await vm.FindCommand.ExecuteAsync(null);
        Assert.Empty(vm.Results);

        vm.Pattern = "tradução";
        await vm.FindCommand.ExecuteAsync(null);
        Assert.Single(vm.Results);
    }

    [Fact]
    public async Task Cancelar_interrompe_a_busca_e_nao_deixa_resultado()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Translated);
        using var comecou = new ManualResetEventSlim();
        var vm = new SearchReplaceViewModel(find: (_, _, token) =>
        {
            comecou.Set();
            token.WaitHandle.WaitOne();
            token.ThrowIfCancellationRequested();
            return new SearchResult([], 0);
        })
        {
            Project = ProjectSummaryReader.Read(root),
            Pattern = "x",
        };
        Assert.False(vm.CancelCommand.CanExecute(null));

        var busca = vm.FindCommand.ExecuteAsync(null);
        comecou.Wait(TestContext.Current.CancellationToken);
        Assert.True(vm.IsBusy);
        Assert.True(vm.CancelCommand.CanExecute(null));
        vm.CancelCommand.Execute(null);
        await busca;

        Assert.Equal("Cancelado. Nada foi gravado.", vm.Summary);
        Assert.False(vm.IsBusy);
        Assert.False(vm.CancelCommand.CanExecute(null));
        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task Padrao_que_so_casa_vazio_explica_na_busca_e_na_previa()
    {
        using var temp = new TempFolder();
        var (vm, _) = Abrir(temp, TranslationStatus.Translated);
        vm.Pattern = "^";
        vm.UseRegex = true;

        await vm.FindCommand.ExecuteAsync(null);
        Assert.Contains("só casou texto vazio", vm.Summary);

        await vm.PreviewCommand.ExecuteAsync(null);
        Assert.Contains("só casou texto vazio", vm.Summary);
        Assert.False(vm.HasPreview);
    }

    [Fact]
    public async Task Previa_avisa_quantas_linhas_perdem_a_aprovacao_e_lista_as_puladas()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Abrir(temp, TranslationStatus.Approved, TranslationStatus.Reviewed, TranslationStatus.Machine);
        var linhas = folder.LoadText("yst00001").ToList();
        linhas[2] = linhas[2] with { Translation = @"tradução \q" };
        folder.SaveText("yst00001", linhas);
        vm.Pattern = "tradução";
        vm.Replacement = "nova";

        await vm.PreviewCommand.ExecuteAsync(null);

        Assert.Contains("1 aprovada e 1 revisada", vm.Summary);
        Assert.Contains("já tem um marcador de controle malformado: 1", vm.Summary);
        Assert.Equal(["yst00001 · linha 3: o texto já tem um marcador malformado."], vm.SkippedNotes);
        Assert.True(vm.HasSkippedNotes);
    }

    [Fact]
    public void Trecho_da_lista_mostra_a_janela_em_volta_da_troca_em_texto_longo()
    {
        var longo = new string('a', 300) + "ALVO" + new string('b', 300);

        var linha = new SearchResultRow("s · linha 1", longo, longo.Replace("ALVO", "novo"), 1, 300);

        Assert.Contains("ALVO", linha.BeforeExcerpt);
        Assert.Contains("novo", linha.AfterExcerpt);
        Assert.StartsWith("…", linha.BeforeExcerpt);
        Assert.EndsWith("…", linha.BeforeExcerpt);
        Assert.True(linha.BeforeExcerpt.Length <= 142);
        Assert.Equal("curto", SearchResultRow.Excerpt("curto", 3));
        Assert.StartsWith("ALVO", SearchResultRow.Excerpt("ALVO" + new string('x', 300), 0));
    }

    [Fact]
    public async Task Aplicar_que_falha_no_segundo_script_avisa_e_manda_o_Editor_reler()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Abrir(temp, TranslationStatus.Translated);
        folder.SaveText("yst00002", [new TextUnit("yst00002:000000", TextUnitKind.Dialogue, null, "原文", "tradução b", TranslationStatus.Translated)]);
        vm.Pattern = "tradução";
        vm.Replacement = "nova";
        await vm.PreviewCommand.ExecuteAsync(null);
        File.Delete(folder.TextPath("yst00002"));
        var avisos = new List<ReplaceResult>();
        vm.Applied += (_, r) => avisos.Add(r);

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Contains("Alguns scripts podem já ter sido gravados", vm.ErrorMessage);
        Assert.Equal(0, Assert.Single(avisos).Applied);
        Assert.Equal("nova 0", folder.LoadText("yst00001")[0].Translation);
        Assert.False(vm.HasPreview);
    }

    [Fact]
    public async Task Janela_principal_tem_o_item_Busca_e_o_Editor_relê_depois_de_aplicar()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Translated);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(DateTimeOffset.UnixEpoch)));
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        var item = vm.Nav.Single(n => n.Key == NavKey.Search);
        Assert.True(item.IsEnabled);

        vm.SelectedNav = item;
        Assert.Same(vm.Search, vm.CurrentPage);
        vm.Search.Pattern = "tradução";
        vm.Search.Replacement = "nova";
        await vm.Editor.Loading;
        await vm.Search.PreviewCommand.ExecuteAsync(null);
        await vm.Search.ApplyCommand.ExecuteAsync(null);
        await vm.Editor.Loading;

        Assert.Equal("nova 0", ProjectFolder.Open(root).LoadText("yst00001")[0].Translation);
    }
}
