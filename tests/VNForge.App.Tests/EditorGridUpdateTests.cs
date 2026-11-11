using Microsoft.Extensions.Time.Testing;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A linha da grade acompanha o que o painel de detalhe grava (issue #231).</summary>
public class EditorGridUpdateTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public EditorGridUpdateTests() =>
        raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Untranslated, TranslationStatus.Machine, TranslationStatus.Untranslated);

    public void Dispose() => pasta.Dispose();

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    private TextUnit Gravada(int n) => ProjectFolder.Open(raiz).LoadText("yst00001")[n];

    [Fact]
    public async Task Nao_traduzir_atualiza_a_linha_da_grade_o_aviso_e_o_filtro()
    {
        var vm = await Abrir();
        var linha = vm.Lines[0];
        Assert.True(linha.HasWarning);
        var pendentes = vm.Filters.Single(f => f.Filter == LineStatusFilter.Pending).Count;

        vm.Detail!.KeepCommand.Execute(null);

        Assert.Equal(TranslationStatus.Keep, linha.Status);
        Assert.Equal("Não traduzir", linha.StatusLabel);
        Assert.Equal(LineRow.KeepLabel, linha.TranslationLabel);
        Assert.False(linha.HasWarning);
        Assert.Equal(TranslationStatus.Keep, Gravada(0).Status);
        Assert.Equal(1, vm.Filters.Single(f => f.Filter == LineStatusFilter.Keep).Count);
        Assert.Equal(pendentes - 1, vm.Filters.Single(f => f.Filter == LineStatusFilter.Pending).Count);
    }
}
