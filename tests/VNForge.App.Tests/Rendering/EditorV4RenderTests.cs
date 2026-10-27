using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O Editor da interface v4 (#403) a 1440×1000 e a 1366×768, nos dois temas: o texto com a largura da grade, a cena pelo nome, o cabeçalho sem o id, o botão principal do
/// inspetor numa barra fixa e o original dentro do cartão. As provas são medidas da árvore visual (larguras, <c>Bounds</c> dentro do pai), nunca faixas de pixels de texto.
/// Os PNGs (<c>editor-v4-*</c>) vão para <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class EditorV4RenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private const double Tolerance = 0.5;
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 0, 41, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static ThemeVariant Of(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_1440_Original_e_Traducao_levam_bem_mais_largura_que_antes_e_o_cabecalho_alinha_com_as_linhas(string themeName)
    {
        var demo = demoFolder;
        await Run(
            async () =>
            {
                using var config = new TempFolder();
                var (window, editor) = await OpenDemo(config, demo.Root, Of(themeName), 1440, 1000);
                try
                {
                    Assert.True(window.GetVisualDescendants().OfType<ScenesPanel>().First().IsEffectivelyVisible, "a lista de cenas está aberta");
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("editor-v4-1440", Of(themeName))));

                    var linhas = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "LinesList");
                    var grade = linhas.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("panel"));
                    var cabecalho = (Grid)grade.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("divider-bottom") && b.Child is Grid { ColumnDefinitions.Count: 6 }).Child!;
                    var colunas = cabecalho.ColumnDefinitions;
                    var titulos = cabecalho.Children.OfType<TextBlock>().ToList();

                    Assert.Equal([56d, 92d, 132d, 32d], new[] { colunas[0], colunas[1], colunas[2], colunas[5] }.Select(c => c.Width.Value));
                    Assert.True(colunas[3].Width.IsStar && colunas[4].Width.IsStar);
                    Assert.Equal(1.2, colunas[4].Width.Value / colunas[3].Width.Value, 3);
                    Assert.Equal("Quem fala", titulos[2].Text);

                    var texto = colunas[3].ActualWidth + colunas[4].ActualWidth;
                    // Antes da v4 as duas somavam uns 194 px de 598 (32%); com o inspetor de volta a 474 px elas passam de 40% da grade.
                    Assert.True(texto >= 245 && texto >= grade.Bounds.Width * 0.40, $"Original e Tradução têm {texto:0} de {grade.Bounds.Width:0} px da grade ({texto / grade.Bounds.Width:P0})");
                    Assert.True(colunas[4].ActualWidth > colunas[3].ActualWidth, "a tradução leva um pouco mais que o original");

                    var linha = linhas.GetVisualDescendants().OfType<LineRow>().First(l => l.IsEffectivelyVisible);
                    foreach (var (nome, indice) in new[] { ("PART_Number", 0), ("PART_Speaker", 2), ("PART_Original", 3), ("PART_Translation", 4) })
                    {
                        var parte = linha.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == nome);
                        var xLinha = parte.TranslatePoint(default, window)!.Value.X;
                        var xCabecalho = titulos[indice].TranslatePoint(default, window)!.Value.X;
                        Assert.True(Math.Abs(xLinha - xCabecalho) <= Tolerance, $"{nome} começa em {xLinha:0.#} e o cabeçalho em {xCabecalho:0.#}");
                    }

                    var largura = linha.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Original").Bounds.Width;
                    Assert.True(largura > 105, $"a coluna Original tem mais que os 97 px de antes da v4 ({largura:0} px)");
                    Assert.Equal(["Todas", "Em japonês", "Para revisar", "Com aviso", "Não traduzir"], editor.Filters.Select(f => f.Label));
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_1366_Original_e_Traducao_levam_mais_de_45_por_cento_da_grade(string themeName)
    {
        var demo = demoFolder;
        await Run(
            async () =>
            {
                using var config = new TempFolder();
                var (window, _) = await OpenDemo(config, demo.Root, Of(themeName), 1366, 768);
                try
                {
                    var linhas = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "LinesList");
                    var grade = linhas.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("panel"));
                    var colunas = ((Grid)grade.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("divider-bottom") && b.Child is Grid { ColumnDefinitions.Count: 6 }).Child!).ColumnDefinitions;
                    var texto = colunas[3].ActualWidth + colunas[4].ActualWidth;
                    Assert.True(texto >= grade.Bounds.Width * 0.45, $"Original e Tradução têm {texto:0} de {grade.Bounds.Width:0} px da grade ({texto / grade.Bounds.Width:P0})");
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Nas_abas_o_Usar_sugestao_do_rodape_sai_enquanto_a_aba_Sugestao_esta_aberta_e_um_degrade_cobre_o_corte(string themeName)
    {
        var demo = demoFolder;
        await Run(
            async () =>
            {
                using var config = new TempFolder();
                var (window, _) = await OpenDemo(config, demo.Root, Of(themeName), 1366, 768);
                try
                {
                    var inspetor = window.GetVisualDescendants().OfType<LineDetailView>().Single();
                    var usar = inspetor.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("use-in-row"));
                    var abas = inspetor.GetVisualDescendants().OfType<TabControl>().Single();
                    Assert.True(usar.IsEffectivelyVisible);

                    abas.SelectedIndex = 1;
                    ScreenRenderer.Settle(window);
                    Assert.False(usar.IsEffectivelyVisible);
                    Assert.True(inspetor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ApproveButton").IsEffectivelyVisible);

                    abas.SelectedIndex = 0;
                    ScreenRenderer.Settle(window);
                    Assert.True(usar.IsEffectivelyVisible);

                    var degrade = inspetor.GetVisualDescendants().OfType<Border>().Single(b => b.OpacityMask is LinearGradientBrush);
                    var rodape = inspetor.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("divider-top") && b.GetVisualDescendants().Contains(usar));
                    Assert.False(degrade.IsHitTestVisible);
                    Assert.True(Math.Abs(degrade.TranslatePoint(new Point(0, degrade.Bounds.Height), window)!.Value.Y - rodape.TranslatePoint(default, window)!.Value.Y) <= 8 + Tolerance, "o degradê termina logo acima da barra fixa");
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_lista_de_cenas_e_o_cabecalho_chamam_a_cena_pelo_nome_e_o_id_vai_pequeno(string themeName)
    {
        var demo = demoFolder;
        await Run(
            async () =>
            {
                using var config = new TempFolder();
                var (window, editor) = await OpenDemo(config, demo.Root, Of(themeName), 1440, 1000);
                try
                {
                    var itens = window.GetVisualDescendants().OfType<ScriptItem>().Where(i => i.IsEffectivelyVisible).ToList();
                    Assert.NotEmpty(itens);
                    foreach (var item in itens)
                    {
                        var nome = item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");
                        var id = item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Id");
                        Assert.True(nome.IsEffectivelyVisible, $"{item.Key} tem nome");
                        Assert.DoesNotMatch(@"^yst\d+$", nome.Text!);
                        Assert.Equal(item.Key, id.Text);
                        Assert.Contains("mono", id.Classes);
                        Assert.True(id.FontSize < nome.FontSize, "o id é menor que o nome");
                        Assert.True(id.Bounds.Width > 0 && id.Bounds.Right <= item.Bounds.Width + Tolerance && nome.Bounds.Right <= item.Bounds.Width + Tolerance);
                    }

                    var atual = itens.Single(i => i.Key == DemoProject.CurrentScript);
                    Assert.Equal(FontWeight.SemiBold, atual.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label").FontWeight);

                    var cabecalho = window.GetVisualDescendants().OfType<PageHeader>().Single(h => h.IsEffectivelyVisible);
                    var sobrancelha = cabecalho.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Breadcrumb");
                    var titulo = cabecalho.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Title");
                    Assert.Equal("Capítulo 1 · cena 13 de 45", sobrancelha.Text);
                    Assert.Equal(editor.SelectedScript!.Name, titulo.Text);
                    Assert.DoesNotMatch(@"yst\d+", titulo.Text! + sobrancelha.Text);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Sem_nome_a_cena_vira_Cena_N_no_titulo_e_na_lista(string themeName)
    {
        using var temp = new TempFolder();
        var root = FitWarningRenderTests.CreateProject(temp.SubFolder("projeto"));
        await Run(
            async () =>
            {
                using var config = new TempFolder();
                var (window, editor) = await OpenProject(config, root, Of(themeName), 1440, 1000, FitWarningRenderTests.LongLine);
                try
                {
                    var cabecalho = window.GetVisualDescendants().OfType<PageHeader>().Single(h => h.IsEffectivelyVisible);
                    Assert.Equal("Cena 1", cabecalho.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Title").Text);
                    Assert.Equal("Capítulo 1 · cena 1 de 1", cabecalho.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Breadcrumb").Text);
                    var item = window.GetVisualDescendants().OfType<ScriptItem>().Single(i => i.IsEffectivelyVisible);
                    Assert.Equal("Cena 1", item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label").Text);
                    Assert.Equal(FitWarningRenderTests.Script, item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Id").Text);
                    Assert.DoesNotContain("yst", editor.ScriptTitle, StringComparison.Ordinal);
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("editor-v4-sem-nome", Of(themeName))));
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Theory]
    [InlineData("Dark", 1440, 1000)]
    [InlineData("Light", 1440, 1000)]
    [InlineData("Dark", 1366, 768)]
    [InlineData("Light", 1366, 768)]
    public async Task O_botao_Aprovar_e_proxima_fica_inteiro_na_barra_fixa_mesmo_com_o_aviso_de_caixa(string themeName, int width, int height)
    {
        using var temp = new TempFolder();
        var root = FitWarningRenderTests.CreateProject(temp.SubFolder("projeto"));
        var theme = Of(themeName);
        await Run(
            async () =>
            {
                using var config = new TempFolder();
                var (window, editor) = await OpenProject(config, root, theme, width, height, FitWarningRenderTests.LongLine);
                try
                {
                    Assert.True(editor.Detail!.Fit!.IsVisible, "o caso é o de 'não cabe na caixa'");
                    var inspetor = window.GetVisualDescendants().OfType<LineDetailView>().Single();
                    var painel = inspetor.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("panel"));
                    var aprovar = inspetor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ApproveButton");
                    var usar = inspetor.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("use-in-row"));
                    var nome = width == 1440 ? "editor-v4-caixa-nao-cabe" : "editor-v4-compacto-caixa-nao-cabe";
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName(nome, theme)));

                    var visivel = new Rect(painel.TranslatePoint(default, window)!.Value, painel.Bounds.Size).Inflate(Tolerance);
                    foreach (var botao in new[] { aprovar, usar })
                    {
                        Assert.True(botao.IsEffectivelyVisible);
                        var area = new Rect(botao.TranslatePoint(default, window)!.Value, botao.Bounds.Size);
                        Assert.True(visivel.Contains(area), $"{width}: o botão {area} cabe na área visível do inspetor {visivel}");
                        Assert.Empty(botao.GetVisualAncestors().TakeWhile(a => a != inspetor).OfType<ScrollViewer>());
                    }

                    Assert.Same(editor.Detail.ApproveAndNextCommand, aprovar.Command);
                    var texto = aprovar.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible);
                    Assert.Equal("Aprovar e próxima", texto.Text);
                    Assert.True(texto.Bounds.Right <= aprovar.Bounds.Width + Tolerance, "o texto do botão principal não é cortado");

                    var aviso = inspetor.GetVisualDescendants().OfType<FitWarning>().Single(w => w.IsEffectivelyVisible);
                    var rolagem = aviso.GetVisualAncestors().OfType<ScrollViewer>().First();
                    Assert.True(aprovar.TranslatePoint(default, window)!.Value.Y >= rolagem.TranslatePoint(new Point(0, rolagem.Bounds.Height), window)!.Value.Y - Tolerance, "a barra de ações fica abaixo da área que rola");

                    var tecnico = inspetor.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Text?.Contains("máximo", StringComparison.Ordinal) == true).Select(t => t.Text);
                    Assert.False(tecnico.Any(), $"frase técnica no inspetor: {string.Join(" | ", tecnico)}");
                    Assert.Contains(inspetor.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.StartsWith("Passa 1 linha", StringComparison.Ordinal) == true);
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task O_original_quebra_linha_dentro_do_cartao_e_o_rotulo_de_quem_fala_fica_fora_da_barra_de_rolagem(string themeName)
    {
        using var temp = new TempFolder();
        var root = CreateLongSourceProject(temp.SubFolder("projeto"));
        await Run(
            async () =>
            {
                using var config = new TempFolder();
                var (window, _) = await OpenProject(config, root, Of(themeName), 1440, 1000, "yst00013:000001");
                try
                {
                    var inspetor = window.GetVisualDescendants().OfType<LineDetailView>().Single();
                    var cartao = inspetor.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("original"));
                    var original = inspetor.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SourceText");
                    var rolagem = inspetor.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "WideScroll");
                    var falante = inspetor.GetVisualDescendants().OfType<TextBlock>().Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Falante");

                    var cartaoArea = new Rect(cartao.TranslatePoint(default, window)!.Value, cartao.Bounds.Size).Inflate(Tolerance);
                    var textoArea = new Rect(original.TranslatePoint(default, window)!.Value, original.Bounds.Size);
                    Assert.True(original.Bounds.Height > original.FontSize * 2.5, $"o original quebrou em mais de uma linha ({original.Bounds.Height:0} px)");
                    Assert.True(cartaoArea.Contains(textoArea), $"o texto {textoArea} está dentro do cartão {cartaoArea}");

                    var viewport = new Rect(rolagem.TranslatePoint(default, window)!.Value, rolagem.Viewport);
                    Assert.True(cartaoArea.Right <= viewport.Right - 8, "o cartão deixa margem para a barra de rolagem");
                    Assert.True(falante.TranslatePoint(default, window)!.Value.X + falante.Bounds.Width <= viewport.Right - 8, "o rótulo de quem fala não passa por baixo da barra");
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("editor-v4-original-longo", Of(themeName))));
                }
                finally
                {
                    window.Close();
                }
            });
    }

    private Task<int> Run(Func<Task> body) =>
        fixture.Session.Dispatch<int>(
            async () =>
            {
                await body();
                return 0;
            },
            TestContext.Current.CancellationToken);

    private static Task<(MainWindow Window, EditorViewModel Editor)> OpenDemo(TempFolder config, string demoRoot, ThemeVariant theme, int width, int height) =>
        Open(config, demoRoot, theme, width, height, DemoProject.Label, DemoProject.CurrentScript, "yst00013:0495");

    private static Task<(MainWindow Window, EditorViewModel Editor)> OpenProject(TempFolder config, string root, ThemeVariant theme, int width, int height, string line) =>
        Open(config, root, theme, width, height, null, FitWarningRenderTests.Script, line);

    private static async Task<(MainWindow Window, EditorViewModel Editor)> Open(
        TempFolder config, string root, ThemeVariant theme, int width, int height, Func<string, string?>? label, string script, string line)
    {
        var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
        var editor = new EditorViewModel(scriptLabel: label);
        var vm = new MainWindowViewModel(start) { Editor = editor };
        var window = new MainWindow { Width = width, Height = height, RequestedThemeVariant = theme, DataContext = vm };
        window.Show();
        await start.OpenFolderCommand.ExecuteAsync(null);
        await vm.ProjectOpening;
        await editor.Loading;
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        Assert.True(await editor.GoToLineAsync(script, line));
        ScreenRenderer.Settle(window);
        return (window, editor);
    }

    private static string CreateLongSourceProject(string parent)
    {
        var folder = ProjectFolder.Create(parent, new ProjectConfig(
            DemoProject.Name,
            new GameConfig(@"D:\Jogos\MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default));
        folder.SaveFormatting(FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: 40, MaxLines: 3, CharWidth: CharWidthMode.Uniform) });
        var longo = string.Concat(Enumerable.Repeat("担任の先生が腕を組んで立っていた。", 6));
        folder.SaveText(
            "yst00013",
            [new TextUnit("yst00013:000001", TextUnitKind.Dialogue, "美咲", $"「{longo}」", "Fala longa.", TranslationStatus.Translated)]);
        return folder.Root;
    }
}
