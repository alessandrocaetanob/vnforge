using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>
/// A contagem de linhas de risco alto do subtítulo "Revisar por risco" na paleta: sem dados no Editor (texto carregando, ou a leitura falhou) ela volta na hora com
/// nulo, e refazer a paleta por causa dela não pode chamar a contagem de novo, num ciclo sem fim.
/// </summary>
public sealed class PaletteRiskCountTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private static string CreateProject(TempFolder temp, bool corrupt = false)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [new TextUnit("yst00001:000000", TextUnitKind.Dialogue, "美咲", "原文", "Bom dia.", TranslationStatus.Machine)]);
        if (corrupt)
        {
            File.WriteAllText(Path.Combine(root, "text", "yst00001.json"), "isto não é json");
        }

        return root;
    }

    private static MainWindowViewModel NewMain(TempFolder temp, string root) =>
        new(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(DateTimeOffset.UnixEpoch)));

    [Fact]
    public async Task Com_a_leitura_do_texto_falha_e_o_Editor_sem_dados_a_paleta_abre_sem_estourar_a_pilha()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp, corrupt: false);
        var main = NewMain(temp, root);
        await main.Start.OpenFolderCommand.ExecuteAsync(null);
        await main.ProjectOpening;
        main.SelectedNav = main.Nav.Single(n => n.Key == NavKey.Editor);
        await main.Editor.Loading;

        // O projeto abriu, e o texto estraga antes de uma releitura: ela falha e o Editor fica sem dados. O Painel e a Conferência
        // ainda podem estar lendo o mesmo arquivo em segundo plano, por isso a gravação espera a leitura terminar.
        TestFiles.WriteAllTextWhileReadersFinish(Path.Combine(root, "text", "yst00001.json"), "isto não é json");
        main.Editor.Reload();
        await main.Editor.Loading;
        Assert.NotNull(main.Editor.ErrorMessage);

        main.Palette.Open();
        main.Palette.Query = "risco";
        await main.RiskCounting!;

        var entry = Assert.Single(main.Palette.Results, r => r.Title == "Revisar por risco");
        Assert.Equal("O que mais precisa de você, no projeto todo", entry.Subtitle);
    }

    [Fact]
    public async Task Com_o_texto_ainda_carregando_a_paleta_abre_sem_estourar_a_pilha()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp, corrupt: false);
        // O texto fica "ainda carregando" por uma porta, e não pela sorte de a paleta abrir antes de a leitura terminar: o teste não tem a thread da tela,
        // então sem a porta o Apply do Editor (que troca Scripts e o cache de falantes) corria em outra thread enquanto a paleta os lia.
        using var gate = new ManualResetEventSlim();
        var main = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(DateTimeOffset.UnixEpoch)))
        {
            Editor = new EditorViewModel(_ =>
            {
                // Com prazo: se o loader rodasse na thread do teste, a porta nunca abriria e a suíte travaria.
                if (!gate.Wait(Deadline))
                {
                    throw new TimeoutException("A porta do loader não abriu: o loader rodou na thread do teste.");
                }

                return ProjectLines.Read(ProjectFolder.Open(root));
            }),
        };

        try
        {
            await main.Start.OpenFolderCommand.ExecuteAsync(null).WaitAsync(Deadline, TestContext.Current.CancellationToken);
            await main.ProjectOpening.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            Assert.True(main.Editor.IsLoading);

            main.Palette.Open();
            main.Palette.Query = "risco";
            if (main.RiskCounting is { } counting)
            {
                await counting.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            }

            Assert.Contains(main.Palette.Results, r => r.Title == "Revisar por risco");
        }
        finally
        {
            gate.Set();
            await main.Editor.Loading.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Fechar_a_paleta_cancela_a_contagem_e_uma_nova_abertura_conta_de_novo()
    {
        using var temp = new TempFolder();
        var main = NewMain(temp, CreateProject(temp, corrupt: false));
        await main.Start.OpenFolderCommand.ExecuteAsync(null);
        await main.ProjectOpening;
        await main.Editor.Loading;

        main.Palette.Open();
        var primeira = main.RiskCounting;
        Assert.NotNull(primeira);
        await primeira;
        main.Palette.Close();
        Assert.Null(main.RiskCounting);

        main.Palette.Open();
        var segunda = main.RiskCounting;
        Assert.NotNull(segunda);
        Assert.NotSame(primeira, segunda);
        await segunda;
    }
}
