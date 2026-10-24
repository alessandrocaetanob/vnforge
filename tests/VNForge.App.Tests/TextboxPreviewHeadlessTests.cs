using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O painel de detalhe real, com o <see cref="TextboxPreview"/> medido e desenhado a partir do perfil do projeto.</summary>
public class TextboxPreviewHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>, IDisposable
{
    private readonly TempFolder pasta = new();

    public void Dispose() => pasta.Dispose();

    [Fact]
    public async Task Painel_desenha_a_caixa_e_acompanha_a_edicao_da_traducao()
    {
        var raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Translated);
        File.WriteAllText(
            ProjectFolder.Open(raiz).FormattingPath,
            """{ "wrap": { "enabled": true, "maxWidth": 10, "maxLines": 2, "charWidth": "uniform" } }""");
        var vm = new EditorViewModel(clock: new FakeTimeProvider(), postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = 1400, Height = 900, Content = new EditorView { DataContext = vm } };
                window.Show();
                Assentar(window);

                var caixa = window.GetVisualDescendants().OfType<TextboxPreview>().Single();
                var estreita = caixa.DesiredSize;
                Assert.True(estreita.Width > 0 && estreita.Height > 0);
                Assert.Equal(["tradução 0"], caixa.Layout!.Boxes[0].Lines.Select(l => l.Text));

                vm.Detail!.Translation = "um dois tres quatro cinco";
                Assentar(window);

                Assert.Equal(["um dois", "tres", "quatro", "cinco"], caixa.Layout!.Boxes[0].Lines.Select(l => l.Text));
                Assert.True(caixa.DesiredSize.Height > estreita.Height);

                DesenharDeVerdade(caixa);
                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Voltar_a_tela_do_editor_depois_de_mudar_o_perfil_refaz_a_previa_da_linha_aberta()
    {
        var raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Translated);
        var arquivo = ProjectFolder.Open(raiz).FormattingPath;
        File.WriteAllText(arquivo, """{ "wrap": { "enabled": true, "maxWidth": 10, "maxLines": 2, "charWidth": "uniform" } }""");
        File.SetLastWriteTimeUtc(arquivo, new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc));
        var vm = new EditorViewModel(clock: new FakeTimeProvider(), postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        vm.Detail!.Translation = "um dois tres";
        Assert.Equal(2, vm.Detail.Preview!.Layout!.Boxes[0].Lines.Count);

        File.WriteAllText(arquivo, """{ "wrap": { "enabled": true, "maxWidth": 30, "maxLines": 2, "charWidth": "uniform" } }""");
        File.SetLastWriteTimeUtc(arquivo, new DateTime(2026, 10, 24, 12, 5, 0, DateTimeKind.Utc));
        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = 1400, Height = 900, Content = new EditorView { DataContext = vm } };
                window.Show();
                Assentar(window);

                Assert.Single(vm.Detail.Preview!.Layout!.Boxes[0].Lines);
                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Largura_segue_as_colunas_da_regua_e_nao_o_texto_cru()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var latino = Medir("abcd");
                var comMarcador = Medir("ab{VAR:nome}\\c");
                var japones = Medir("日本");
                var japonesComMarcador = Medir("日{EF:00}本");

                Assert.Equal(Medir("ab").Width, comMarcador.Width);
                Assert.Equal(latino.Width, japones.Width);
                Assert.Equal(latino.Width, japonesComMarcador.Width);
                Assert.True(latino.Width > Medir("ab").Width);
                Assert.Equal(latino.Height, japones.Height);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Render_percorre_marcador_glifo_largo_e_linha_em_excesso_sem_falhar()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var vm = new TextboxPreviewViewModel(
                    new TextUnit("a:1", TextUnitKind.Dialogue, "美咲", "原文", "x", TranslationStatus.Translated),
                    FormattingProfileSerializer.Parse("""{ "wrap": { "enabled": true, "maxWidth": 8, "maxLines": 1, "charWidth": "halfWidthLatin" } }"""),
                    "日本語 {VAR:nome} um dois tres\\cquatro");
                var caixa = new TextboxPreview { Layout = vm.Layout };
                caixa.Measure(Size.Infinity);
                caixa.Arrange(new Rect(caixa.DesiredSize));

                Assert.Contains(vm.Layout!.Boxes[0].Lines, l => l.Overflow);
                DesenharDeVerdade(caixa);
            },
            CancellationToken.None);
    }

    private static Size Medir(string traducao)
    {
        var vm = new TextboxPreviewViewModel(
            new TextUnit("a:1", TextUnitKind.Dialogue, null, "原文", "x", TranslationStatus.Translated),
            FormattingProfile.Default,
            traducao);
        var caixa = new TextboxPreview { Layout = vm.Layout };
        caixa.Measure(Size.Infinity);
        return caixa.DesiredSize;
    }

    /// <summary>Chama <c>Render</c> direto num contexto de desenho: uma exceção no desenho apareceria aqui.</summary>
    private static void DesenharDeVerdade(TextboxPreview caixa)
    {
        using var imagem = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)caixa.DesiredSize.Width), Math.Max(1, (int)caixa.DesiredSize.Height)));
        using var contexto = imagem.CreateDrawingContext();
        caixa.Render(contexto);
    }

    private static void Assentar(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
