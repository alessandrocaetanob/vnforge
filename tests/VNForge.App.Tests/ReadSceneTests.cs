using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O leitor de cena (#475): uma fala por vez, sem nada para aprovar, e a marca "soa estranho" gravada em <c>Flags</c>.</summary>
public sealed class ReadSceneTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly string raiz;

    public ReadSceneTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste");
        var projeto = ProjectFolder.Open(raiz);
        projeto.SaveText("yst00001",
        [
            Fala(1, 0, "美咲", "「おはよう」\\p", "Bom dia.\\p", TranslationStatus.Machine),
            Fala(1, 1, null, "雨が降っている。\\p", null, TranslationStatus.Untranslated),
            Fala(1, 2, "健太", "「うん」\\p", "Aham.\\p", TranslationStatus.Approved),
            Fala(1, 3, "美咲", "「ねえ」\\p", "Ei.\\p", TranslationStatus.Machine),
            Fala(1, 4, "健太", "「なに？」\\p", "O que foi?\\p", TranslationStatus.Reviewed),
        ]);
        projeto.SaveText("yst00002", [Fala(2, 0, "美咲", "「うん」\\p", "Sim.\\p", TranslationStatus.Machine)]);
        projeto.SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "animada"), new CharacterEntry("健太", "Kenta")]);
    }

    public void Dispose() => pasta.Dispose();

    private static TextUnit Fala(int script, int n, string? falante, string original, string? traducao, TranslationStatus status) =>
        new($"yst0000{script}:{n:D6}", falante is null ? TextUnitKind.Narration : TextUnitKind.Dialogue, falante, original, traducao, status);

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    private IReadOnlyList<TextUnit> Gravadas(string script = "yst00001") => ProjectFolder.Open(raiz).LoadText(script);

    [Fact]
    public async Task Comeca_na_primeira_fala_da_cena_aberta_com_todas_as_falas_de_qualquer_status()
    {
        var vm = await Abrir();

        vm.StartReading();

        var leitor = vm.Reading!;
        Assert.True(vm.IsReadingScene);
        Assert.Equal(5, leitor.Total);
        Assert.Equal("Fala 1 de 5", leitor.PositionText);
        Assert.Equal("Misaki", leitor.SpeakerText);
        Assert.False(leitor.CanGoBack);
        Assert.True(leitor.IsActive);
    }

    [Fact]
    public async Task Comeca_na_fala_selecionada()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines.Single(l => l.Id == "yst00001:000003");

        vm.StartReading();

        Assert.Equal("Fala 4 de 5", vm.Reading!.PositionText);
    }

    [Fact]
    public async Task Avancar_e_voltar_andam_uma_fala_e_a_primeira_nao_volta()
    {
        var vm = await Abrir();
        vm.StartReading();
        var leitor = vm.Reading!;

        leitor.NextCommand.Execute(null);
        leitor.NextCommand.Execute(null);
        Assert.Equal("Fala 3 de 5", leitor.PositionText);
        Assert.Equal("Kenta", leitor.SpeakerText);

        leitor.BackCommand.Execute(null);
        leitor.BackCommand.Execute(null);
        leitor.BackCommand.Execute(null);
        Assert.Equal("Fala 1 de 5", leitor.PositionText);
    }

    [Fact]
    public async Task Depois_da_ultima_fala_vem_o_fim_da_cena_e_voltar_leva_a_ultima()
    {
        var vm = await Abrir();
        vm.StartReading();
        var leitor = vm.Reading!;

        for (var n = 0; n < 5; n++)
        {
            leitor.NextCommand.Execute(null);
        }

        Assert.True(leitor.IsFinished);
        Assert.False(leitor.IsActive);
        Assert.Equal("Nenhuma fala marcada como estranha.", leitor.FinishedText);

        leitor.BackCommand.Execute(null);
        Assert.False(leitor.IsFinished);
        Assert.Equal("Fala 5 de 5", leitor.PositionText);
    }

    [Fact]
    public async Task Ler_a_cena_inteira_nao_muda_status_nem_traducao_nem_arquivo()
    {
        var vm = await Abrir();
        var arquivo = ProjectFolder.Open(raiz).TextPath("yst00001");
        var antes = File.ReadAllText(arquivo);
        vm.StartReading();

        for (var n = 0; n < 6; n++)
        {
            vm.Reading!.NextCommand.Execute(null);
        }

        vm.StopReading();

        Assert.Equal(antes, File.ReadAllText(arquivo));
        Assert.False(vm.IsReadingScene);
        Assert.Equal(LineStatusFilter.All, vm.Filters.Single(f => f.IsSelected).Filter);
    }

    [Fact]
    public async Task A_caixa_do_jogo_leva_a_traducao_e_o_nome_da_placa()
    {
        var vm = await Abrir();
        vm.StartReading();

        var linha = vm.Reading!.Line!;

        Assert.False(linha.ShowsSource);
        Assert.NotNull(linha.Layout);
        Assert.Equal("Misaki", linha.Layout.Speaker);
        Assert.Contains("Bom dia.", linha.Layout.Boxes.SelectMany(b => b.Lines).Select(l => l.Text));
    }

    [Fact]
    public async Task Fala_sem_traducao_mostra_o_original()
    {
        var vm = await Abrir();
        vm.StartReading();
        vm.Reading!.NextCommand.Execute(null);

        var leitor = vm.Reading;

        Assert.True(leitor.ShowsSource);
        Assert.True(leitor.HasLayout);
    }

    [Fact]
    public async Task F_marca_soa_estranho_no_arquivo_sem_mexer_no_resto_da_fala()
    {
        var vm = await Abrir();
        vm.StartReading();
        var leitor = vm.Reading!;

        leitor.ToggleOddCommand.Execute(null);

        Assert.True(leitor.IsOdd);
        Assert.Equal(1, leitor.OddCount);
        Assert.Equal("1 fala soa estranho", leitor.OddText);
        var gravada = Gravadas()[0];
        Assert.Equal([ReadingFlags.SoundsOdd], gravada.Flags);
        Assert.Equal("Bom dia.\\p", gravada.Translation);
        Assert.Equal(TranslationStatus.Machine, gravada.Status);
        Assert.Equal(1, vm.Filters.Single(f => f.Filter == LineStatusFilter.Odd).Count);
    }

    [Fact]
    public async Task O_chip_Soa_estranho_so_aparece_com_falas_marcadas_ou_quando_e_o_filtro_ativo()
    {
        var vm = await Abrir();
        var chip = vm.Filters.Single(f => f.Filter == LineStatusFilter.Odd);
        Assert.False(chip.IsShown);
        Assert.All(vm.Filters.Where(f => f != chip), f => Assert.True(f.IsShown));

        vm.StartReading();
        vm.Reading!.ToggleOddCommand.Execute(null);
        Assert.True(chip.IsShown);

        vm.Reading.ToggleOddCommand.Execute(null);
        Assert.False(chip.IsShown);
    }

    [Fact]
    public async Task F_de_novo_tira_a_marca()
    {
        var vm = await Abrir();
        vm.StartReading();
        var leitor = vm.Reading!;

        leitor.ToggleOddCommand.Execute(null);
        leitor.ToggleOddCommand.Execute(null);

        Assert.False(leitor.IsOdd);
        Assert.Equal(0, leitor.OddCount);
        Assert.Empty(Gravadas()[0].Flags);
        Assert.False(leitor.HasMarkedNow);
    }

    [Fact]
    public async Task Falha_ao_gravar_a_marca_fica_na_fala_e_diz_o_motivo()
    {
        var vm = await Abrir();
        vm.StartReading();
        var leitor = vm.Reading!;
        File.Delete(ProjectFolder.Open(raiz).TextPath("yst00001"));

        leitor.ToggleOddCommand.Execute(null);

        Assert.False(leitor.IsOdd);
        Assert.True(leitor.HasError);
        Assert.StartsWith("Não foi possível marcar a fala:", leitor.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sair_com_marca_abre_o_Editor_so_com_as_falas_marcadas()
    {
        var vm = await Abrir();
        vm.StartReading();
        var leitor = vm.Reading!;
        leitor.NextCommand.Execute(null);
        leitor.NextCommand.Execute(null);
        leitor.ToggleOddCommand.Execute(null);
        leitor.NextCommand.Execute(null);
        leitor.NextCommand.Execute(null);
        leitor.ToggleOddCommand.Execute(null);

        leitor.CloseCommand.Execute(null);

        Assert.False(vm.IsReadingScene);
        var chip = vm.Filters.Single(f => f.IsSelected);
        Assert.Equal(LineStatusFilter.Odd, chip.Filter);
        Assert.Equal(["yst00001:000002", "yst00001:000004"], vm.Lines.Select(l => l.Id));
    }

    [Fact]
    public async Task Sair_sem_marca_deixa_o_filtro_como_estava()
    {
        var vm = await Abrir();
        vm.SelectFilterCommand.Execute(vm.Filters.Single(f => f.Filter == LineStatusFilter.Machine));
        vm.StartReading();

        vm.Reading!.CloseCommand.Execute(null);

        Assert.Equal(LineStatusFilter.Machine, vm.Filters.Single(f => f.IsSelected).Filter);
    }

    [Fact]
    public async Task Marca_de_leitura_antiga_nao_liga_o_filtro_ao_sair_sem_marcar_nada_agora()
    {
        ProjectFolder.Open(raiz).SaveText("yst00001", Gravadas().Select((u, i) => i == 1 ? u.WithOdd(true) : u).ToList());
        var vm = await Abrir();
        Assert.Equal(1, vm.Filters.Single(f => f.Filter == LineStatusFilter.Odd).Count);
        vm.StartReading();

        Assert.Equal(1, vm.Reading!.OddCount);
        vm.Reading.CloseCommand.Execute(null);

        Assert.Equal(LineStatusFilter.All, vm.Filters.Single(f => f.IsSelected).Filter);
    }

    [Fact]
    public async Task A_marca_sobrevive_a_fechar_e_abrir_o_projeto()
    {
        var vm = await Abrir();
        vm.StartReading();
        vm.Reading!.ToggleOddCommand.Execute(null);
        vm.StopReading();

        var outro = await Abrir();

        Assert.Equal(1, outro.Filters.Single(f => f.Filter == LineStatusFilter.Odd).Count);
    }

    [Fact]
    public async Task Marcar_depois_de_editar_a_linha_no_painel_nao_perde_a_edicao()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[0];
        vm.Detail!.FieldText = "Bom dia, Kenta.";
        vm.StartReading();

        vm.Reading!.ToggleOddCommand.Execute(null);

        var gravada = Gravadas()[0];
        Assert.Equal("Bom dia, Kenta.", gravada.Translation?.TrimEnd('\\', 'p'));
        Assert.True(gravada.IsOdd());
    }

    [Fact]
    public async Task Abrir_outra_cena_fecha_o_leitor_sem_mexer_no_filtro()
    {
        var vm = await Abrir();
        vm.StartReading();
        vm.Reading!.ToggleOddCommand.Execute(null);

        vm.SelectedScript = vm.Scripts.Single(s => s.Key == "yst00002");

        Assert.False(vm.IsReadingScene);
        Assert.Equal(LineStatusFilter.All, vm.Filters.Single(f => f.IsSelected).Filter);
    }

    [Fact]
    public async Task Abrir_o_leitor_fecha_o_modo_revisao()
    {
        var vm = await Abrir();
        vm.StartReview();
        Assert.True(vm.IsReviewing);

        vm.StartReading();

        Assert.False(vm.IsReviewing);
        Assert.True(vm.IsReadingScene);
    }

    [Fact]
    public async Task Releitura_do_projeto_fecha_o_leitor()
    {
        var vm = await Abrir();
        vm.StartReading();

        vm.Reload();

        Assert.False(vm.IsReadingScene);
    }
}
