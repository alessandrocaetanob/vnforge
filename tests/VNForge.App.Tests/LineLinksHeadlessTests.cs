using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Rendering;
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

    // A sessão headless é uma só para o processo todo: uma janela deixada aberta por um teste continua viva (e escutando a preferência de
    // movimento e o foco) para os seguintes, então cada teste fecha a sua. O quadro é assentado com o tique do compositor, como nos outros
    // testes de clique, para o hit test do ponteiro enxergar o layout final e não o quadro anterior.
    private static Window ShowBuild(BuildViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 900, Content = new BuildView { DataContext = vm, TechnicalExpanded = true } };
        window.Show();
        ScreenRenderer.Settle(window);
        return window;
    }

    private static TextBlock LogLine(Window window)
    {
        var line = window.GetVisualDescendants().OfType<TextBlock>().First(t => LineLinks.GetSource(t) == Message);
        line.BringIntoView();
        ScreenRenderer.Settle(window);
        return line;
    }

    // Se o ponteiro não cair no texto da linha, o clique foi para outro controle e o erro diz qual.
    private static void ClickOn(Window window, TextBlock line, string id)
    {
        var point = CenterOf(line, window, id);
        var hit = window.InputHitTest(point);
        Assert.True(hit is Visual visual && (ReferenceEquals(visual, line) || line.IsVisualAncestorOf(visual)), $"O ponto {point} caiu em {hit?.GetType().Name ?? "nada"}, e não no texto da linha.");
        Click(window, point);
    }

    [Fact]
    public async Task Clicar_no_id_do_log_do_build_pede_a_linha()
    {
        var vm = new BuildViewModel(new BackgroundTaskService());
        vm.Log.Add(new BuildLogLine("20:19:23", Message, "26/10/2026"));
        LineRequest? requested = null;
        vm.LineRequested += (_, request) => requested = request;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = ShowBuild(vm);
                try
                {
                    ClickOn(window, LogLine(window), "yst00112:033792");

                    Assert.Equal(new LineRequest("yst00112", "yst00112:033792"), requested);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Clicar_fora_do_id_nao_pede_linha()
    {
        var vm = new BuildViewModel(new BackgroundTaskService());
        vm.Log.Add(new BuildLogLine("20:19:23", Message, "26/10/2026"));
        var requests = 0;
        vm.LineRequested += (_, _) => requests++;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = ShowBuild(vm);
                try
                {
                    ClickOn(window, LogLine(window), "Build falhou");

                    Assert.Equal(0, requests);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Id_seguido_de_dois_pontos_num_texto_solto_e_link_com_a_cor_de_destaque()
    {
        const string text = "yst00112:033792: tradução inválida";
        LineRequest? requested = null;

        await fixture.Session.Dispatch(
            () =>
            {
                var block = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
                LineLinks.SetCommand(block, new RelayCommand<LineRequest?>(r => requested = r));
                LineLinks.SetSource(block, text);
                var window = new Window { Width = 600, Height = 200, Content = block };
                window.Show();
                ScreenRenderer.Settle(window);
                try
                {
                    var link = Assert.IsType<Avalonia.Controls.Documents.Run>(block.Inlines![0]);
                    Assert.NotNull(link.Foreground);
                    var first = block.TextLayout.HitTestTextPosition(0);
                    var click = block.TranslatePoint(new Point(first.Left + 4, first.Top + (first.Height / 2)), window)!.Value;
                    Click(window, click);

                    Assert.Equal(new LineRequest("yst00112", "yst00112:033792"), requested);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }
}
