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

    private static ThemeVariant Of(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task No_Roteiro_o_nome_vai_acima_do_par_e_o_original_fica_em_cima_da_traducao_com_mais_largura_que_nas_colunas(string themeName, int width, int height)
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
                    var colunas = Original(linhas.GetVisualDescendants().OfType<LineRow>().First(l => l.IsEffectivelyVisible)).Bounds.Width;

                    editor.IsScriptDensity = true;
                    ScreenRenderer.Settle(window);
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName($"editor-roteiro-{width}", Of(themeName))));

                    Assert.Contains("script", linhas.Classes);
                    var visiveis = linhas.GetVisualDescendants().OfType<LineRow>().Where(l => l.IsEffectivelyVisible).ToList();
                    Assert.NotEmpty(visiveis);

                    foreach (var linha in visiveis)
                    {
                        Assert.True(linha.IsScript);
                        var nome = Speaker(linha);
                        var original = Original(linha);
                        var traducao = Translation(linha);
                        Assert.Equal(RowPadding + nome.Bounds.Height + Gap + original.Bounds.Height + Gap + traducao.Bounds.Height, linha.Bounds.Height, Tolerance);
                        Assert.InRange(linha.Bounds.Height, 72 - Tolerance, 108 + Tolerance);

                        Assert.True(Top(nome, window) < Top(original, window), "o nome é o rótulo acima do par");
                        Assert.True(Top(original, window) + original.Bounds.Height <= Top(traducao, window) + Tolerance, "o original fica em cima da tradução");
                        Assert.True(original.Bounds.Height <= 2 * TextLine + Tolerance && traducao.Bounds.Height <= 2 * TextLine + Tolerance, "até duas linhas cada");
                        Assert.True(Math.Abs(Left(nome, window) - Left(original, window)) <= Tolerance && Math.Abs(Left(original, window) - Left(traducao, window)) <= Tolerance, "os três alinham à esquerda");
                        Assert.Equal(original.Bounds.Width, traducao.Bounds.Width, Tolerance);

                        var area = new Rect(linha.TranslatePoint(default, window)!.Value, linha.Bounds.Size).Inflate(Tolerance);
                        foreach (var parte in new[] { nome, original, traducao })
                        {
                            Assert.True(area.Contains(new Rect(parte.TranslatePoint(default, window)!.Value, parte.Bounds.Size)), $"{parte.Name} cabe na linha");
                        }
                    }

                    var largura = Original(visiveis[0]).Bounds.Width;
                    Assert.True(largura >= colunas * 2.5 && largura >= 250, $"o original tinha {colunas:0} px nas colunas e agora tem {largura:0} px");
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task Texto_longo_do_original_e_da_traducao_quebra_em_duas_linhas_e_o_resto_vira_reticencias(string themeName, int width, int height)
    {
        var demo = demoFolder;
        await Run(
            async () =>
            {
                using var config = new TempFolder();
                var (window, editor) = await OpenDemo(config, demo.Root, Of(themeName), width, height);
                try
                {
                    editor.IsScriptDensity = true;
                    ScreenRenderer.Settle(window);
                    var linha = LinesList(window).GetVisualDescendants().OfType<LineRow>().First(l => l.IsEffectivelyVisible);
                    var curta = Original(linha).Bounds.Height;

                    linha.Original = string.Concat(Enumerable.Repeat("窓の外では、朝から雨が降り続いている。", 30));
                    linha.Translation = string.Concat(Enumerable.Repeat("Lá fora, a chuva não parou desde cedo. ", 30));
                    ScreenRenderer.Settle(window);

                    Assert.True(curta <= 2 * TextLine + Tolerance);
                    Assert.Equal(2 * TextLine, Original(linha).Bounds.Height, Tolerance);
                    Assert.Equal(2 * TextLine, Translation(linha).Bounds.Height, Tolerance);
                    Assert.Equal(TextTrimming.CharacterEllipsis, Original(linha).TextTrimming);
                    Assert.Equal(TextTrimming.CharacterEllipsis, Translation(linha).TextTrimming);
                    Assert.Equal(2, Original(linha).MaxLines);
                    Assert.Equal(2, Translation(linha).MaxLines);
                    Assert.Equal(108, linha.Bounds.Height, Tolerance);
                    Assert.True(Top(Translation(linha), window) + Translation(linha).Bounds.Height <= Top(linha, window) + linha.Bounds.Height + Tolerance, "a tradução de duas linhas cabe na linha");
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task O_cabecalho_do_Roteiro_troca_as_colunas_e_o_botao_de_densidade_volta_para_elas(string themeName, int width, int height)
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
                    var alturaColunas = window.GetVisualDescendants().OfType<LineRow>().First(l => l.IsEffectivelyVisible).Bounds.Height;

                    Assert.False(botao.IsChecked);
                    Assert.True(botao.IsEffectivelyVisible);
                    Assert.True(colunas.IsEffectivelyVisible);
                    Assert.False(roteiro.IsEffectivelyVisible);
                    Assert.DoesNotContain("script", linhas.Classes);
                    Assert.Equal(["Linha", "Situação", "Quem fala", "Original", "Tradução"], Titles(colunas));

                    botao.IsChecked = true;
                    ScreenRenderer.Settle(window);

                    Assert.True(editor.IsScriptDensity);
                    Assert.False(colunas.IsEffectivelyVisible);
                    Assert.True(roteiro.IsEffectivelyVisible);
                    Assert.Contains("script", linhas.Classes);
                    Assert.Equal(["Linha", "Situação", "Quem fala · original · tradução"], Titles(roteiro));
                    Assert.True(botao.IsEffectivelyVisible);
                    Assert.True(window.GetVisualDescendants().OfType<LineRow>().First(l => l.IsEffectivelyVisible).Bounds.Height >= alturaColunas * 2 - Tolerance);

                    var linha = linhas.GetVisualDescendants().OfType<LineRow>().First(l => l.IsEffectivelyVisible);
                    var titulos = roteiro.GetVisualDescendants().OfType<TextBlock>().ToList();
                    foreach (var (parte, indice) in new[] { (Number(linha), 0), (Speaker(linha), 2) })
                    {
                        Assert.True(Math.Abs(Left(parte, window) - Left(titulos[indice], window)) <= Tolerance, $"{parte.Name} alinha com o título do cabeçalho");
                    }

                    botao.IsChecked = false;
                    ScreenRenderer.Settle(window);

                    Assert.False(editor.IsScriptDensity);
                    Assert.True(colunas.IsEffectivelyVisible);
                    Assert.False(roteiro.IsEffectivelyVisible);
                    Assert.DoesNotContain("script", linhas.Classes);
                    Assert.Equal(alturaColunas, window.GetVisualDescendants().OfType<LineRow>().First(l => l.IsEffectivelyVisible).Bounds.Height, Tolerance);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task Ao_trocar_a_densidade_nos_dois_sentidos_a_linha_aberta_continua_a_vista(string themeName, int width, int height)
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
                    var aberta = editor.SelectedLine!;
                    Assert.Same(aberta, linhas.SelectedItem);

                    foreach (var roteiro in new[] { true, false, true })
                    {
                        editor.IsScriptDensity = roteiro;
                        ScreenRenderer.Settle(window);

                        var linha = linhas.GetVisualDescendants().OfType<LineRow>().SingleOrDefault(l => l.IsEffectivelyVisible && l.DataContext == aberta);
                        Assert.NotNull(linha);
                        var area = new Rect(linha.TranslatePoint(default, window)!.Value, linha.Bounds.Size);
                        var lista = new Rect(linhas.TranslatePoint(default, window)!.Value, linhas.Bounds.Size).Inflate(Tolerance);
                        Assert.True(lista.Contains(area), $"{(roteiro ? "Roteiro" : "Colunas")}: a linha {area} cabe na lista {lista}");
                    }
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task A_escolha_fica_em_editor_layout_json_e_o_proximo_Editor_abre_no_Roteiro(string themeName, int width, int height)
    {
        var demo = demoFolder;
        await Run(
            async () =>
            {
                using var config = new TempFolder();
                var layout = config.SubFolder("layout");
                var (first, editor) = await OpenDemo(config, demo.Root, Of(themeName), width, height, layout);
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
                var (second, next) = await OpenDemo(config, demo.Root, Of(themeName), width, height, layout);
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
