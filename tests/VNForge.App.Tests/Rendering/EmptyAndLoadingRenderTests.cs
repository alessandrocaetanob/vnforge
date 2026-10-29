using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// Renderiza para PNG as páginas em estado vazio e em carregamento (início sem projeto, painel sem projeto, conferência limpa, editor sem linhas,
/// editor e painel lendo), a 1440×1000 no tema escuro, para o antes e depois do PR da #249.
/// </summary>
public sealed class EmptyAndLoadingRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private Task OnUi(Func<Task> test) => fixture.Session.Dispatch<int>(
        async () =>
        {
            await test();
            return 0;
        },
        TestContext.Current.CancellationToken);

    // O quadro é gravado e relido numa pasta própria da execução: o caminho de RenderOutput é fixo, e duas execuções ao mesmo tempo (dois
    // dotnet test, ou um worktree ao lado) gravariam o mesmo arquivo enquanto a outra o lia. A cópia para RenderOutput é só a publicação do PNG.
    private static void Render(Control page, string name)
    {
        using var temp = new TempFolder();
        var fileName = ScreenRenderer.FileName(name, ThemeVariant.Dark);
        var path = Path.Combine(temp.Path, fileName);
        ScreenRenderer.Render(page, ThemeVariant.Dark, path);
        using (var png = new Bitmap(path))
        {
            Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
        }

        Publish(path, RenderOutput.PathFor(fileName));
    }

    private static void Publish(string source, string destination)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
        catch (IOException)
        {
            // Outra execução está gravando o mesmo PNG: o teste já conferiu o quadro dele.
        }
    }

    [Fact]
    public Task Inicio_sem_projeto_recente() => OnUi(() =>
    {
        using var config = new TempFolder();
        var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(null), TimeProvider.System);
        Render(new StartView { DataContext = start }, "inicio-vazio");
        return Task.CompletedTask;
    });

    [Fact]
    public Task Painel_sem_projeto() => OnUi(() =>
    {
        Render(new DashboardView { DataContext = new DashboardViewModel() }, "painel-vazio");
        return Task.CompletedTask;
    });

    [Fact]
    public Task Painel_lendo() => OnUi(async () =>
    {
        var gate = new ManualResetEventSlim();
        var dashboard = new DashboardViewModel(_ =>
        {
            gate.Wait();
            throw new IOException("liberado");
        })
        {
            Project = EditorFixtures.Summary(),
        };
        var reload = dashboard.ReloadAsync();
        try
        {
            Render(new DashboardView { DataContext = dashboard }, "painel-lendo");
        }
        finally
        {
            gate.Set();
            await reload;
        }
    });

    [Fact]
    public Task Conferencia_sem_problemas() => OnUi(async () =>
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.Path, "Teste", TranslationStatus.Approved, TranslationStatus.Approved);
        var conference = new ConferenceViewModel { Project = EditorFixtures.Summary("Teste", root) };
        await conference.RefreshAsync();
        Render(new ConferenceView { DataContext = conference }, "conferir-limpo");
    });

    [Fact]
    public Task Editor_sem_linhas() => OnUi(async () =>
    {
        var editor = new EditorViewModel(_ => EditorFixtures.Lines()) { Project = EditorFixtures.Summary() };
        await editor.Loading;
        Render(new EditorView { DataContext = editor }, "editor-vazio");
    });

    [Fact]
    public Task Editor_lendo() => OnUi(async () =>
    {
        var gate = new ManualResetEventSlim();
        var editor = new EditorViewModel(_ =>
        {
            gate.Wait();
            return EditorFixtures.Lines();
        })
        {
            Project = EditorFixtures.Summary(),
        };
        try
        {
            Render(new EditorView { DataContext = editor }, "editor-lendo");
        }
        finally
        {
            gate.Set();
            await editor.Loading;
        }
    });
}
