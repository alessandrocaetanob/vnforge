using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// A ficha do personagem (#469) montada de verdade nos dois temas a 1440×1000: abre pelo botão da linha, mostra gênero, cor, como fala, as cinco falas mais
/// longas e as linhas esperando revisão, e o botão "Revisar este personagem" abre o Modo revisão. Confere a tinta dentro de retângulos conhecidos (o ponto
/// de cor), não faixas de texto, porque a fonte do CI é outra. Os PNG seguem <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class CharacterSheetRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 20, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static TextUnit Line(int n, string source, string speaker, string? translation, TranslationStatus status) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, speaker, source, translation, status);

    private static string CreateProject(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001",
        [
            Line(0, "あ", "美咲", "Ah.", TranslationStatus.Machine),
            Line(1, "おはようございます、先輩", "美咲", "Bom dia, veterano.", TranslationStatus.Machine),
            Line(2, "遅いぞ", "健太", null, TranslationStatus.Untranslated),
            Line(3, "今日もいい天気ですね", "美咲", "Que dia lindo hoje.", TranslationStatus.Translated),
            Line(4, "ええ", "美咲", "Sim.", TranslationStatus.Machine),
            Line(5, "それはどういう意味ですか", "美咲", "O que isso quer dizer?", TranslationStatus.Machine),
            Line(6, "なるほど、そうですか", "美咲", null, TranslationStatus.Untranslated),
            Line(7, "ありがとう", "美咲", "Obrigada.", TranslationStatus.Approved),
        ]);
        folder.SaveCharacters(
        [
            new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "animada, fala com educação", "violet"),
            new CharacterEntry("健太", "Kenta"),
        ]);
        return root;
    }

    private static string VisibleText(Control root) =>
        string.Join('\n', root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_ficha_abre_pelo_botao_da_linha_e_mostra_o_que_se_sabe_do_personagem(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                using var project = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(CreateProject(project)), new FixedClock(Now));
                var vm = new MainWindowViewModel(start);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Glossary);
                    await vm.Glossary.Loading;
                    ScreenRenderer.Settle(window);

                    Assert.DoesNotContain(window.GetVisualDescendants().OfType<CharacterSheetView>(), v => v.IsEffectivelyVisible);
                    var open = window.GetVisualDescendants().OfType<Button>()
                        .Where(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Abrir a ficha do personagem").ToList();
                    Assert.Equal(2, open.Count);
                    var misaki = open.Single(b => ((CharacterRow)b.DataContext!).Source == "美咲");
                    Assert.True(misaki.Command!.CanExecute(misaki.CommandParameter));
                    misaki.Command.Execute(misaki.CommandParameter);
                    ScreenRenderer.Settle(window);

                    var view = Assert.Single(window.GetVisualDescendants().OfType<CharacterSheetView>(), v => v.IsEffectivelyVisible);
                    Assert.True(view.Bounds.Width > 600 && view.Bounds.Right <= ScreenRenderer.Width, "a ficha cabe na janela");
                    var texts = VisibleText(view);
                    Assert.Contains("Misaki", texts, StringComparison.Ordinal);
                    Assert.Contains("美咲", texts, StringComparison.Ordinal);
                    Assert.Contains("Feminino", texts, StringComparison.Ordinal);
                    Assert.Contains("Violeta", texts, StringComparison.Ordinal);
                    Assert.Contains("animada, fala com educação", texts, StringComparison.Ordinal);
                    Assert.Contains("それはどういう意味ですか", texts, StringComparison.Ordinal);
                    Assert.Contains("O que isso quer dizer?", texts, StringComparison.Ordinal);
                    Assert.Contains("Ainda sem tradução", texts, StringComparison.Ordinal);
                    Assert.Contains("4 linhas da IA esperando revisão.", texts, StringComparison.Ordinal);
                    Assert.Equal(5, view.GetVisualDescendants().OfType<TextBlock>().Count(t => t.IsEffectivelyVisible && t.Classes.Contains("jp-row") && t.Text?.Length > 1 && t.Text != "美咲"));
                    Assert.DoesNotContain('—', texts);
                    Assert.DoesNotContain('–', texts);

                    // O ponto de cor tem a tinta do personagem neste tema.
                    var dot = view.GetVisualDescendants().OfType<Border>().First(b => b.IsEffectivelyVisible && Math.Abs(b.Bounds.Width - 12) < 0.5 && Math.Abs(b.Bounds.Height - 12) < 0.5 && b.CornerRadius.TopLeft == 6);
                    var ink = CharacterPalette.Find("violet")!.BrushFor(theme) is Avalonia.Media.ISolidColorBrush solid ? solid.Color : default;
                    var probe = PixelProbe.Capture(window, dot);
                    Assert.True(probe.Near(probe.Width / 2, probe.Height / 2, ink, 6), $"ponto de cor: {probe.At(probe.Width / 2, probe.Height / 2)} esperada {ink}");

                    var clipped = ClippedTextHeadlessTests.FindClipped(view);
                    Assert.True(clipped.Count == 0, string.Join("; ", clipped));

                    var review = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "Revisar este personagem");
                    Assert.True(review.IsEffectivelyEnabled && review.Classes.Contains("accent"));
                    var corner = review.TranslatePoint(new Point(review.Bounds.Width, review.Bounds.Height), window)!.Value;
                    Assert.True(corner.X <= ScreenRenderer.Width && corner.Y <= ScreenRenderer.Height, "o botão cabe na janela");

                    var path = RenderOutput.PathFor(ScreenRenderer.FileName("glossario-v5-ficha", theme));
                    ScreenRenderer.Save(window, path);
                    using (var png = new Bitmap(path))
                    {
                        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
                        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
                    }

                    // Revisar este personagem leva ao Modo revisão com as falas dele.
                    review.Command!.Execute(review.CommandParameter);
                    await vm.LineNavigation;
                    Assert.Equal(NavKey.Editor, vm.SelectedNav!.Key);
                    Assert.True(vm.Editor.IsReviewing);
                    Assert.Equal("Misaki", vm.Editor.Review!.SourceName);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fechar_a_ficha_pelo_botao_a_esconde_e_personagem_sem_linhas_da_IA_deixa_Revisar_desligado()
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                using var project = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(CreateProject(project)), new FixedClock(Now));
                var vm = new MainWindowViewModel(start);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = ThemeVariant.Dark, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Glossary);
                    await vm.Glossary.Loading;
                    vm.Glossary.OpenSheetCommand.Execute(vm.Glossary.Characters.Single(c => c.Source == "健太"));
                    ScreenRenderer.Settle(window);

                    var view = Assert.Single(window.GetVisualDescendants().OfType<CharacterSheetView>(), v => v.IsEffectivelyVisible);
                    var review = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "Revisar este personagem");
                    Assert.False(review.IsEffectivelyEnabled);
                    Assert.Contains("Nenhuma linha da IA esperando revisão.", VisibleText(view), StringComparison.Ordinal);

                    var close = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "Fechar a ficha");
                    close.Command!.Execute(close.CommandParameter);
                    ScreenRenderer.Settle(window);

                    Assert.DoesNotContain(window.GetVisualDescendants().OfType<CharacterSheetView>(), v => v.IsEffectivelyVisible);
                    Assert.Null(vm.Glossary.Sheet);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }
}
