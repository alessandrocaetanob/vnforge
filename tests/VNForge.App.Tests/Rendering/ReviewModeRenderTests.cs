using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Demo;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O modo revisão montado de verdade a 1440×1000 nos dois temas: uma fala que cabe, uma que passa da caixa de um personagem sem gênero e a tela final.
/// Os PNG (<c>VNFORGE_RENDER_DIR</c>) vão ao lado do artboard no PR. A conferência é pela tinta em retângulos conhecidos da caixa do jogo e pela cor
/// de fundo do aviso, não por faixas de pixels de texto, porque a fonte muda de um sistema para outro.
/// </summary>
public sealed class ReviewModeRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const int Tinta = 24;
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);
    private static readonly string LongText = "O nosso professor responsável já estava de pé, esperando por nós dois de braços cruzados na frente do portão da escola, debaixo da chuva.";

    public static TheoryData<string> Themes => ["Dark", "Light"];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Fala_que_cabe_mostra_a_caixa_do_jogo_com_tinta_e_as_duas_colunas(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            Assert.Equal("Linha 1 de 3", vm.Editor.Review!.PositionText);
            Assert.True(view.Bounds.Width > 1200 && view.Bounds.Height > 600, "a tela ocupa o Editor");

            var box = Assert.Single(view.GetVisualDescendants().OfType<GameTextbox>());
            Assert.Equal(GameTextboxSize.Stage, box.Size);
            var frame = Frame(window, box);
            Assert.True(frame.Left > window.Width / 2 - 40, "a caixa do jogo fica na coluna da direita");
            Assert.True(frame.Right <= window.Width && frame.Bottom <= window.Height, "a caixa cabe na janela");
            Assert.Equal(1d, box.FitScale);

            var probe = PixelProbe.Capture(window, box);
            var ink = Token(box, "Vnf.TextboxInk");
            var rows = box.RowBounds();
            Assert.Equal([GameTextboxRowKind.Text, GameTextboxRowKind.Vacant, GameTextboxRowKind.Vacant], rows.Select(r => r.Row.Kind));
            Assert.True(probe.CountInArea(rows[0].Bounds, ink, Tinta) >= 3, "a primeira linha tem tinta");
            Assert.Equal(0, probe.CountInArea(rows[1].Bounds, ink, Tinta));

            Assert.Equal("Cabe · 1 de 3 linhas", vm.Editor.Review.Detail!.FitText);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<FitWarning>(), w => w.IsEffectivelyVisible);
            Salvar(window, "modo-revisao-cabe", theme);
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Fala_que_passa_mostra_o_traco_o_aviso_com_as_saidas_e_o_aviso_de_genero(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            vm.Editor.Review!.SkipCommand.Execute(null);
            ScreenRenderer.Settle(window);
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);

            var box = Assert.Single(view.GetVisualDescendants().OfType<GameTextbox>());
            var probe = PixelProbe.Capture(window, box);
            var overflow = Token(box, "Vnf.TextboxOverflow");
            var over = Assert.Single(box.RowBounds(), r => r.Row.Kind == GameTextboxRowKind.Overflow && r.Row.FirstOverflow);
            Assert.True(probe.CountInArea(over.Bounds, overflow, Tinta) >= 3, "a linha que passa vai na cor de overflow");

            var warning = Assert.Single(view.GetVisualDescendants().OfType<FitWarning>(), w => w.IsEffectivelyVisible);
            Assert.NotNull(warning.SplitCommand);
            Assert.NotNull(warning.ShortenCommand);
            var warningProbe = PixelProbe.Capture(window, warning);
            var expected = theme == ThemeVariant.Light ? Color.Parse("#FFF1D6") : Color.Parse("#3A2C10");
            Assert.True(warningProbe.Near(10, 6, expected, 6), $"o fundo do aviso é o do tema ({warningProbe.At(10, 6)} em vez de {expected})");

            Assert.True(vm.Editor.Review!.GenderMissing);
            var gender = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == vm.Editor.Review.DefineGenderCommand);
            Assert.True(gender.Bounds.Width > 40 && gender.Bounds.Height > 16);
            Salvar(window, "modo-revisao-passa", theme);
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Tela_final_mostra_o_resumo_e_a_proxima_cena(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            var review = vm.Editor.Review!;
            review.ApproveCommand.Execute(null);
            review.LaterCommand.Execute(null);
            review.SkipCommand.Execute(null);
            ScreenRenderer.Settle(window);

            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var card = Assert.Single(view.GetVisualDescendants().OfType<Border>(), b => b.IsEffectivelyVisible && b.Width == 520);
            var origin = card.TranslatePoint(default, window) ?? throw new InvalidOperationException("o cartão não está na janela");
            Assert.True(origin.X > 0 && origin.X + card.Bounds.Width < window.Width && origin.Y > 0 && origin.Y + card.Bounds.Height < window.Height);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<GameTextbox>(), b => b.IsEffectivelyVisible);
            Assert.Equal("1 linha aprovada, 1 deixada para depois e 1 pulada.", review.FinishedText);
            Assert.Equal("Cena 2", review.NextSceneName);
            Assert.Equal("Cena 2", view.FindControl<TextBlock>("NextSceneTitle")!.Text);
            Assert.Equal("1 linha revisada hoje", review.TodayText);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible), t => t.Text?.Contains("yst0", StringComparison.Ordinal) == true);
            Assert.Equal("Cena 1", view.FindControl<TextBlock>("SceneTitle")!.Text);
            Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == review.GoToNextSceneCommand);
            Salvar(window, "modo-revisao-fim", theme);
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_1366_por_768_a_tela_cobre_o_Editor_e_a_caixa_do_jogo_cabe_na_janela(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var origin = view.TranslatePoint(default, window) ?? throw new InvalidOperationException("a tela não está na janela");
            Assert.True(view.Bounds.Width > 1100 && view.Bounds.Height > 500, $"a tela ocupa o Editor ({view.Bounds.Width}x{view.Bounds.Height})");
            Assert.True(origin.X + view.Bounds.Width <= window.Width && origin.Y + view.Bounds.Height <= window.Height, "a tela cabe na janela");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<PageHeader>(), h => h.IsEffectivelyVisible);

            var box = Assert.Single(view.GetVisualDescendants().OfType<GameTextbox>());
            var boxOrigin = box.TranslatePoint(default, window) ?? throw new InvalidOperationException("a caixa não está na janela");
            Assert.True(boxOrigin.X + box.Bounds.Width <= window.Width && boxOrigin.Y + box.Bounds.Height <= window.Height, "a caixa cabe na janela");
            Salvar(window, "modo-revisao-1366", theme);
        }, 1366, 768);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task O_palco_e_16_por_9_maior_que_o_roteiro_e_guarda_a_caixa_na_metade_de_baixo(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var stage = Assert.Single(view.GetVisualDescendants().OfType<ReviewStage>());
            var script = view.FindControl<ScrollViewer>("Script")!;
            var stageFrame = Frame(window, stage);
            Assert.InRange(stageFrame.Width / stageFrame.Height, (16d / 9) - 0.02, (16d / 9) + 0.02);
            Assert.True(stageFrame.Width > script.Bounds.Width, $"o palco ({stageFrame.Width}) é mais largo que o roteiro ({script.Bounds.Width})");
            Assert.True(stageFrame.Right <= window.Width && stageFrame.Bottom <= window.Height);
            Assert.Equal("Cena 1", stage.SceneName);

            var box = Assert.Single(stage.GetVisualDescendants().OfType<GameTextbox>());
            var boxFrame = Frame(window, box);
            Assert.True(stageFrame.Contains(boxFrame.TopLeft) && stageFrame.Contains(boxFrame.BottomRight), "a caixa do jogo está dentro do palco");
            Assert.True(boxFrame.Top > stageFrame.Top + (stageFrame.Height / 2) - 60 && boxFrame.Bottom > stageFrame.Bottom - 40, "a caixa está encostada na base do palco");
            Assert.True(boxFrame.Width > stageFrame.Width * 0.6, $"a caixa ocupa a largura útil do palco ({boxFrame.Width} de {stageFrame.Width})");
            Salvar(window, "modo-revisao-palco", theme);
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task O_fundo_do_palco_e_escuro_nos_dois_temas(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var stage = Assert.Single(view.GetVisualDescendants().OfType<ReviewStage>());
            var probe = PixelProbe.Capture(window, stage);
            var top = Token(stage, "Vnf.StageTop");
            var bottom = Token(stage, "Vnf.StageBottom");

            // Faixa do alto, à direita do cartão de capítulo e longe da caixa: só o degradê (a vinheta começa depois dos 55% do raio).
            var area = new Rect(probe.Width * 0.45, probe.Height * 0.08, probe.Width * 0.3, probe.Height * 0.12);
            var expected = PixelProbe.Blend(bottom, 0.14, top);
            Assert.True(probe.CountInArea(area, expected, 14) > area.Width * area.Height * 0.9, "o alto do palco é o degradê escuro do tema");
            Assert.True(((0.2126 * expected.R) + (0.7152 * expected.G) + (0.0722 * expected.B)) / 255 < 0.3, "o palco é escuro mesmo no tema claro");
        });
    }

    [Theory]
    [InlineData(1440, 1000)]
    [InlineData(1366, 768)]
    public async Task A_barra_de_acoes_fica_inteira_na_janela_e_Esta_boa_aparece_sem_rolar(int width, int height)
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, ThemeVariant.Dark, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var bar = view.FindControl<Border>("ActionBar")!;
            var barFrame = Frame(window, bar);
            Assert.True(barFrame.Left >= 0 && barFrame.Right <= window.Width && barFrame.Bottom <= window.Height, $"a barra cabe na janela ({barFrame})");
            Assert.True(barFrame.Width > window.Width - 160, "a barra vai de ponta a ponta do Editor");
            Assert.DoesNotContain(bar.GetVisualAncestors(), a => a is ScrollViewer);

            var review = vm.Editor.Review!;
            var approve = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == review.ApproveCommand);
            var later = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == review.LaterCommand);
            var approveFrame = Frame(window, approve);
            Assert.True(barFrame.Contains(approveFrame.TopLeft) && barFrame.Contains(approveFrame.BottomRight), "Está boa fica dentro da barra");
            Assert.True(approveFrame.Bottom <= window.Height - 20, "Está boa está à vista");
            Assert.DoesNotContain(approve.GetVisualAncestors(), a => a is ScrollViewer);
            Assert.True(approveFrame.Right < Frame(window, later).Left, "Depois e Pular ficam à direita");

            var stage = Assert.Single(view.GetVisualDescendants().OfType<ReviewStage>());
            Assert.True(Frame(window, stage).Bottom <= barFrame.Top, "o palco não passa para baixo da barra");
        }, width, height);
    }

    [Fact]
    public async Task O_nome_de_quem_fala_e_texto_selecionavel_com_o_menu_de_copiar_o_nome_original()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, ThemeVariant.Dark, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var speaker = Assert.Single(view.GetVisualDescendants().OfType<SelectableTextBlock>(), b => AutomationProperties.GetName(b) == "Quem fala");
            Assert.StartsWith("美咲", speaker.Text, StringComparison.Ordinal);
            // O texto da tela pode trazer a tradução e a voz; o menu copia o nome do jogo.
            Assert.Equal("美咲", SpeakerMenu.GetOriginal(speaker));
            var flyout = Assert.IsAssignableFrom<MenuFlyout>(speaker.ContextFlyout);
            Assert.Contains(flyout.Items.OfType<MenuItem>(), i => Equals(i.Header, SpeakerMenu.CopyOriginalHeader));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task O_menu_do_nome_revisa_as_falas_do_personagem_em_todas_as_cenas_e_a_tela_diz_de_quem_sao(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            Assert.Equal(string.Empty, vm.Editor.Review!.SourceText);
            var speaker = Assert.Single(view.GetVisualDescendants().OfType<SelectableTextBlock>(), b => AutomationProperties.GetName(b) == "Quem fala");
            var flyout = Assert.IsType<MenuFlyout>(speaker.ContextFlyout, exactMatch: false);
            flyout.ShowAt(speaker);
            ScreenRenderer.Settle(window);
            var item = Assert.Single(flyout.Items.OfType<MenuItem>(), i => Equals(i.Header, SpeakerMenu.ReviewHeader));
            Assert.True(item.IsVisible);
            item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            flyout.Hide();
            ScreenRenderer.Settle(window);

            var review = vm.Editor.Review!;
            Assert.Equal(3, review.Total);
            Assert.Equal("Linha 1 de 3", review.PositionText);
            Assert.Equal("Falas de Misaki em todas as cenas", review.SourceText);
            var caption = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "SourceCaption");
            Assert.True(caption.IsEffectivelyVisible);
            Assert.Equal("Falas de Misaki em todas as cenas", caption.Text);

            var scene = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "SceneTitle");
            var first = scene.Text;
            review.SkipCommand.Execute(null);
            review.SkipCommand.Execute(null);
            ScreenRenderer.Settle(window);
            Assert.Equal("yst00002", vm.Editor.SelectedScript!.Key);
            Assert.NotEqual(first, scene.Text);
            Assert.Equal(vm.Editor.SelectedScript.Name, scene.Text);
            Salvar(window, "modo-revisao-personagem", theme);
        });
    }

    [Fact]
    public async Task Os_chips_de_reescrever_ficam_sob_a_caixa_do_jogo()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, ThemeVariant.Dark, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var bar = Assert.Single(view.GetVisualDescendants().OfType<RewriteBar>(), b => b.IsEffectivelyVisible);
            var stage = Assert.Single(view.GetVisualDescendants().OfType<ReviewStage>());
            Assert.True(Frame(window, bar).Top >= Frame(window, stage).Bottom - 1, "os chips ficam sob o palco");
            var labels = bar.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => (b.Content as TextBlock)?.Text).ToList();
            Assert.Equal(["Mais curta", "Mais natural", "Mais fiel", "Mais formal", "Como o personagem falaria"], labels);
        });
    }

    [Fact]
    public async Task A_1440_por_1000_o_bloco_das_colunas_fica_centrado_entre_o_topo_e_a_barra_e_o_palco_passa_de_60_por_cento()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, ThemeVariant.Dark, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var top = Frame(window, view.FindControl<Grid>("TopBar")!);
            var body = Frame(window, view.FindControl<Grid>("Body")!);
            var bar = Frame(window, view.FindControl<Border>("ActionBar")!);
            var above = body.Top - top.Bottom;
            var below = bar.Top - body.Bottom;
            Assert.True(Math.Abs(above - below) <= 24, $"o vazio se divide em cima ({above}) e embaixo ({below})");

            var stage = Frame(window, Assert.Single(view.GetVisualDescendants().OfType<ReviewStage>()));
            var script = Frame(window, view.FindControl<ScrollViewer>("Script")!);
            Assert.True(stage.Width / body.Width > 0.6, $"o palco ocupa mais de 60% da largura ({stage.Width / body.Width:P0})");
            Assert.True(Math.Abs(script.Top - stage.Top) <= 1, "roteiro e palco se alinham pelo topo");
            Salvar(window, "modo-revisao-centrado", ThemeVariant.Dark);
        });
    }

    [Fact]
    public async Task Com_pouca_altura_o_palco_nao_passa_de_180_px_e_so_a_pilha_de_baixo_rola()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, ThemeVariant.Dark, (window, vm) =>
        {
            vm.Editor.Review!.SkipCommand.Execute(null);
            ScreenRenderer.Settle(window);
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var stageFrame = Frame(window, Assert.Single(view.GetVisualDescendants().OfType<ReviewStage>()));
            var below = Frame(window, view.FindControl<ScrollViewer>("BelowStage")!);
            var barFrame = Frame(window, view.FindControl<Border>("ActionBar")!);
            Assert.True(stageFrame.Height >= 179, $"o palco tem {stageFrame.Height} px");
            Assert.True(below.Top >= stageFrame.Bottom && below.Bottom <= barFrame.Top, "a pilha de baixo cabe entre o palco e a barra");
            Assert.True(barFrame.Bottom <= window.Height, "a barra segue dentro da janela");
            Assert.True(view.FindControl<ScrollViewer>("BelowStage")!.Extent.Height > below.Height, "o que não cabe rola dentro da pilha");
            Salvar(window, "modo-revisao-passa-baixo", ThemeVariant.Dark);
        }, 1366, 560);
    }

    [Fact]
    public async Task Trocar_de_linha_no_meio_do_fade_termina_com_o_roteiro_e_o_palco_opacos()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, ThemeVariant.Dark, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var script = view.FindControl<ScrollViewer>("Script")!;
            var stage = Assert.Single(view.GetVisualDescendants().OfType<ReviewStage>());

            vm.Editor.Review!.SkipCommand.Execute(null);
            ScreenRenderer.Settle(window);
            vm.Editor.Review.SkipCommand.Execute(null);
            for (var n = 0; n < 12; n++)
            {
                SpinWait.SpinUntil(static () => false, 60);
                ScreenRenderer.Settle(window);
            }

            Assert.Equal("Linha 3 de 3", vm.Editor.Review.PositionText);
            Assert.Equal(1d, script.Opacity);
            Assert.Equal(1d, stage.Opacity);
        }, animations: true);
    }

    [Fact]
    public async Task A_1366_com_aviso_o_palco_encolhe_e_a_barra_continua_inteira()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, ThemeVariant.Dark, (window, vm) =>
        {
            vm.Editor.Review!.SkipCommand.Execute(null);
            ScreenRenderer.Settle(window);
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var stageFrame = Frame(window, Assert.Single(view.GetVisualDescendants().OfType<ReviewStage>()));
            var barFrame = Frame(window, view.FindControl<Border>("ActionBar")!);
            Assert.True(barFrame.Bottom <= window.Height);
            Assert.True(stageFrame.Bottom <= barFrame.Top);
            var warning = Frame(window, Assert.Single(view.GetVisualDescendants().OfType<FitWarning>(), w => w.IsEffectivelyVisible));
            Assert.True(warning.Top >= stageFrame.Bottom && warning.Bottom <= barFrame.Top, "o aviso cabe entre o palco e a barra");
            Salvar(window, "modo-revisao-passa-1366", ThemeVariant.Dark);
        }, 1366, 768);
    }

    [Theory]
    [InlineData("Dark", 1366, 768)]
    [InlineData("Light", 1366, 768)]
    [InlineData("Dark", 1440, 900)]
    [InlineData("Light", 1440, 900)]
    public async Task Com_a_fala_anterior_o_roteiro_cabe_sem_cortar_e_a_barra_continua_inteira(string themeName, int width, int height)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            var review = vm.Editor.Review!;
            review.SkipCommand.Execute(null);
            ScreenRenderer.Settle(window);
            Assert.True(review.HasPrevious && review.HasNext);
            Salvar(window, $"modo-revisao-anterior-{width}", theme);
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var script = view.FindControl<ScrollViewer>("Script")!;
            var viewport = Frame(window, script);
            var previous = Assert.Single(view.GetVisualDescendants().OfType<ContextLines>(), c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == "Fala anterior");
            Assert.True(Frame(window, previous).Top >= viewport.Top - 1, "a fala anterior abre o roteiro, acima do original");
            Assert.True(script.Extent.Height <= script.Viewport.Height + 1, $"o roteiro inteiro cabe sem rolar ({script.Extent.Height} > {script.Viewport.Height})");
            Assert.All(
                view.GetVisualDescendants().OfType<ContextLines>().Where(c => c.IsEffectivelyVisible),
                c => Assert.True(Frame(window, c).Bottom <= viewport.Bottom + 1, "nenhuma fala vizinha passa da borda do roteiro"));
            if (width >= 1440)
            {
                Assert.Contains(view.GetVisualDescendants().OfType<ContextLines>(), c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == "Fala seguinte");
            }

            var barFrame = Frame(window, view.FindControl<Border>("ActionBar")!);
            var stageFrame = Frame(window, Assert.Single(view.GetVisualDescendants().OfType<ReviewStage>()));
            Assert.True(barFrame.Bottom <= window.Height, "a barra cabe na janela");
            Assert.True(stageFrame.Bottom <= barFrame.Top, "o palco não passa para baixo da barra");
        }, width, height);
    }

    [Fact]
    public async Task A_1366_a_fala_seguinte_volta_quando_a_linha_seguinte_deixa_o_roteiro_caber()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, ThemeVariant.Dark, (window, vm) =>
        {
            var review = vm.Editor.Review!;
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            bool NextShown() => view.GetVisualDescendants().OfType<ContextLines>().Any(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == "Fala seguinte");

            Assert.True(NextShown(), "na primeira linha o roteiro cabe com a seguinte");
            review.SkipCommand.Execute(null);
            ScreenRenderer.Settle(window);
            Assert.False(NextShown(), "na linha longa a seguinte cede o lugar");
            review.SkipCommand.Execute(null);
            ScreenRenderer.Settle(window);
            Assert.False(review.HasNext);
            review.BackCommand.Execute(null);
            review.BackCommand.Execute(null);
            ScreenRenderer.Settle(window);
            var sv = view.FindControl<ScrollViewer>("Script")!;
            Assert.True(NextShown(), $"de volta à primeira, a seguinte reaparece (extent {sv.Extent.Height}, viewport {sv.Viewport.Height}, pos {review.PositionText}, hasnext {review.HasNext})");
        }, 1366, 768);
    }

    /// <summary>
    /// A fala seguinte que cedeu na linha anterior não pode pesar na linha nova: chegar à terceira linha passando pela segunda (cuja seguinte é longa e some) e abrir a
    /// revisão já na terceira têm que dar o mesmo resultado. A altura varia porque a diferença só aparece quando a janela está entre as duas medidas.
    /// </summary>
    [Theory]
    [InlineData(800)]
    [InlineData(808)]
    [InlineData(816)]
    [InlineData(824)]
    [InlineData(832)]
    [InlineData(840)]
    [InlineData(848)]
    [InlineData(856)]
    [InlineData(864)]
    [InlineData(872)]
    [InlineData(880)]
    public async Task Ao_trocar_de_linha_a_medida_da_fala_seguinte_recomeca_do_zero(int height)
    {
        var viaSecond = await NextShownOnThirdLine(height, reviewedFirstTwo: false);
        var direct = await NextShownOnThirdLine(height, reviewedFirstTwo: true);

        Assert.Equal(direct, viaSecond);
    }

    private async Task<bool> NextShownOnThirdLine(int height, bool reviewedFirstTwo)
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"), longNeighbours: true, reviewedFirstTwo);
        var shown = false;

        await Abrir(root, ThemeVariant.Dark, (window, vm) =>
        {
            var review = vm.Editor.Review!;
            if (!reviewedFirstTwo)
            {
                review.SkipCommand.Execute(null);
                ScreenRenderer.Settle(window);
                review.SkipCommand.Execute(null);
                ScreenRenderer.Settle(window);
            }

            Assert.True(review.HasPrevious && review.HasNext);
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            shown = view.GetVisualDescendants().OfType<ContextLines>().Any(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == "Fala seguinte");
        }, 1366, height);

        return shown;
    }

    /// <summary>
    /// A view do modo revisão é reaproveitada entre sessões: a fala seguinte que cedeu no fim de uma sessão não pode pesar na abertura da seguinte.
    /// Abrir a revisão de novo depois de uma sessão que terminou com a seguinte escondida tem que dar o mesmo que abrir a primeira vez.
    /// </summary>
    [Theory]
    [InlineData(718)]
    [InlineData(730)]
    [InlineData(742)]
    [InlineData(754)]
    [InlineData(766)]
    public async Task Uma_sessao_nova_nao_herda_a_fala_seguinte_escondida_da_anterior(int height)
    {
        var fresh = await NextShownOnFirstLine(height, previousSession: false);
        var reopened = await NextShownOnFirstLine(height, previousSession: true);

        Assert.Equal(fresh, reopened);
    }

    private async Task<bool> NextShownOnFirstLine(int height, bool previousSession)
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"), tallThird: true);
        var shown = false;

        await Abrir(root, ThemeVariant.Dark, (window, vm) =>
        {
            if (previousSession)
            {
                vm.Editor.Review!.SkipCommand.Execute(null);
                ScreenRenderer.Settle(window);
                vm.Editor.StopReview();
                ScreenRenderer.Settle(window);
                vm.Editor.StartReview();
                ScreenRenderer.Settle(window);
            }

            Assert.Equal("Linha 1 de 4", vm.Editor.Review!.PositionText);
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            shown = view.GetVisualDescendants().OfType<ContextLines>().Any(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == "Fala seguinte");
        }, 1366, height);

        return shown;
    }

    private async Task Abrir(string root, ThemeVariant theme, Action<MainWindow, MainWindowViewModel> verificar, int width = ScreenRenderer.Width, int height = ScreenRenderer.Height, bool animations = false)
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                using var motion = MotionPreferences.Force(animations);
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start);
                var window = new MainWindow { Width = width, Height = height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await vm.Editor.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
                    ScreenRenderer.Settle(window);

                    var entry = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == vm.Editor.StartReviewCommand);
                    entry.Command!.Execute(null);
                    ScreenRenderer.Settle(window);
                    Assert.True(vm.Editor.IsReviewing);

                    verificar(window, vm);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>Onde o controle está na janela, em pixels dela, já com qualquer escala que um Viewbox aplique.</summary>
    private static Rect Frame(Visual root, Control control)
    {
        var origin = control.TranslatePoint(default, root) ?? throw new InvalidOperationException("O controle não está na janela.");
        var end = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), root) ?? throw new InvalidOperationException("O controle não está na janela.");
        return new Rect(origin, end);
    }

    private static Color Token(Control host, string key)
    {
        Assert.True(host.TryFindResource(key, host.ActualThemeVariant, out var found), key);
        return Assert.IsType<ISolidColorBrush>(found, exactMatch: false).Color;
    }

    private static void Salvar(Window window, string name, ThemeVariant theme)
    {
        ScreenRenderer.Settle(window);
        ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName(name, theme)));
    }

    private static string CreateProject(string parent, bool longNeighbours = false, bool reviewedFirstTwo = false, bool tallThird = false)
    {
        var folder = ProjectFolder.Create(parent, new ProjectConfig(
            DemoProject.Name,
            new GameConfig(@"D:\Jogos\MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default));
        folder.SaveFormatting(FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: 40, MaxLines: 3, CharWidth: CharWidthMode.Uniform) });
        folder.SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "animada"), new CharacterEntry("健太", "Kenta")]);
        folder.SaveText(
            "yst00001",
            tallThird
            ?
            [
                Line("yst00001", 1, "健太", @"担任の先生が腕を組んで立っていた。\p", LongText + @"\p"),
                Line("yst00001", 2, "美咲", @"「うん」\p", @"Aham.\p"),
                Line("yst00001", 3, "健太", @"担任の先生が腕を組んで立っていた。\p", LongText + " " + LongText + @"\p"),
                Line("yst00001", 4, "美咲", @"「うん」\p", @"Aham.\p"),
            ]
            : longNeighbours
            ?
            [
                Line("yst00001", 1, "健太", @"担任の先生が腕を組んで立っていた。\p", LongText + @"\p", reviewedFirstTwo ? TranslationStatus.Approved : TranslationStatus.Machine),
                Line("yst00001", 2, "健太", @"担任の先生が腕を組んで立っていた。\p", LongText + @"\p", reviewedFirstTwo ? TranslationStatus.Approved : TranslationStatus.Machine),
                Line("yst00001", 3, "健太", @"担任の先生が腕を組んで立っていた。\p", LongText + @"\p"),
                Line("yst00001", 4, "美咲", @"「うん」\p", @"Aham.\p"),
            ]
            :
            [
                Line("yst00001", 1, "美咲", @"「平気平気！　ほら、行こう！」\p", @"Tranquilo! Vamos, anda!\p"),
                Line("yst00001", 2, "健太", @"担任の先生が腕を組んで立っていた。\p", LongText + @"\p"),
                Line("yst00001", 3, "美咲", @"「うん」\p", @"Aham.\p"),
            ]);
        folder.SaveText("yst00002", [Line("yst00002", 1, "美咲", @"「そう」\p", @"Isso.\p")]);
        return folder.Root;
    }

    private static TextUnit Line(string script, int number, string speaker, string source, string translation, TranslationStatus status = TranslationStatus.Machine) =>
        new($"{script}:{number:D6}", TextUnitKind.Dialogue, speaker, source, translation, status);
}
