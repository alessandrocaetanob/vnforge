using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class LineDetailViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(Agora);
    private readonly FakeNavigator navegador = new();
    private readonly string raiz;

    public LineDetailViewModelTests() =>
        raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Untranslated, TranslationStatus.Machine, TranslationStatus.Untranslated);

    public void Dispose() => pasta.Dispose();

    private LineDetailViewModel Abrir(int n, TextUnit? linha = null)
    {
        var unidade = linha ?? Gravada(raiz, n);
        return new LineDetailViewModel(unidade, "yst00001", new LineEditor(ProjectFolder.Open(raiz), relogio), navegador, relogio, acao => acao());
    }

    private static TextUnit Gravada(string raiz, int n) => ProjectFolder.Open(raiz).LoadText("yst00001")[n];

    [Fact]
    public void Nao_traduzir_marca_a_linha_grava_no_arquivo_e_esvazia_o_campo()
    {
        using var vm = Abrir(1);
        Assert.False(string.IsNullOrEmpty(vm.Translation));

        vm.KeepCommand.Execute(null);

        Assert.Equal(TranslationStatus.Keep, vm.Status);
        Assert.True(vm.IsKeep);
        Assert.Equal("Não traduzir", vm.StatusText);
        Assert.Equal(string.Empty, vm.Translation);
        Assert.Equal(string.Empty, vm.FieldText);
        var gravada = Gravada(raiz, 1);
        Assert.Equal(TranslationStatus.Keep, gravada.Status);
        Assert.Null(gravada.Translation);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Traduzir_desfaz_o_nao_traduzir_e_a_linha_volta_a_ficar_sem_traducao()
    {
        using var vm = Abrir(0);
        vm.KeepCommand.Execute(null);

        vm.TranslateCommand.Execute(null);

        Assert.Equal(TranslationStatus.Untranslated, vm.Status);
        Assert.False(vm.IsKeep);
        Assert.Equal(TranslationStatus.Untranslated, Gravada(raiz, 0).Status);
    }

    [Fact]
    public void Expoe_original_falante_status_e_notas_da_linha()
    {
        var unidade = new TextUnit("yst00001:000000", TextUnitKind.Dialogue, "美咲", @"こんにちは\n{VAR:name}\p", "Oi.", TranslationStatus.Reviewed) { Notes = "ver glossário" };

        using var vm = Abrir(0, unidade);

        Assert.Equal("美咲", vm.Speaker);
        Assert.Equal(@"こんにちは\n{VAR:name}\p", vm.Source);
        Assert.Equal("Oi.", vm.Translation);
        Assert.Equal("ver glossário", vm.Notes);
        Assert.Equal(TranslationStatus.Reviewed, vm.Status);
        Assert.Equal("Revisada", vm.StatusText);
        Assert.Equal([false, true, true, true], vm.SourceSegments.Select(s => s.IsMarker));
        Assert.Equal("yst00001:000000", vm.Id);
        Assert.Equal("yst00001", vm.ScriptKey);
        Assert.False(vm.IsApproved);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Linha_sem_traducao_nem_falante_abre_com_campos_vazios()
    {
        var unidade = new TextUnit("yst00001:000000", TextUnitKind.Narration, "", "原文", null, TranslationStatus.Untranslated);

        using var vm = Abrir(0, unidade);

        Assert.Null(vm.Speaker);
        Assert.Equal(string.Empty, vm.Translation);
        Assert.Equal("Sem tradução", vm.StatusText);
        Assert.Empty(vm.MissingMarkers);
    }

    [Fact]
    public void Original_com_marcador_invalido_aparece_sem_chips()
    {
        var unidade = new TextUnit("yst00001:000000", TextUnitKind.Other, null, @"abc\x", null, TranslationStatus.Untranslated);

        using var vm = Abrir(0, unidade);

        Assert.Equal([new TextSegment(@"abc\x", false)], vm.SourceSegments);
    }

    [Fact]
    public void Notas_tambem_gravam_com_debounce()
    {
        using var vm = Abrir(1);

        vm.Notes = "conferir tom";
        Assert.Equal(string.Empty, Gravada(raiz, 1).Notes);
        relogio.Advance(LineDetailViewModel.SaveDelay);

        var lida = Gravada(raiz, 1);
        Assert.Equal("conferir tom", lida.Notes);
        Assert.Equal(TranslationStatus.Machine, lida.Status);
    }

    [Fact]
    public void Descartar_o_painel_grava_o_que_estava_pendente()
    {
        var vm = Abrir(0);
        vm.Translation = "Pendente";

        vm.Dispose();
        vm.Dispose();

        Assert.Equal("Pendente", Gravada(raiz, 0).Translation);
        vm.Translation = "Depois de descartar";
        relogio.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("Pendente", Gravada(raiz, 0).Translation);
    }

    [Fact]
    public void Proxima_e_anterior_gravam_antes_de_trocar_de_linha()
    {
        using var vm = Abrir(0);
        vm.Translation = "Primeira";

        vm.NextCommand.Execute(null);

        Assert.Equal("Primeira", Gravada(raiz, 0).Translation);
        Assert.Equal(1, navegador.Next);

        vm.Translation = "Segunda";
        vm.PreviousCommand.Execute(null);

        Assert.Equal("Segunda", Gravada(raiz, 0).Translation);
        Assert.Equal(1, navegador.Previous);
    }

    [Fact]
    public void Aprovar_sem_traducao_mostra_o_erro_e_nao_avanca()
    {
        using var vm = Abrir(0);

        vm.ApproveAndNextCommand.Execute(null);

        Assert.Contains("não tem tradução", vm.ErrorMessage);
        Assert.Equal(0, navegador.Next);
        Assert.Equal(TranslationStatus.Untranslated, Gravada(raiz, 0).Status);
    }

    [Fact]
    public void Marcadores_faltando_aparecem_conforme_a_traducao_muda()
    {
        var unidade = new TextUnit("yst00001:000000", TextUnitKind.Dialogue, null, @"A\nB\p{VAR:x}", null, TranslationStatus.Untranslated);
        using var vm = Abrir(0, unidade);
        Assert.Empty(vm.MissingMarkers);

        vm.Translation = @"a\n";
        Assert.Equal([@"\p", "{VAR:x}"], vm.MissingMarkers);
        Assert.True(vm.HasMissingMarkers);
        Assert.Contains(@"\p, {VAR:x}", vm.MissingMarkersText);

        vm.Translation = @"a\nb\p{VAR:x}";
        Assert.Empty(vm.MissingMarkers);
        Assert.False(vm.HasMissingMarkers);
        Assert.Equal(string.Empty, vm.MissingMarkersText);
    }

    [Fact]
    public void Marcador_invalido_na_traducao_vira_aviso_sem_derrubar_o_painel()
    {
        var unidade = new TextUnit("yst00001:000000", TextUnitKind.Dialogue, null, @"A\n", null, TranslationStatus.Untranslated);
        using var vm = Abrir(0, unidade);

        vm.Translation = "abc {VAR:";

        Assert.NotNull(vm.MarkerProblem);
        Assert.Empty(vm.MissingMarkers);

        vm.Translation = @"abc\n";
        Assert.Null(vm.MarkerProblem);
    }

    [Fact]
    public void Aprovar_com_erro_ao_gravar_nao_avanca()
    {
        var caminho = ProjectFolder.Open(raiz).TextPath("yst00001");
        using var vm = Abrir(1);
        File.Delete(caminho);

        vm.ApproveAndNextCommand.Execute(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Equal(0, navegador.Next);
    }

    [Fact]
    public void Argumentos_nulos_sao_recusados()
    {
        var unidade = Gravada(raiz, 0);
        var editor = new LineEditor(ProjectFolder.Open(raiz), relogio);
        Action<Action> direto = a => a();

        Assert.Throws<ArgumentNullException>(() => new LineDetailViewModel(null!, "k", editor, navegador, relogio, direto));
        Assert.Throws<ArgumentNullException>(() => new LineDetailViewModel(unidade, null!, editor, navegador, relogio, direto));
        Assert.Throws<ArgumentNullException>(() => new LineDetailViewModel(unidade, "k", null!, navegador, relogio, direto));
        Assert.Throws<ArgumentNullException>(() => new LineDetailViewModel(unidade, "k", editor, null!, relogio, direto));
        Assert.Throws<ArgumentNullException>(() => new LineDetailViewModel(unidade, "k", editor, navegador, null!, direto));
        Assert.Throws<ArgumentNullException>(() => new LineDetailViewModel(unidade, "k", editor, navegador, relogio, null!));
    }

    private sealed class FakeNavigator : ILineNavigator
    {
        public int Next { get; private set; }

        public int Previous { get; private set; }

        public void SelectNext() => Next++;

        public void SelectPrevious() => Previous++;
    }
}
