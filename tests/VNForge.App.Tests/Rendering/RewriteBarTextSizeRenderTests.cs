using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Translation;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>As versões reescritas do RewriteBar seguem o tamanho do texto do Editor (#480): o texto da versão usa o token <c>Vnf.FontSize.Body</c>.</summary>
public sealed class RewriteBarTextSizeRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    [Theory]
    [InlineData(100, 14d)]
    [InlineData(125, 17.5)]
    [InlineData(150, 21d)]
    public async Task Texto_da_versao_reescrita_acompanha_o_tamanho_do_texto(int percent, double expected)
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Machine);
        ProjectFolder.Open(root).SaveText("yst00001", [new("yst00001:000000", TextUnitKind.Dialogue, "美咲", @"原文\p", @"Olá, tudo bem?\p", TranslationStatus.Machine)]);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

        await fixture.Session.Dispatch<int>(
            async () =>
            {
                var unit = ProjectFolder.Open(root).LoadText("yst00001")[0];
                using var vm = new LineDetailViewModel(unit, "yst00001", new LineEditor(ProjectFolder.Open(root), clock), new NullNavigator(), clock, action => action())
                {
                    Suggester = new LineSuggester(() => new FakeTranslationProvider()),
                    ProjectRoot = root,
                };
                var window = new Window
                {
                    Width = 600,
                    Height = 500,
                    RequestedThemeVariant = ThemeVariant.Dark,
                    Resources = TextAppearance.Build(percent),
                    Content = new RewriteBar { DataContext = vm },
                };
                try
                {
                    window.Show();
                    await vm.RewriteChips[0].Command.ExecuteAsync(null);
                    ScreenRenderer.Settle(window);

                    Assert.Equal(2, vm.RewriteOptions.Count);
                    foreach (var option in vm.RewriteOptions)
                    {
                        var block = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == option.Text);
                        Assert.True(block.IsEffectivelyVisible);
                        Assert.Equal(expected, block.FontSize);
                    }

                    ScreenRenderer.Save(window, RenderOutput.PathFor($"rewritebar-texto-{percent}.png"));
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private sealed class NullNavigator : ILineNavigator
    {
        public void SelectNext()
        {
        }

        public void SelectPrevious()
        {
        }
    }
}
