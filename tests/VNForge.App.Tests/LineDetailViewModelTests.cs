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
    public void Depois_de_gravar_o_campo_mostra_o_texto_com_a_pontuacao_do_teclado()
    {
        using var vm = Abrir(0);
        var mudancas = new List<string?>();
        vm.PropertyChanged += (_, e) => mudancas.Add(e.PropertyName);

        vm.FieldText = "～Oi。（tudo bem）";
        Assert.True(vm.Flush());

        Assert.Equal("~Oi.(tudo bem)", ProjectFolder.Open(raiz).LoadText("yst00001")[0].Translation);
        Assert.Equal("~Oi.(tudo bem)", vm.Translation);
        Assert.Equal("~Oi.(tudo bem)", vm.FieldText);
        Assert.Equal("~Oi.(tudo bem)", vm.Saved.Translation);
        Assert.Null(vm.ErrorMessage);
    }

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
    public void Nao_traduzir_leva_o_texto_digitado_e_ainda_nao_gravado_a_valer_antes_de_marcar()
    {
        using var vm = Abrir(0);
        vm.FieldText = "MS Gothic";

        vm.KeepCommand.Execute(null);
        vm.Flush();

        Assert.Equal(TranslationStatus.Keep, Gravada(raiz, 0).Status);
        Assert.Null(Gravada(raiz, 0).Translation);
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
    public void Digitar_numa_linha_nao_traduzir_traduz_a_linha()
    {
        using var vm = Abrir(0);
        vm.KeepCommand.Execute(null);

        vm.FieldText = "Fonte";
        vm.Flush();

        Assert.Equal(TranslationStatus.Translated, vm.Status);
        Assert.Equal("Fonte", Gravada(raiz, 0).Translation);
    }

    [Fact]
    public void Depois_da_troca_nao_ha_nova_gravacao_pendente()
    {
        using var vm = Abrir(0);
        vm.FieldText = "Oi。";
        vm.Flush();
        var gravada = vm.Saved;

        vm.Flush();

        Assert.Same(gravada, vm.Saved);
    }

    [Fact]
    public void Texto_sem_pontuacao_japonesa_fica_como_foi_digitado()
    {
        using var vm = Abrir(0);

        vm.FieldText = "Bom dia, tudo bem?";
        vm.Flush();

        Assert.Equal("Bom dia, tudo bem?", vm.FieldText);
        Assert.Equal("Bom dia, tudo bem?", vm.Translation);
    }

    [Fact]
    public void Quebra_de_linha_do_campo_sobrevive_a_troca_da_pontuacao()
    {
        using var vm = Abrir(0);

        vm.FieldText = "Oi。\nTudo bem？";
        vm.Flush();

        Assert.Equal(@"Oi.\nTudo bem?", vm.Translation);
        Assert.Equal("Oi.\nTudo bem?", vm.FieldText);
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
    public void Edicao_so_grava_depois_de_400_ms_sem_digitar()
    {
        using var vm = Abrir(0);

        vm.Translation = "Oi";
        relogio.Advance(TimeSpan.FromMilliseconds(399));
        Assert.Equal(TranslationStatus.Untranslated, Gravada(raiz, 0).Status);

        relogio.Advance(TimeSpan.FromMilliseconds(1));

        var lida = Gravada(raiz, 0);
        Assert.Equal("Oi", lida.Translation);
        Assert.Equal(TranslationStatus.Translated, lida.Status);
        Assert.Equal(TranslationStatus.Translated, vm.Status);
    }

    [Fact]
    public void Nova_digitacao_reinicia_o_debounce_e_so_a_ultima_versao_e_gravada()
    {
        using var vm = Abrir(0);

        vm.Translation = "O";
        relogio.Advance(TimeSpan.FromMilliseconds(300));
        vm.Translation = "Oi";
        relogio.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(TranslationStatus.Untranslated, Gravada(raiz, 0).Status);

        relogio.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal("Oi", Gravada(raiz, 0).Translation);
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
    public void Abrir_a_linha_nao_grava_nada()
    {
        var caminho = ProjectFolder.Open(raiz).TextPath("yst00001");
        File.SetLastWriteTimeUtc(caminho, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        using var vm = Abrir(0);
        relogio.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(caminho));
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
    public void Aprovar_e_avancar_grava_o_texto_pendente_aprova_e_pede_a_proxima_linha()
    {
        using var vm = Abrir(0);
        vm.Translation = "Bom dia.";

        vm.ApproveAndNextCommand.Execute(null);

        var lida = Gravada(raiz, 0);
        Assert.Equal("Bom dia.", lida.Translation);
        Assert.Equal(TranslationStatus.Approved, lida.Status);
        Assert.Equal(TranslationStatus.Approved, vm.Status);
        Assert.True(vm.IsApproved);
        Assert.Equal(1, navegador.Next);
    }

    [Fact]
    public void Aprovar_nao_avanca()
    {
        using var vm = Abrir(1);

        vm.ApproveCommand.Execute(null);

        Assert.Equal(TranslationStatus.Approved, Gravada(raiz, 1).Status);
        Assert.Equal(0, navegador.Next);
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
    public void Editar_uma_linha_aprovada_volta_para_traduzida()
    {
        using var vm = Abrir(1);
        vm.ApproveCommand.Execute(null);

        vm.Translation = "Outra tradução";
        relogio.Advance(LineDetailViewModel.SaveDelay);

        Assert.Equal(TranslationStatus.Translated, vm.Status);
        Assert.Equal(TranslationStatus.Translated, Gravada(raiz, 1).Status);
    }

    [Fact]
    public void Apagar_a_traducao_deixa_a_linha_sem_traducao()
    {
        using var vm = Abrir(1);

        vm.Translation = "";
        relogio.Advance(LineDetailViewModel.SaveDelay);

        Assert.Equal(TranslationStatus.Untranslated, vm.Status);
        Assert.Null(Gravada(raiz, 1).Translation);
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
    public void FindMissingMarkers_conta_cada_ocorrencia()
    {
        Assert.Equal([@"\n"], LineDetailViewModel.FindMissingMarkers([@"\n", @"\n", @"\p"], [@"\n", @"\p"]));
        Assert.Empty(LineDetailViewModel.FindMissingMarkers([@"\n"], [@"\n", @"\n"]));
        Assert.Throws<ArgumentNullException>(() => LineDetailViewModel.FindMissingMarkers(null!, []));
        Assert.Throws<ArgumentNullException>(() => LineDetailViewModel.FindMissingMarkers([], null!));
    }

    [Fact]
    public void Id_que_nao_esta_no_arquivo_vira_mensagem_e_a_edicao_continua_pendente()
    {
        var unidade = Gravada(raiz, 0) with { Id = "yst00001:999999" };
        using var vm = Abrir(0, unidade);

        vm.Translation = "Oi";
        relogio.Advance(LineDetailViewModel.SaveDelay);

        Assert.Contains("Não foi possível gravar", vm.ErrorMessage);
        Assert.Contains("yst00001:999999", vm.ErrorMessage);
        vm.NextCommand.Execute(null);
        Assert.Equal(0, navegador.Next);
    }

    [Fact]
    public void Erro_some_quando_a_gravacao_seguinte_da_certo()
    {
        var caminho = ProjectFolder.Open(raiz).TextPath("yst00001");
        var original = File.ReadAllText(caminho);
        using var vm = Abrir(0);
        File.WriteAllText(caminho, "isto não é json");

        vm.Translation = "Oi";
        relogio.Advance(LineDetailViewModel.SaveDelay);
        Assert.NotNull(vm.ErrorMessage);

        File.WriteAllText(caminho, original);
        vm.NextCommand.Execute(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal("Oi", Gravada(raiz, 0).Translation);
        Assert.Equal(1, navegador.Next);
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
    public void Construtor_padrao_usa_o_contexto_da_thread_atual()
    {
        var unidade = Gravada(raiz, 0);

        using var vm = new LineDetailViewModel(unidade, "yst00001", new LineEditor(ProjectFolder.Open(raiz), relogio), navegador, relogio);
        vm.Translation = "Oi";

        // Com ou sem SynchronizationContext no teste, o Flush explícito garante a gravação.
        Assert.True(vm.Flush());
        Assert.Equal("Oi", Gravada(raiz, 0).Translation);
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

    [Fact]
    public void Edicao_feita_pelo_painel_aparece_no_status_do_projeto()
    {
        var antes = ProjectStatusReporter.Create(ProjectFolder.Open(raiz));
        Assert.Equal(2, antes.Total.Untranslated);
        Assert.Equal(0, antes.Total.Approved);

        using (var vm = Abrir(0))
        {
            vm.Translation = "Bom dia.";
            relogio.Advance(LineDetailViewModel.SaveDelay);
            vm.ApproveAndNextCommand.Execute(null);
        }

        // O mesmo caminho do `vnf status`.
        var depois = ProjectStatusReporter.Create(ProjectFolder.Open(raiz));
        Assert.Equal(1, depois.Total.Untranslated);
        Assert.Equal(1, depois.Total.Machine);
        Assert.Equal(1, depois.Total.Approved);
        Assert.Equal(1, depois.Files.Single().Counts.Approved);
    }

    private sealed class FakeNavigator : ILineNavigator
    {
        public int Next { get; private set; }

        public int Previous { get; private set; }

        public void SelectNext() => Next++;

        public void SelectPrevious() => Previous++;
    }
}
