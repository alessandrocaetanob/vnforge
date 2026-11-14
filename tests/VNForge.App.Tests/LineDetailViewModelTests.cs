using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

[Trait("Risco", "R2")]
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
    public void Aprovar_com_erro_ao_gravar_nao_avanca()
    {
        var caminho = ProjectFolder.Open(raiz).TextPath("yst00001");
        using var vm = Abrir(1);
        File.Delete(caminho);

        vm.ApproveAndNextCommand.Execute(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Equal(0, navegador.Next);
    }

    private sealed class FakeNavigator : ILineNavigator
    {
        public int Next { get; private set; }

        public int Previous { get; private set; }

        public void SelectNext() => Next++;

        public void SelectPrevious() => Previous++;
    }
}
