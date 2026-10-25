using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public sealed class ConferenceFitTests : IDisposable
{
    private const string Id = "yst00001:000001";

    private readonly TempFolder pasta = new();
    private readonly ProjectFolder folder;

    public ConferenceFitTests()
    {
        folder = ProjectFolder.Open(SampleProjects.Create(pasta.SubFolder("vn"), "Minha VN", TranslationStatus.Translated));
        folder.SaveFormatting(FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: 40, MaxLines: 3, CharWidth: CharWidthMode.Uniform) });
    }

    public void Dispose() => pasta.Dispose();

    private static string Palavras(int quantas) => string.Join(' ', Enumerable.Repeat("aaaaaaaaa", quantas));

    private static TextUnit Linha(string traducao, int n = 0) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, null, $"原文{n}", traducao, TranslationStatus.Translated);

    private string Gravada(string id = "yst00001:000000") => folder.LoadText("yst00001").Single(u => u.Id == id).Translation!;

    private async Task<ConferenceViewModel> Conferir(ILineShortener? encurtador)
    {
        folder.SaveText("yst00001", [Linha(Palavras(16)), Linha("cabe bem", 1)]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(folder.Root), Shortener = encurtador };
        await vm.RefreshAsync();
        vm.SelectedRow = vm.Rows.Single(r => r.RuleId == BoxLineLimitRule.RuleId);
        return vm;
    }

    private sealed class Encurtador(string resposta) : ILineShortener
    {
        public List<string> Pedidos { get; } = [];

        public Task<string> ShortenAsync(TextUnit unit, string current, WrapRule box, CancellationToken ct)
        {
            Pedidos.Add(current);
            return Task.FromResult(resposta);
        }
    }

    [Fact]
    public async Task Problema_de_caixa_selecionado_mostra_o_aviso_com_as_duas_saidas()
    {
        var vm = await Conferir(new Encurtador("curta"));

        var aviso = Assert.IsType<FitWarningViewModel>(vm.SelectedFit);
        Assert.True(vm.HasSelectedFit);
        Assert.True(aviso.IsVisible);
        Assert.Equal("Dividir em 2 páginas", aviso.SplitText);
        Assert.True(aviso.HasShortener);
    }

    [Fact]
    public async Task Outro_problema_selecionado_nao_mostra_o_aviso()
    {
        var vm = await Conferir(null);
        folder.SaveText("yst00001", [Linha("olá  mundo")]);
        await vm.RefreshAsync();

        vm.SelectedRow = vm.Rows[0];

        Assert.Null(vm.SelectedFit);
        Assert.False(vm.HasSelectedFit);
    }

    [Fact]
    public async Task Dividir_grava_a_traducao_com_a_virada_de_pagina_e_reconfere()
    {
        var vm = await Conferir(null);
        var untouched = Gravada("yst00001:000001");

        await vm.SelectedFit!.SplitCommand.ExecuteAsync(null);

        Assert.Equal($@"{Palavras(12)}\p{Palavras(4)}", Gravada());
        Assert.Equal(untouched, Gravada("yst00001:000001"));
        Assert.DoesNotContain(vm.Rows, r => r.RuleId == BoxLineLimitRule.RuleId);
        Assert.Equal("Fala dividida em páginas.", vm.NoticeMessage);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsFixing);
    }

    [Fact]
    public async Task Dividir_avisa_o_editor_que_o_texto_mudou()
    {
        var vm = await Conferir(null);
        var changed = 0;
        vm.TextsChanged += (_, _) => changed++;

        await vm.SelectedFit!.SplitCommand.ExecuteAsync(null);

        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Depois_de_dividir_a_pagina_a_mais_aparece_como_aviso_e_nao_como_erro()
    {
        var vm = await Conferir(null);

        await vm.SelectedFit!.SplitCommand.ExecuteAsync(null);

        var extra = Assert.Single(vm.Rows, r => r.RuleId == ExtraPageQaRule.RuleId);
        Assert.False(extra.IsError);
        Assert.DoesNotContain(vm.Rows, r => r.RuleId == "marcador-diferente");
    }

    [Fact]
    public async Task Encurtar_grava_a_versao_curta_da_ia_e_reconfere()
    {
        var encurtador = new Encurtador("Cabe na caixa.");
        var vm = await Conferir(encurtador);

        await vm.SelectedFit!.ShortenCommand.ExecuteAsync(null);

        Assert.Equal(Palavras(16), Assert.Single(encurtador.Pedidos));
        Assert.Equal("Cabe na caixa.", Gravada());
        Assert.DoesNotContain(vm.Rows, r => r.RuleId == BoxLineLimitRule.RuleId);
        Assert.Equal("Fala encurtada pela IA.", vm.NoticeMessage);
    }

    [Fact]
    public async Task Encurtar_com_a_ia_recusando_deixa_a_traducao_e_mostra_o_motivo_no_aviso()
    {
        var recusa = new LineSuggester(() => new FakeTranslationProvider(_ => string.Empty));
        var vm = await Conferir(recusa);
        var aviso = vm.SelectedFit!;

        await aviso.ShortenCommand.ExecuteAsync(null);

        Assert.Equal(Palavras(16), Gravada());
        Assert.True(aviso.HasError);
        Assert.Contains(vm.Rows, r => r.RuleId == BoxLineLimitRule.RuleId);
    }

    [Fact]
    public async Task Trocar_de_problema_descarta_o_aviso_anterior()
    {
        var vm = await Conferir(null);
        var primeiro = vm.SelectedFit;

        vm.SelectedRow = null;

        Assert.NotNull(primeiro);
        Assert.Null(vm.SelectedFit);
    }

    [Fact]
    public async Task Falha_ao_gravar_mostra_o_erro_e_nao_perde_a_lista()
    {
        var vm = await Conferir(null);
        var aviso = vm.SelectedFit!;
        File.Delete(Path.Combine(folder.Root, "text", "yst00001.json"));
        Directory.CreateDirectory(Path.Combine(folder.Root, "text", "yst00001.json"));

        await aviso.SplitCommand.ExecuteAsync(null);

        Assert.StartsWith("Não foi possível gravar a nova tradução", vm.ErrorMessage);
        Assert.False(vm.IsFixing);
    }
}
