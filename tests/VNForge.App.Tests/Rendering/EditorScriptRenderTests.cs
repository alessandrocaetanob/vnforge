using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Demo;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// A densidade Roteiro da grade do Editor (#460) a 1440×1000 e a 1366×768, nos dois temas: o nome de quem fala como rótulo, o original em cima e a tradução embaixo, até duas
/// linhas cada, e a escolha lembrada. As provas são medidas da árvore visual, nunca faixas de pixels de texto. Os PNGs (<c>editor-roteiro-*</c>) vão para <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class EditorScriptRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private const double Tolerance = 0.5;
    private const double TextLine = 18;
    private const double RowPadding = 16;
    private const double Gap = 2;
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    public static TheoryData<string, int, int> Sizes =>
        new()
        {
            { "Dark", 1440, 1000 },
            { "Light", 1440, 1000 },
            { "Dark", 1366, 768 },
            { "Light", 1366, 768 },
        };

    private static readonly bool[] BackToColumnsThenScript = [false, true];

    private static ThemeVariant Of(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task O_botao_troca_as_colunas_pelo_Roteiro_com_o_nome_acima_do_par_e_o_original_em_cima_da_traducao(string themeName, int width, int height)
    {
        var demo = demoFolder;
        await Run(
            async () =>
            {
                using var config = new TempFolder();
                var (window, editor) = await OpenDemo(config, demo.Root, Of(themeName), width, height);
                try
                {
                    var linhas = LinesList(window);
                    var botao = window.GetVisualDescendants().OfType<ToggleButton>().Single(c => c.Name == "DensityToggle");
                    var colunas = Header(window, "ColumnsHeader");
                    var roteiro = Header(window, "ScriptHeader");
                    var primeira = linhas.GetVisualDescendants().OfType<LineRow>().First(l => l.IsEffectivelyVisible);
                    var alturaColunas = primeira.Bounds.Height;
                    var larguraColunas = Original(primeira).Bounds.Width;

                    Assert.False(botao.IsChecked);
                    Assert.True(botao.IsEffectivelyVisible);
                    Assert.True(colunas.IsEffectivelyVisible);
                    Assert.False(roteiro.IsEffectivelyVisible);
                    Assert.DoesNotContain("script", linhas.Classes);
                    Assert.Equal(["Linha", "Situação", "Quem fala", "Original", "Tradução"], Titles(colunas));

                    botao.IsChecked = true;
                    ScreenRenderer.Settle(window);
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName($"editor-roteiro-{width}", Of(themeName))));

                    Assert.True(editor.IsScriptDensity);
                    Assert.False(colunas.IsEffectivelyVisible);
                    Assert.True(roteiro.IsEffectivelyVisible);
                    Assert.Contains("script", linhas.Classes);
                    Assert.Equal(["Linha", "Situação", "Quem fala · original · tradução"], Titles(roteiro));
                    Assert.True(botao.IsEffectivelyVisible);

                    AssertScriptRows(window, linhas, roteiro, larguraColunas, alturaColunas);
                    AssertSelectedLineStaysInView(window, linhas, editor, botao, alturaColunas);
                    AssertLongTextWrapsInTwoLines(window, linhas);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    /// <summary>A escolha fica em editor-layout.json e o próximo Editor abre no Roteiro; não depende de tema nem de tamanho, então roda uma vez.</summary>
    [Fact]
    public async Task A_escolha_fica_em_editor_layout_json_e_o_proximo_Editor_abre_no_Roteiro()
    {
        var demo = demoFolder;
        await Run(
            async () =>
            {
                using var config = new TempFolder();
                var layout = config.SubFolder("layout");
                var (first, editor) = await OpenDemo(config, demo.Root, ThemeVariant.Dark, 1440, 1000, layout);
                try
                {
                    Assert.False(editor.IsScriptDensity);
                    first.GetVisualDescendants().OfType<ToggleButton>().Single(c => c.Name == "DensityToggle").IsChecked = true;
                    ScreenRenderer.Settle(first);
                }
                finally
                {
                    first.Close();
                }

                Assert.True(File.Exists(Path.Combine(layout, "editor-layout.json")));
                var (second, next) = await OpenDemo(config, demo.Root, ThemeVariant.Dark, 1440, 1000, layout);
                try
                {
                    Assert.True(next.IsScriptDensity);
                    Assert.Contains("script", LinesList(second).Classes);
                    Assert.True(second.GetVisualDescendants().OfType<ToggleButton>().Single(c => c.Name == "DensityToggle").IsChecked);
                    Assert.True(second.GetVisualDescendants().OfType<LineRow>().First(l => l.IsEffectivelyVisible).IsScript);
                    Assert.True(Header(second, "ScriptHeader").IsEffectivelyVisible);
                }
                finally
                {
                    second.Close();
                }
            });
    }

    /// <summary>Cada linha visível: o nome é o rótulo acima do par, o original fica em cima da tradução, até duas linhas cada, tudo alinhado e dentro da linha, e o original ganha largura sobre as colunas.</summary>
    private static void AssertScriptRows(MainWindow window, ListBox linhas, Border roteiro, double larguraColunas, double alturaColunas)
    {
        var visiveis = linhas.GetVisualDescendants().OfType<LineRow>().Where(l => l.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(visiveis);
        var titulos = roteiro.GetVisualDescendants().OfType<TextBlock>().ToList();

        foreach (var linha in visiveis)
        {
            Assert.True(linha.IsScript);
            var nome = Speaker(linha);
            var original = Original(linha);
            var traducao = Translation(linha);
            Assert.Equal(RowPadding + nome.Bounds.Height + Gap + original.Bounds.Height + Gap + traducao.Bounds.Height, linha.Bounds.Height, Tolerance);
            Assert.InRange(linha.Bounds.Height, 72 - Tolerance, 108 + Tolerance);
            Assert.True(linha.Bounds.Height >= alturaColunas * 2 - Tolerance);

            Assert.True(Top(nome, window) < Top(original, window), "o nome é o rótulo acima do par");
            Assert.True(Top(original, window) + original.Bounds.Height <= Top(traducao, window) + Tolerance, "o original fica em cima da tradução");
            Assert.True(original.Bounds.Height <= 2 * TextLine + Tolerance && traducao.Bounds.Height <= 2 * TextLine + Tolerance, "até duas linhas cada");
            Assert.True(Math.Abs(Left(nome, window) - Left(original, window)) <= Tolerance && Math.Abs(Left(original, window) - Left(traducao, window)) <= Tolerance, "os três alinham à esquerda");
            Assert.Equal(original.Bounds.Width, traducao.Bounds.Width, Tolerance);
            Assert.True(Math.Abs(Left(Number(linha), window) - Left(titulos[0], window)) <= Tolerance, "o número alinha com o título Linha");
            Assert.True(Math.Abs(Left(nome, window) - Left(titulos[2], window)) <= Tolerance, "o texto alinha com o título do cabeçalho");

            var area = new Rect(linha.TranslatePoint(default, window)!.Value, linha.Bounds.Size).Inflate(Tolerance);
            foreach (var parte in new[] { nome, original, traducao })
            {
                Assert.True(area.Contains(new Rect(parte.TranslatePoint(default, window)!.Value, parte.Bounds.Size)), $"{parte.Name} cabe na linha");
            }
        }

        var largura = Original(visiveis[0]).Bounds.Width;
        Assert.True(largura >= larguraColunas * 2.5 && largura >= 250, $"o original tinha {larguraColunas:0} px nas colunas e agora tem {largura:0} px");
    }

    /// <summary>Trocar a densidade pelo botão, nos dois sentidos, muda a altura das linhas e a linha aberta continua à vista; de volta às colunas a altura é a de antes. Termina no Roteiro.</summary>
    private static void AssertSelectedLineStaysInView(MainWindow window, ListBox linhas, EditorViewModel editor, ToggleButton botao, double alturaColunas)
    {
        var aberta = editor.SelectedLine!;
        Assert.Same(aberta, linhas.SelectedItem);

        foreach (var roteiro in BackToColumnsThenScript)
        {
            botao.IsChecked = roteiro;
            ScreenRenderer.Settle(window);

            Assert.Equal(roteiro, editor.IsScriptDensity);
            Assert.Equal(!roteiro, Header(window, "ColumnsHeader").IsEffectivelyVisible);
            Assert.Equal(roteiro, Header(window, "ScriptHeader").IsEffectivelyVisible);
            Assert.Equal(roteiro, linhas.Classes.Contains("script"));
            var linha = linhas.GetVisualDescendants().OfType<LineRow>().SingleOrDefault(l => l.IsEffectivelyVisible && l.DataContext == aberta);
            Assert.NotNull(linha);
            if (!roteiro)
            {
                Assert.Equal(alturaColunas, linha.Bounds.Height, Tolerance);
            }

            var area = new Rect(linha.TranslatePoint(default, window)!.Value, linha.Bounds.Size);
            var lista = new Rect(linhas.TranslatePoint(default, window)!.Value, linhas.Bounds.Size).Inflate(Tolerance);
            Assert.True(lista.Contains(area), $"{(roteiro ? "Roteiro" : "Colunas")}: a linha {area} cabe na lista {lista}");
        }
    }

    /// <summary>Texto longo do original e da tradução ocupa duas linhas cada, o resto vira reticências e a linha chega aos 108 px.</summary>
    private static void AssertLongTextWrapsInTwoLines(MainWindow window, ListBox linhas)
    {
        var linha = linhas.GetVisualDescendants().OfType<LineRow>().First(l => l.IsEffectivelyVisible);

        linha.Original = string.Concat(Enumerable.Repeat("窓の外では、朝から雨が降り続いている。", 30));
        linha.Translation = string.Concat(Enumerable.Repeat("Lá fora, a chuva não parou desde cedo. ", 30));
        ScreenRenderer.Settle(window);

        Assert.Equal(2 * TextLine, Original(linha).Bounds.Height, Tolerance);
        Assert.Equal(2 * TextLine, Translation(linha).Bounds.Height, Tolerance);
        Assert.Equal(TextTrimming.CharacterEllipsis, Original(linha).TextTrimming);
        Assert.Equal(TextTrimming.CharacterEllipsis, Translation(linha).TextTrimming);
        Assert.Equal(2, Original(linha).MaxLines);
        Assert.Equal(2, Translation(linha).MaxLines);
        Assert.Equal(108, linha.Bounds.Height, Tolerance);
    }

    private static ListBox LinesList(Visual window) => window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "LinesList");

    private static Border Header(Visual window, string name) => window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == name);

    private static string[] Titles(Border header) => header.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("overline")).Select(t => t.Text!).ToArray();

    private static TextBlock Number(LineRow row) => row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Number");

    private static TextBlock Speaker(LineRow row) => row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Speaker");

    private static TextBlock Original(LineRow row) => row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Original");

    private static TextBlock Translation(LineRow row) => row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Translation");

    private static double Top(Visual visual, Visual window) => visual.TranslatePoint(default, window)!.Value.Y;

    private static double Left(Visual visual, Visual window) => visual.TranslatePoint(default, window)!.Value.X;

    private Task<int> Run(Func<Task> body) =>
        fixture.Session.Dispatch<int>(
            async () =>
            {
                await body();
                return 0;
            },
            TestContext.Current.CancellationToken);

    private static async Task<(MainWindow Window, EditorViewModel Editor)> OpenDemo(TempFolder config, string root, ThemeVariant theme, int width, int height, string? layoutFolder = null)
    {
        var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
        var editor = new EditorViewModel(scriptLabel: DemoProject.Label, layoutMemory: layoutFolder is null ? null : new EditorLayoutMemory(layoutFolder));
        var vm = new MainWindowViewModel(start) { Editor = editor };
        var window = new MainWindow { Width = width, Height = height, RequestedThemeVariant = theme, DataContext = vm };
        window.Show();
        await start.OpenFolderCommand.ExecuteAsync(null);
        await vm.ProjectOpening;
        await editor.Loading;
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        Assert.True(await editor.GoToLineAsync(DemoProject.CurrentScript, "yst00013:0495"));
        ScreenRenderer.Settle(window);
        return (window, editor);
    }
}
