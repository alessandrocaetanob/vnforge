using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using VNForge.App.Services;
using VNForge.App.Tests.Rendering;
using VNForge.App.ViewModels;
using VNForge.App.Views;

namespace VNForge.App.Tests;

/// <summary>
/// A barra de tarefas do rodapé: título, barra de progresso e mensagem ficam lado a lado, sem um desenhar por cima do outro.
/// O FluentTheme dá um MinWidth à <see cref="ProgressBar"/> maior que os 120 px da célula; sem zerar, ela cobria o texto.
/// </summary>
public class TaskBarLayoutHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const double Tolerance = 0.5;

    public static TheoryData<int, double?> Cases => new()
    {
        { 960, null },
        { 960, 0.4 },
        { 2560, null },
        { 2560, 0.4 },
    };

    private static Rect BoundsIn(Control control, Visual ancestor)
    {
        var origin = control.TranslatePoint(default, ancestor) ?? default;
        return new Rect(origin, control.Bounds.Size);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Titulo_barra_e_mensagem_nao_se_sobrepoem(int width, double? progress)
    {
        var service = new BackgroundTaskService();
        var gate = new TaskCompletionSource();
        var reported = new TaskCompletionSource();
        var item = service.Start("Traduzindo o capítulo", (reporter, _) =>
        {
            reporter.Report(progress, "Traduzindo esta cena");
            reported.SetResult();
            return gate.Task;
        });
        await reported.Task;

        var problems = new List<string>();
        await fixture.Session.Dispatch(
            () =>
            {
                var view = new TaskBar { DataContext = new TaskBarViewModel(service) };
                var window = new Window { Width = width, Height = 200, Content = view };
                window.Show();
                ScreenRenderer.Settle(window);
                try
                {
                    var title = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Traduzindo o capítulo");
                    var message = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Traduzindo esta cena");
                    var bar = view.GetVisualDescendants().OfType<ProgressBar>().Single();
                    var cell = (Control)bar.GetVisualParent()!;

                    var titleBox = BoundsIn(title, view);
                    var barBox = BoundsIn(bar, view);
                    var cellBox = BoundsIn(cell, view);
                    var messageBox = BoundsIn(message, view);

                    if (bar.Bounds.Width > cell.Bounds.Width + Tolerance)
                    {
                        problems.Add($"a barra tem {bar.Bounds.Width:0.#} px e a célula {cell.Bounds.Width:0.#} px");
                    }

                    if (barBox.Left < titleBox.Right - Tolerance)
                    {
                        problems.Add($"a barra começa em {barBox.Left:0.#}, dentro do título, que termina em {titleBox.Right:0.#}");
                    }

                    if (barBox.Right > messageBox.Left + Tolerance)
                    {
                        problems.Add($"a barra termina em {barBox.Right:0.#}, dentro da mensagem, que começa em {messageBox.Left:0.#}");
                    }

                    if (cellBox.Intersects(titleBox.Deflate(Tolerance)) || cellBox.Intersects(messageBox.Deflate(Tolerance)))
                    {
                        problems.Add("a célula da barra cruza o título ou a mensagem");
                    }
                }
                finally
                {
                    window.Close();
                }
            },
            TestContext.Current.CancellationToken);

        gate.SetResult();
        await item.Completion;
        Assert.Empty(problems);
    }

    [Fact]
    public async Task Titulo_longo_corta_com_reticencias_e_nao_empurra_a_barra()
    {
        var service = new BackgroundTaskService();
        var gate = new TaskCompletionSource();
        var item = service.Start(new string('T', 200), (_, _) => gate.Task);
        var trimming = Avalonia.Media.TextTrimming.None;
        var maxWidth = double.PositiveInfinity;

        await fixture.Session.Dispatch(
            () =>
            {
                var view = new TaskBar { DataContext = new TaskBarViewModel(service) };
                var window = new Window { Width = 960, Height = 200, Content = view };
                window.Show();
                ScreenRenderer.Settle(window);
                try
                {
                    var title = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == item.Title);
                    trimming = title.TextTrimming;
                    maxWidth = title.Bounds.Width;
                }
                finally
                {
                    window.Close();
                }
            },
            TestContext.Current.CancellationToken);

        gate.SetResult();
        await item.Completion;
        Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, trimming);
        Assert.True(maxWidth <= 320, $"o título ocupou {maxWidth:0.#} px");
    }
}
