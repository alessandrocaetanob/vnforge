using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using VNForge.App.Services;
using VNForge.App.Tests.Rendering;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;

namespace VNForge.App.Tests;

/// <summary>
/// A barra de tarefas do rodapé: título, barra de progresso e mensagem ficam lado a lado, sem um desenhar por cima do outro.
/// O FluentTheme dá à <see cref="ProgressBar"/> um MinWidth maior que os 120 px da célula; sem zerar, ela cobria o texto.
/// </summary>
public class TaskBarLayoutHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const double Tolerance = 0.5;
    private const double TitleMaxWidth = 240;

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

    /// <summary>Inicia uma tarefa que já relatou progresso e mensagem e fica em andamento até o <paramref name="gate"/> abrir.</summary>
    private static async Task<BackgroundTaskItem> StartReported(BackgroundTaskService service, string title, double? progress, TaskCompletionSource gate)
    {
        var reported = new TaskCompletionSource();
        var item = service.Start(title, (reporter, _) =>
        {
            reporter.Report(progress, "Traduzindo esta cena");
            reported.SetResult();
            return gate.Task;
        });
        await reported.Task;
        return item;
    }

    private static List<string> OverlapsIn(Visual root)
    {
        var problems = new List<string>();
        var title = root.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Traduzindo o capítulo");
        var message = root.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Traduzindo esta cena");
        var bar = root.GetVisualDescendants().OfType<ProgressBar>().Single();
        var cell = (Control)bar.GetVisualParent()!;

        var titleBox = BoundsIn(title, root);
        var barBox = BoundsIn(bar, root);
        var cellBox = BoundsIn(cell, root);
        var messageBox = BoundsIn(message, root);

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

        return problems;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Titulo_barra_e_mensagem_nao_se_sobrepoem(int width, double? progress)
    {
        var service = new BackgroundTaskService();
        var gate = new TaskCompletionSource();
        var item = await StartReported(service, "Traduzindo o capítulo", progress, gate);
        var problems = new List<string>();

        try
        {
            await fixture.Session.Dispatch(
                () =>
                {
                    var view = new TaskBar { DataContext = new TaskBarViewModel(service) };
                    var window = new Window { Width = width, Height = 200, Content = view };
                    window.Show();
                    ScreenRenderer.Settle(window);
                    try
                    {
                        problems.AddRange(OverlapsIn(view));
                    }
                    finally
                    {
                        window.Close();
                    }
                },
                TestContext.Current.CancellationToken);
        }
        finally
        {
            gate.SetResult();
            await item.Completion;
        }

        Assert.Empty(problems);
    }

    /// <summary>A barra de status da janela principal: a tarefa não passa por cima da versão e dos avisos da direita.</summary>
    [Theory]
    [InlineData(960)]
    [InlineData(2560)]
    public async Task Na_janela_principal_a_tarefa_cabe_na_barra_de_status(int width)
    {
        using var temp = new TempFolder();
        var service = new BackgroundTaskService();
        var vm = new MainWindowViewModel(
            new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), TimeProvider.System),
            taskService: service);
        var gate = new TaskCompletionSource();
        var item = await StartReported(service, "Traduzindo o capítulo", 0.4, gate);
        var problems = new List<string>();

        try
        {
            await fixture.Session.Dispatch(
                () =>
                {
                    var window = new MainWindow { DataContext = vm, Width = width, Height = 900 };
                    window.Show();
                    ScreenRenderer.Settle(window);
                    try
                    {
                        var view = window.GetVisualDescendants().OfType<TaskBar>().Single();
                        problems.AddRange(OverlapsIn(view));

                        var version = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == vm.AppVersionText);
                        var taskRight = BoundsIn(view, window).Right;
                        var versionLeft = BoundsIn(version, window).Left;
                        if (taskRight > versionLeft + Tolerance)
                        {
                            problems.Add($"a tarefa termina em {taskRight:0.#}, depois do início da versão em {versionLeft:0.#}");
                        }
                    }
                    finally
                    {
                        window.Close();
                    }
                },
                TestContext.Current.CancellationToken);
        }
        finally
        {
            gate.SetResult();
            await item.Completion;
        }

        Assert.Empty(problems);
    }

    [Fact]
    public async Task Titulo_longo_corta_com_reticencias_e_nao_empurra_a_barra()
    {
        var service = new BackgroundTaskService();
        var gate = new TaskCompletionSource();
        var item = service.Start(new string('T', 200), (_, _) => gate.Task);
        var trimming = TextTrimming.None;
        var measured = double.PositiveInfinity;

        try
        {
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
                        measured = title.Bounds.Width;
                    }
                    finally
                    {
                        window.Close();
                    }
                },
                TestContext.Current.CancellationToken);
        }
        finally
        {
            gate.SetResult();
            await item.Completion;
        }

        Assert.Equal(TextTrimming.CharacterEllipsis, trimming);
        Assert.True(measured <= TitleMaxWidth + Tolerance, $"o título ocupou {measured:0.#} px");
    }
}
