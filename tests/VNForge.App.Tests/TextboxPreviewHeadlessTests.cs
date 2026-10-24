using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.App.Views;
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

                // Desenha de verdade (placa, linhas, limite): uma exceção em Render apareceria aqui.
                using var imagem = new RenderTargetBitmap(new PixelSize((int)caixa.Bounds.Width, (int)caixa.Bounds.Height));
                imagem.Render(caixa);
                window.Close();
            },
            CancellationToken.None);
    }

    private static void Assentar(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
