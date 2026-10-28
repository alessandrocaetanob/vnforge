using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class TextboxPreviewTests : IDisposable
{
    private const string Perfil = """{ "wrap": { "enabled": true, "maxWidth": 10, "maxLines": 2, "charWidth": "uniform" } }""";

    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public TextboxPreviewTests() =>
        raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Translated, TranslationStatus.Untranslated);

    public void Dispose()
    {
        pasta.Dispose();
        GC.SuppressFinalize(this);
    }

    private static TextUnit Unidade(string? falante = null) =>
        new("yst00001:000000", TextUnitKind.Dialogue, falante, "原文", "x", TranslationStatus.Translated);

    private static FormattingProfile Parse(string json) => FormattingProfileSerializer.Parse(json);

    private void GravarPerfil(string json) => File.WriteAllText(ProjectFolder.Open(raiz).FormattingPath, json);

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    [Fact]
    public void Quebra_nos_mesmos_pontos_que_o_vnf_preview_da_linha_gravada()
    {
        const string Texto = "um dois tres quatro cinco\\pseis sete oito";
        GravarPerfil(Perfil);
        var projeto = ProjectFolder.Open(raiz);
        projeto.SaveText("yst00001", [new TextUnit("yst00001:000000", TextUnitKind.Dialogue, null, "原文", Texto, TranslationStatus.Translated)]);
        var cli = LinePreviewer.Create(projeto, "yst00001:000000");

        var vm = new TextboxPreviewViewModel(Unidade(), projeto.LoadFormatting(), Texto);

        var linhas = vm.Layout!.Boxes.Select(b => b.Lines.Select(l => l.Text).ToArray()).ToArray();
        Assert.Equal(cli.Boxes.Select(b => b.Select(l => l.Text).ToArray()), linhas);
        Assert.Equal(cli.Columns, vm.Layout.Boxes.Select(b => (IReadOnlyList<int>)[.. b.Lines.Select(l => l.Columns)]));
        Assert.Equal(cli.Text, string.Join("\\p", linhas.Select(b => string.Join("\\n", b))));
    }

    [Fact]
    public void Update_recalcula_a_quebra_com_o_texto_em_edicao()
    {
        var vm = new TextboxPreviewViewModel(Unidade(), Parse(Perfil), "um dois");
        var mudancas = new List<string?>();
        vm.PropertyChanged += (_, e) => mudancas.Add(e.PropertyName);

        vm.Update("um dois tres");

        Assert.Equal(["um dois", "tres"], vm.Layout!.Boxes[0].Lines.Select(l => l.Text));
        Assert.Contains(nameof(TextboxPreviewViewModel.Layout), mudancas);
    }

    [Fact]
    public void Linhas_alem_do_limite_do_perfil_ficam_marcadas_e_o_aviso_aparece()
    {
        var vm = new TextboxPreviewViewModel(Unidade(), Parse(Perfil), "um dois tres quatro cinco seis");

        var linhas = vm.Layout!.Boxes[0].Lines;

        Assert.Equal(["um dois", "tres", "quatro", "cinco seis"], linhas.Select(l => l.Text));
        Assert.Equal([false, false, true, true], linhas.Select(l => l.Overflow));
        Assert.Equal(2, vm.Layout.MaxLines);
        Assert.Equal(10, vm.Layout.Columns);
        var aviso = Assert.Single(vm.Warnings);
        Assert.Contains("caixa 1", aviso);
        Assert.True(vm.HasWarnings);
    }

    [Fact]
    public void Quebra_desligada_no_perfil_mede_a_linha_mais_larga_e_diz_isso()
    {
        var vm = new TextboxPreviewViewModel(Unidade(), Parse("{}"), "uma frase longa\\ncurta");

        Assert.Equal(15, vm.Layout!.Columns);
        Assert.Contains("quebra desligada", vm.Summary);
    }

    [Fact]
    public void Abertura_invisivel_da_cp1252_ocupa_uma_coluna_na_primeira_linha_da_fala_com_falante()
    {
        var marks = SpeakerBoxMarks.For(new Cp1252TextEncoder());

        var vm = new TextboxPreviewViewModel(Unidade("Misaki"), Parse(Perfil), "um dois tres", marks);

        var linhas = vm.Layout!.Boxes[0].Lines;
        Assert.Equal(["\u00A0um dois", "tres"], linhas.Select(l => l.Text));
        Assert.Equal([8, 4], linhas.Select(l => l.Columns));
        Assert.Equal(marks, vm.Marks);
    }

    [Fact]
    public void Com_guillemets_o_par_entra_na_primeira_e_na_ultima_linha()
    {
        var vm = new TextboxPreviewViewModel(Unidade("Misaki"), Parse(Perfil), "um dois tres", SpeakerBoxMarks.For(new Cp1252TextEncoder(SpeakerDelimiterStyle.Guillemets)));

        Assert.Equal(["«um dois", "tres»"], vm.Layout!.Boxes[0].Lines.Select(l => l.Text));
    }

    [Fact]
    public void Narracao_e_a_previa_sem_marcas_medem_so_a_traducao()
    {
        var marks = SpeakerBoxMarks.For(new Cp1252TextEncoder());

        Assert.Equal([7, 4], new TextboxPreviewViewModel(Unidade(), Parse(Perfil), "um dois tres", marks).Layout!.Boxes[0].Lines.Select(l => l.Columns));
        Assert.Equal([7, 4], new TextboxPreviewViewModel(Unidade("Misaki"), Parse(Perfil), "um dois tres").Layout!.Boxes[0].Lines.Select(l => l.Columns));
    }

    [Fact]
    public void Update_mantem_as_marcas_da_fala()
    {
        var vm = new TextboxPreviewViewModel(Unidade("Misaki"), Parse(Perfil), "um", SpeakerBoxMarks.For(new Cp1252TextEncoder()));

        vm.Update("um dois");

        Assert.Equal([8], vm.Layout!.Boxes[0].Lines.Select(l => l.Columns));
    }

    [Fact]
    public void O_cache_das_marcas_le_o_vnforge_json_e_so_o_relê_quando_ele_muda()
    {
        var arquivo = Path.Combine(raiz, ProjectFolder.ConfigFileName);
        var projeto = ProjectFolder.Open(raiz);
        var cache = new SpeakerMarksCache();
        Assert.Equal(SpeakerBoxMarks.None, cache.Get(raiz));

        projeto.SaveConfig(projeto.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        File.SetLastWriteTimeUtc(arquivo, new DateTime(2026, 10, 24, 12, 5, 0, DateTimeKind.Utc));
        Assert.Equal(new SpeakerBoxMarks("\u00A0", string.Empty), cache.Get(raiz));

        projeto.SaveConfig(projeto.LoadConfig() with { Style = StyleConfig.Default with { SpeakerDelimiter = SpeakerDelimiterStyle.Guillemets } });
        File.SetLastWriteTimeUtc(arquivo, new DateTime(2026, 10, 24, 12, 6, 0, DateTimeKind.Utc));
        Assert.Equal(new SpeakerBoxMarks("«", "»"), cache.Get(raiz));

        File.WriteAllText(arquivo, "{ isto nao e json");
        File.SetLastWriteTimeUtc(arquivo, new DateTime(2026, 10, 24, 12, 7, 0, DateTimeKind.Utc));
        Assert.Equal(SpeakerBoxMarks.None, cache.Get(raiz));
    }

    [Fact]
    public async Task Editor_com_cp1252_mede_a_abertura_e_voltar_dos_ajustes_com_o_estilo_mudado_refaz_a_previa()
    {
        GravarPerfil(Perfil);
        var projeto = ProjectFolder.Open(raiz);
        var arquivo = Path.Combine(raiz, ProjectFolder.ConfigFileName);
        projeto.SaveConfig(projeto.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        File.SetLastWriteTimeUtc(arquivo, new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc));
        projeto.SaveText("yst00001", [new TextUnit("yst00001:000000", TextUnitKind.Dialogue, "Misaki", "原文", "um dois", TranslationStatus.Translated)]);
        var vm = await Abrir();
        var painel = vm.Detail!;
        Assert.Equal([8], painel.Preview!.Layout!.Boxes[0].Lines.Select(l => l.Columns));

        projeto.SaveConfig(projeto.LoadConfig() with { Style = StyleConfig.Default with { SpeakerDelimiter = SpeakerDelimiterStyle.Guillemets } });
        File.SetLastWriteTimeUtc(arquivo, new DateTime(2026, 10, 24, 12, 5, 0, DateTimeKind.Utc));
        vm.RefreshPreview();

        Assert.Same(painel, vm.Detail);
        Assert.Equal(["«um dois»"], painel.Preview!.Layout!.Boxes[0].Lines.Select(l => l.Text));
    }

    [Fact]
    public void Falante_vai_para_a_placa_e_o_resumo_conta_linhas_e_colunas()
    {
        var vm = new TextboxPreviewViewModel(Unidade("美咲"), Parse(Perfil), "um dois tres");

        Assert.Equal("美咲", vm.Layout!.Speaker);
        Assert.Equal("2 linhas · largura máxima 10 colunas", vm.Summary);
    }

    [Fact]
    public void Sem_traducao_nao_ha_previa_e_a_mensagem_orienta()
    {
        var vm = new TextboxPreviewViewModel(Unidade(), Parse(Perfil), "  ");

        Assert.Null(vm.Layout);
        Assert.False(vm.HasLayout);
        Assert.Contains("Escreva a tradução", vm.Summary);
    }

    [Fact]
    public void Marcador_invalido_mostra_o_problema_em_vez_de_quebrar_a_tela()
    {
        var vm = new TextboxPreviewViewModel(Unidade(), Parse(Perfil), "oi \\x");

        Assert.Null(vm.Layout);
        Assert.NotNull(vm.Problem);
        Assert.Contains("\\x", vm.Problem);

        vm.Update("oi");

        Assert.NotNull(vm.Layout);
        Assert.Null(vm.Problem);
    }

    [Fact]
    public void Linha_que_o_build_nao_formata_mostra_o_aviso_do_vnf_preview_e_nao_marca_excesso()
    {
        var escolha = new TextUnit("yst00001:000000", TextUnitKind.Choice, null, "原文", "x", TranslationStatus.Translated);

        var vm = new TextboxPreviewViewModel(escolha, Parse(Perfil), "um dois tres quatro cinco seis");

        Assert.Equal(LinePreview.NotFormattedNotice, vm.Notice);
        Assert.All(vm.Layout!.Boxes.SelectMany(b => b.Lines), l => Assert.False(l.Overflow));
        Assert.Null(new TextboxPreviewViewModel(Unidade(), Parse(Perfil), "um dois").Notice);
    }

    [Fact]
    public void Linhas_levam_os_segmentos_da_regua_de_colunas()
    {
        var vm = new TextboxPreviewViewModel(Unidade(), Parse(Perfil), "a{VAR:x}日");

        var segmentos = vm.Layout!.Boxes[0].Lines[0].Segments;

        Assert.Equal([("a", 0, false), ("{VAR:x}", 1, true), ("日", 1, false)], segmentos.Select(s => (s.Text, s.Column, s.IsMarker)));
        Assert.Equal(2, vm.Layout.Boxes[0].Lines[0].Columns);
    }

    [Fact]
    public void Cache_reaproveita_o_perfil_ate_o_arquivo_mudar()
    {
        GravarPerfil(Perfil);
        var arquivo = ProjectFolder.Open(raiz).FormattingPath;
        File.SetLastWriteTimeUtc(arquivo, new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc));
        var cache = new FormattingProfileCache();

        var primeiro = cache.Get(raiz);

        Assert.Same(primeiro, cache.Get(raiz));
        GravarPerfil("""{ "wrap": { "enabled": true, "maxWidth": 20, "charWidth": "uniform" } }""");
        File.SetLastWriteTimeUtc(arquivo, new DateTime(2026, 10, 24, 12, 5, 0, DateTimeKind.Utc));
        var segundo = cache.Get(raiz);
        Assert.NotSame(primeiro, segundo);
        Assert.Equal(20, segundo!.Wrap.MaxWidth);
        GravarPerfil("{ isto nao e json");
        File.SetLastWriteTimeUtc(arquivo, new DateTime(2026, 10, 24, 12, 6, 0, DateTimeKind.Utc));
        Assert.Null(cache.Get(raiz));
        File.Delete(arquivo);
        Assert.Same(FormattingProfile.Default, cache.Get(raiz));
    }

    [Fact]
    public async Task Voltar_ao_editor_com_o_perfil_mudado_refaz_a_previa_da_mesma_linha()
    {
        GravarPerfil(Perfil);
        var arquivo = ProjectFolder.Open(raiz).FormattingPath;
        File.SetLastWriteTimeUtc(arquivo, new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc));
        var vm = await Abrir();
        var painel = vm.Detail!;
        painel.Translation = "um dois tres";
        Assert.Equal(["um dois", "tres"], painel.Preview!.Layout!.Boxes[0].Lines.Select(l => l.Text));

        GravarPerfil("""{ "wrap": { "enabled": true, "maxWidth": 20, "maxLines": 2, "charWidth": "uniform" } }""");
        File.SetLastWriteTimeUtc(arquivo, new DateTime(2026, 10, 24, 12, 5, 0, DateTimeKind.Utc));
        vm.RefreshPreview();

        Assert.Same(painel, vm.Detail);
        Assert.Equal(["um dois tres"], painel.Preview!.Layout!.Boxes[0].Lines.Select(l => l.Text));

        GravarPerfil("{ isto nao e json");
        File.SetLastWriteTimeUtc(arquivo, new DateTime(2026, 10, 24, 12, 6, 0, DateTimeKind.Utc));
        vm.RefreshPreview();
        Assert.Null(painel.Preview);
    }

    [Fact]
    public async Task Painel_do_editor_traz_a_previa_e_recalcula_enquanto_a_traducao_e_editada()
    {
        GravarPerfil(Perfil);
        var vm = await Abrir();

        var painel = vm.Detail!;
        Assert.NotNull(painel.Preview);
        Assert.Equal(["tradução 0"], painel.Preview.Layout!.Boxes[0].Lines.Select(l => l.Text));

        painel.Translation = "um dois tres";

        Assert.Equal(["um dois", "tres"], painel.Preview.Layout!.Boxes[0].Lines.Select(l => l.Text));
    }

    [Fact]
    public async Task Painel_de_linha_sem_traducao_abre_a_previa_sem_texto()
    {
        GravarPerfil(Perfil);
        var vm = await Abrir();

        vm.SelectedLine = vm.Lines[1];

        Assert.False(vm.Detail!.Preview!.HasLayout);
    }

    [Fact]
    public async Task Perfil_ilegivel_deixa_o_painel_sem_previa()
    {
        GravarPerfil(Perfil);
        var vm = await Abrir();
        GravarPerfil("{ isto nao e json");

        vm.SelectedLine = vm.Lines[1];

        Assert.NotNull(vm.Detail);
        Assert.Null(vm.Detail.Preview);
    }
}
