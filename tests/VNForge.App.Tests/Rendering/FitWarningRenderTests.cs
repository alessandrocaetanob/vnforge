using Avalonia;
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
/// O aviso "Não cabe na caixa do jogo" com "Dividir em páginas" e "Encurtar com a IA" no Editor e na Conferência, a 1440×1000 nos dois temas. O projeto
/// tem uma fala de quatro linhas numa caixa de três; os PNG (<c>VNFORGE_RENDER_DIR</c>) vão ao lado do artboard no PR. A conferência é pela cor do fundo
/// do aviso num ponto conhecido do controle, não por faixas de pixels de texto, porque a fonte muda de um sistema para outro.
/// </summary>
public sealed class FitWarningRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const string Script = "yst00013";
    private const string LongLine = "yst00013:000045";
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static readonly string LongText = "O nosso professor responsável já estava de pé, esperando por nós dois de braços cruzados na frente do portão da escola, debaixo da chuva.";

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Editor_mostra_o_aviso_e_depois_de_dividir_a_caixa_em_duas_paginas(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await vm.Editor.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
                    Assert.True(await vm.Editor.GoToLineAsync(Script, LongLine));

                    var fit = vm.Editor.Detail!.Fit!;
                    Assert.True(fit.IsVisible);
                    Assert.Equal("Dividir em 2 páginas", fit.SplitText);
                    ScreenRenderer.Settle(window);
                    ConferirAviso(window, theme, fit.Message);
                    Save(window, "editor-caixa-nao-cabe", theme);

                    await fit.SplitCommand.ExecuteAsync(null);
                    ScreenRenderer.Settle(window);

                    Assert.False(fit.IsVisible);
                    Assert.Contains(@"\p", vm.Editor.Detail.Translation);
                    Assert.DoesNotContain(window.GetVisualDescendants().OfType<FitWarning>(), w => w.IsEffectivelyVisible);
                    Save(window, "editor-caixa-dividida", theme);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Conferencia_mostra_o_aviso_no_problema_de_caixa(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
                    await vm.ConferenceLoading;

                    vm.Conference.SelectedRow = vm.Conference.Rows.Single(r => r.RuleId == "caixa-acima-do-limite");
                    var fit = vm.Conference.SelectedFit!;
                    Assert.True(fit.IsVisible);
                    ScreenRenderer.Settle(window);
                    ConferirAviso(window, theme, fit.Message);
                    Save(window, "conferir-caixa-nao-cabe", theme);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>O aviso está na tela, dentro da janela, com os dois botões, e o fundo dele (num ponto de dentro do infobar) é o do aviso do tema.</summary>
    private static void ConferirAviso(Avalonia.Controls.Window window, ThemeVariant theme, string expectedMessage)
    {
        var warning = Assert.Single(window.GetVisualDescendants().OfType<FitWarning>(), w => w.IsEffectivelyVisible);
        Assert.Equal(expectedMessage, warning.Message);
        Assert.NotNull(warning.SplitCommand);
        Assert.NotNull(warning.ShortenCommand);

        var origin = warning.TranslatePoint(default, window) ?? throw new InvalidOperationException("O aviso não está na janela.");
        Assert.True(origin.X >= 0 && origin.Y >= 0 && origin.X + warning.Bounds.Width <= window.Width && origin.Y + warning.Bounds.Height <= window.Height, "o aviso cabe na janela");
        Assert.All(warning.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Where(b => b.IsEffectivelyVisible), button => Assert.True(button.Bounds.Width > 40 && button.Bounds.Height > 16));

        var probe = PixelProbe.Capture(window, warning);
        var expected = theme == ThemeVariant.Light ? Color.Parse("#FFF1D6") : Color.Parse("#3A2C10");
        Assert.True(probe.Near(10, 6, expected, 6), $"o fundo do aviso é o do tema ({probe.At(10, 6)} em vez de {expected})");
    }

    private static void Save(Avalonia.Controls.Window window, string name, ThemeVariant theme)
    {
        ScreenRenderer.Settle(window);
        ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName(name, theme)));
    }

    private static string CreateProject(string parent)
    {
        var folder = ProjectFolder.Create(parent, new ProjectConfig(
            DemoProject.Name,
            new GameConfig(@"D:\Jogos\MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default));
        folder.SaveFormatting(FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: 40, MaxLines: 3, CharWidth: CharWidthMode.Uniform) });
        folder.SaveText(
            Script,
            [
                Line(44, @"「平気平気！　ほら、行こう！」\p", @"Tranquilo! Vamos, anda!\p"),
                Line(45, @"担任の先生が腕を組んで立っていた。\p", LongText + @"\p"),
            ]);
        return folder.Root;
    }

    private static TextUnit Line(int number, string source, string translation) =>
        new($"{Script}:{number:D6}", TextUnitKind.Dialogue, null, source, translation, TranslationStatus.Translated);
}
