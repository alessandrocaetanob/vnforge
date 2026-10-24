using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// A Conferência com o QA de verdade sobre um projeto pequeno escrito para casar com as linhas do artboard "Conferir": marcadores perdidos, caixa
/// de quatro linhas, espaço duplo, espaço antes de pontuação, glossário e a mesma fala traduzida de três jeitos. Gera os PNG que vão ao lado do
/// artboard no PR: a primeira linha selecionada (erro) e a linha com correção proposta (Aplicar correção).
/// </summary>
public sealed class ConferenceRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_a_Conferencia_do_artboard_com_erro_selecionado_e_com_correcao_proposta(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = ArtboardProject.Create(temp.SubFolder("projeto"));

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

                    // O primeiro problema da lista: erro, sem correção automática.
                    Assert.Equal("marcador-diferente", vm.Conference.SelectedRow?.RuleId);
                    Save(window, "conferir-artboard", theme);

                    // O espaço duplo da mesma linha traz a correção proposta e o botão "Aplicar correção".
                    vm.Conference.SelectedRow = vm.Conference.Rows.First(r => r.RuleId == "espaco-duplo");
                    Assert.True(vm.Conference.SelectedRow.HasProposal);
                    Save(window, "conferir-correcao", theme);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static void Save(MainWindow window, string name, ThemeVariant theme)
    {
        ScreenRenderer.Settle(window);
        var path = RenderOutput.PathFor(ScreenRenderer.FileName(name, theme));
        ScreenRenderer.Save(window, path);
        using var png = new Bitmap(path);
        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
    }

    /// <summary>As linhas do artboard "Conferir", escritas para ele: nenhum arquivo ou texto de jogo.</summary>
    private static class ArtboardProject
    {
        public static string Create(string parent)
        {
            var folder = ProjectFolder.Create(parent, new ProjectConfig(
                DemoProject.Name,
                new GameConfig(@"D:\Jogos\MinhaVN", "MinhaVN.exe"),
                new EngineConfig("yuris", 500, "0xD36FAC96"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));
            folder.SaveFormatting(FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: 40, MaxLines: 3) });
            folder.SaveGlossary([new GlossaryTerm("先輩", "veterano"), new GlossaryTerm("担任の先生", "professor responsável")]);
            folder.SaveText("yst00012", [Line("yst00012", 42, @"「本当に、いいの？」\p", "De verdade, tudo bem?")]);
            folder.SaveText(
                "yst00013",
                [
                    Line("yst00013", 43, @"「……いいのか？\n濡れるぞ、お前」\p", "…Tem certeza?  Você vai se molhar."),
                    Line("yst00013", 44, @"「平気平気！　ほら、行こう！」\p", @"Tranquilo! Vamos, anda !\p"),
                    Line("yst00013", 45, @"担任の先生が腕を組んで立っていた。\p", @"O nosso professor responsável já estava de pé, esperando por nós dois de braços cruzados na frente do portão da escola.\p"),
                ]);
            folder.SaveText(
                "yst00014",
                [
                    Line("yst00014", 128, @"「了解」\p", @"Beleza.\p"),
                    Line("yst00014", 129, @"「了解」\p", @"Entendido.\p"),
                    Line("yst00014", 130, @"「了解」\p", @"Certo.\p"),
                ]);
            folder.SaveText("yst00020", [Line("yst00020", 311, @"「先輩、待ってください！」\p", @"Sênior, espera aí!\p")]);
            folder.SaveText(
                "yst00031",
                [
                    Line("yst00031", 102, @"「『暁の書』って知ってる？」\p", @"Você conhece o Livro da Aurora ★?\p"),
                    new TextUnit("yst00031:000103", TextUnitKind.Dialogue, null, "「まだだよ」\\p", null, TranslationStatus.Untranslated),
                    new TextUnit("yst00031:000104", TextUnitKind.Dialogue, null, "「行こう」\\p", null, TranslationStatus.Untranslated),
                ]);
            return folder.Root;
        }

        private static TextUnit Line(string key, int number, string source, string translation) =>
            new($"{key}:{number:D6}", TextUnitKind.Dialogue, null, source, translation, TranslationStatus.Translated);
    }
}
