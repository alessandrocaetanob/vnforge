using Microsoft.Extensions.Time.Testing;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A dúvida que a IA registra ao traduzir: o selo na grade, o filtro, o cartão do inspetor, aprovar e o modo revisão só das dúvidas.</summary>
public sealed class LineDoubtTests : IDisposable
{
    private static readonly TranslationDoubt Sujeito = new(DoubtKind.HiddenSubject, "O japonês não diz quem fala.");

    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public LineDoubtTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste");
        ProjectFolder.Open(raiz).SaveText("yst00001",
        [
            Fala(0, "Bom dia.\\p", TranslationStatus.Machine, Sujeito),
            Fala(1, "Chovia.\\p", TranslationStatus.Machine, null),
            Fala(2, "Aham.\\p", TranslationStatus.Machine, new TranslationDoubt(DoubtKind.Tone, "Ironia ou carinho?")),
            Fala(3, "Ei.\\p", TranslationStatus.Approved, null),
        ]);
    }

    public void Dispose() => pasta.Dispose();

    private static TextUnit Fala(int n, string traducao, TranslationStatus status, TranslationDoubt? duvida) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, "美咲", $"原文{n}\\p", traducao, status) { Doubt = duvida };

    private TextUnit Gravada(int n) => ProjectFolder.Open(raiz).LoadText("yst00001")[n];

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    [Fact]
    public async Task A_grade_marca_so_as_linhas_com_duvida_e_a_dica_traz_o_tipo_e_a_frase()
    {
        var vm = await Abrir();

        Assert.Equal([true, false, true, false], vm.Lines.Select(l => l.HasDoubt));
        Assert.Equal("Sujeito oculto: O japonês não diz quem fala.", vm.Lines[0].DoubtTip);
        Assert.Null(vm.Lines[1].DoubtTip);
    }

    [Fact]
    public async Task O_inspetor_mostra_o_cartao_da_duvida_e_aprovar_a_linha_o_encerra_na_grade_no_arquivo_e_no_filtro()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[0];
        var detail = vm.Detail!;
        var mudou = new List<string?>();
        detail.PropertyChanged += (_, e) => mudou.Add(e.PropertyName);

        Assert.True(detail.HasDoubt);
        Assert.Equal("Dúvida · Sujeito oculto", detail.DoubtTitle);
        Assert.Equal("O japonês não diz quem fala.", detail.DoubtNote);

        detail.ApproveCommand.Execute(null);

        Assert.False(detail.HasDoubt);
        Assert.Equal(string.Empty, detail.DoubtTitle);
        Assert.Contains(nameof(LineDetailViewModel.HasDoubt), mudou);
        Assert.Null(Gravada(0).Doubt);
        Assert.False(vm.Lines[0].HasDoubt);
        Assert.Equal(1, vm.Filters.Single(f => f.Filter == LineStatusFilter.Doubt).Count);
        Assert.Equal(1, vm.DoubtLines);
    }

    [Fact]
    public async Task Abrir_as_duvidas_fecha_o_leitor_de_cena()
    {
        var vm = await Abrir();
        vm.StartReading();
        Assert.True(vm.IsReadingScene);

        vm.StartDoubtReview();

        Assert.False(vm.IsReadingScene);
        Assert.True(vm.IsReviewing);
        Assert.Equal(2, vm.Review!.Total);
    }

    [Fact]
    public void A_fonte_Duvidas_nao_oferece_a_proxima_cena_e_tem_os_textos_da_cena()
    {
        var duvidas = ReviewSource.Doubts;

        Assert.False(duvidas.OffersNextScene);
        Assert.False(duvidas.SpansScenes);
        Assert.True(ReviewSource.OpenScene.OffersNextScene);
        Assert.Null(duvidas.Caption("x"));
        Assert.Equal("Revisão da cena concluída", duvidas.FinishedTitle("x"));
        Assert.Equal("Esta cena não tem linhas com dúvida da IA.", duvidas.EmptyText("x"));
    }
}
