using Microsoft.Extensions.Time.Testing;
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

    public void Dispose() => pasta.Dispose();

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
