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
