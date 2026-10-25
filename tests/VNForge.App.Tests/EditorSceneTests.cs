using Microsoft.Extensions.Time.Testing;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>"Na cena" (a fala anterior e a seguinte) e "Usar em todos" (a mesma fala em outras linhas) no inspetor do Editor.</summary>
public sealed class EditorSceneTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public EditorSceneTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste");
        var projeto = ProjectFolder.Open(raiz);
        projeto.SaveText("yst00001",
        [
            Fala(1, 0, "美咲", "「おはよう」\\p", "Bom dia.\\p", TranslationStatus.Machine),
            Fala(1, 1, null, "雨が降っている。\\p", null, TranslationStatus.Untranslated),
            Fala(1, 2, "健太", "「うん」\\p", "Aham.\\p", TranslationStatus.Approved),
            Fala(1, 3, "健太", "「うん」\\p", null, TranslationStatus.Untranslated),
        ]);
        projeto.SaveText("yst00002",
        [
            Fala(2, 0, "美咲", "「うん」\\p", "Sim.\\p", TranslationStatus.Machine),
            Fala(2, 1, "美咲", "「うん」", null, TranslationStatus.Untranslated),
        ]);
    }

    public void Dispose() => pasta.Dispose();

    private static TextUnit Fala(int script, int n, string? falante, string original, string? traducao, TranslationStatus status) =>
        new($"yst0000{script}:{n:D6}", falante is null ? TextUnitKind.Narration : TextUnitKind.Dialogue, falante, original, traducao, status);

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    private TextUnit Gravada(string script, int n) => ProjectFolder.Open(raiz).LoadText(script)[n];

    [Fact]
    public async Task Na_cena_mostra_a_fala_anterior_e_a_seguinte_sem_marcadores()
    {
        var vm = await Abrir();

        vm.SelectedLine = vm.Lines[1];

        Assert.Equal(
            [new ContextLine("美咲", "「おはよう」", "Bom dia."), new ContextLine("健太", "「うん」", "Aham.")],
            vm.Detail!.Scene);
        Assert.True(vm.Detail.HasScene);
    }

    [Fact]
    public async Task Na_primeira_e_na_ultima_linha_so_existe_um_lado()
    {
        var vm = await Abrir();

        Assert.Equal([new ContextLine("Narração", "雨が降っている。")], vm.Detail!.Scene);

        vm.SelectedLine = vm.Lines[^1];

        Assert.Equal([new ContextLine("健太", "「うん」", "Aham.")], vm.Detail!.Scene);
    }

    [Fact]
    public async Task A_cena_ignora_o_filtro_da_grade()
    {
        var vm = await Abrir();
        vm.ShowPendingLines();

        vm.SelectedLine = vm.Lines[0];

        Assert.Equal("美咲", vm.Detail!.Scene[0].Who);
    }

    [Fact]
    public async Task A_mesma_fala_em_outras_linhas_aparece_no_inspetor_com_a_contagem()
    {
        var vm = await Abrir();

        vm.SelectedLine = vm.Lines[2];

        Assert.Equal(2, vm.Detail!.SameSourceCount);
        Assert.True(vm.Detail.HasSameSource);
        Assert.Equal("Esta fala aparece em mais 2 linhas.", vm.Detail.SameSourceText);
    }

    [Fact]
    public async Task Fala_unica_nao_oferece_usar_em_todos()
    {
        var vm = await Abrir();

        vm.SelectedLine = vm.Lines[0];

        Assert.False(vm.Detail!.HasSameSource);
        Assert.False(vm.Detail.UseForAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task Usar_em_todos_grava_a_traducao_nas_linhas_do_mesmo_original_e_em_nenhuma_outra()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[3];
        vm.Detail!.FieldText = "Tá bom.";

        vm.Detail.UseForAllCommand.Execute(null);

        Assert.Equal("Tá bom.\\p", Gravada("yst00001", 3).Translation);
        Assert.Equal("Tá bom.\\p", Gravada("yst00001", 2).Translation);
        Assert.Equal("Tá bom.\\p", Gravada("yst00002", 0).Translation);
        Assert.Equal("Bom dia.\\p", Gravada("yst00001", 0).Translation);
        Assert.Null(Gravada("yst00001", 1).Translation);
        Assert.Null(Gravada("yst00002", 1).Translation);
        Assert.Equal("Aplicada a 2 linhas.", vm.Detail.SameSourceResult);
        Assert.Null(vm.Detail.ErrorMessage);
    }

    [Fact]
    public async Task Usar_em_todos_atualiza_a_grade_do_script_aberto()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[3];
        vm.Detail!.FieldText = "Tá bom.";

        vm.Detail.UseForAllCommand.Execute(null);

        Assert.Equal("Tá bom.", vm.Lines[2].TranslationPlain);
        Assert.Equal(TranslationStatus.Translated, vm.Lines[2].Status);
        Assert.Equal(TranslationStatus.Translated, vm.Lines[3].Status);
        Assert.Equal(TranslationStatus.Machine, vm.Lines[0].Status);
    }

    [Fact]
    public async Task Usar_em_todos_sem_traducao_no_campo_avisa_e_nao_grava()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[3];

        vm.Detail!.UseForAllCommand.Execute(null);

        Assert.Equal("Escreva a tradução desta linha antes de usá-la nas outras.", vm.Detail.ErrorMessage);
        Assert.Equal("Aham.\\p", Gravada("yst00001", 2).Translation);
    }

    [Fact]
    public async Task Voltar_a_uma_linha_do_script_mostra_o_texto_aplicado()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[3];
        vm.Detail!.FieldText = "Tá bom.";
        vm.Detail.UseForAllCommand.Execute(null);

        vm.SelectedLine = vm.Lines[2];

        Assert.Equal("Tá bom.\\p", vm.Detail!.Translation);
    }
}
