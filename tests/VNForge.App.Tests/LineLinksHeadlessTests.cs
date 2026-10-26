using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;

namespace VNForge.App.Tests;

/// <summary>O clique de verdade no id de uma linha, dentro do log do build e num texto solto.</summary>
public class LineLinksHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const string Message = "Build falhou: A tradução da unidade 'yst00112:033792' não pode ser gravada.";

    private static Point CenterOf(TextBlock block, Window window, string id)
    {
        var start = Message.IndexOf(id, StringComparison.Ordinal);
        var first = block.TextLayout.HitTestTextPosition(start);
        var last = block.TextLayout.HitTestTextPosition(start + id.Length - 1);
        var middle = new Point((first.Left + last.Right) / 2, first.Top + (first.Height / 2));
        return block.TranslatePoint(middle + new Point(block.Padding.Left, block.Padding.Top), window)!.Value;
    }

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    [Fact]
    public async Task Clicar_no_id_do_log_do_build_pede_a_linha()
    {
        var vm = new BuildViewModel(new BackgroundTaskService());
        vm.Log.Add(new BuildLogLine("20:19:23", Message));
        LineRequest? requested = null;
        vm.LineRequested += (_, request) => requested = request;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = 1200, Height = 900, Content = new BuildView { DataContext = vm, TechnicalExpanded = true } };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var line = window.GetVisualDescendants().OfType<TextBlock>().First(t => LineLinks.GetSource(t) == Message);
                Click(window, CenterOf(line, window, "yst00112:033792"));

                Assert.Equal(new LineRequest("yst00112", "yst00112:033792"), requested);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Clicar_fora_do_id_nao_pede_linha()
    {
        var vm = new BuildViewModel(new BackgroundTaskService());
        vm.Log.Add(new BuildLogLine("20:19:23", Message));
        var requests = 0;
        vm.LineRequested += (_, _) => requests++;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = 1200, Height = 900, Content = new BuildView { DataContext = vm, TechnicalExpanded = true } };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var line = window.GetVisualDescendants().OfType<TextBlock>().First(t => LineLinks.GetSource(t) == Message);
                Click(window, CenterOf(line, window, "Build falhou"));

                Assert.Equal(0, requests);
            },
            CancellationToken.None);
    }
}
