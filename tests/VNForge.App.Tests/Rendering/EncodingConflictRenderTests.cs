using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O ajuste de formatação que a estratégia de acentuação não grava, nas duas telas: o card da Conferência no topo da lista de erros e a opção desligada
/// (com a faixa de aviso) em Ajustes, aba Formatação. Gera os PNG que vão ao PR, nos dois temas.
/// </summary>
public sealed class EncodingConflictRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static string CreateProject(TempFolder temp, EllipsisStyle ellipsis)
    {
        var folder = ProjectFolder.Create(temp.SubFolder("projeto"), new ProjectConfig(
            "Manhã de chuva",
            new GameConfig("D:/Jogos/Exemplo", "Exemplo.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default));
        folder.SaveFormatting(FormattingProfile.Default with { Typography = FormattingProfile.Default.Typography with { Ellipsis = ellipsis } });
        string[] lines = ["Espera... eu já vou.", "Hum... não sei se é boa ideia.", "Ah... então era isso.", "Você disse... o quê?", "Bom dia,  pessoal."];
        folder.SaveText("yst00001", [.. lines.Select((text, i) => new TextUnit($"yst00001:{i:D6}", TextUnitKind.Dialogue, null, $"原文{i}", text, TranslationStatus.Translated))]);
        return folder.Root;
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_o_card_do_ajuste_no_topo_da_lista_de_erros(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp, EllipsisStyle.Character);

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
                    ScreenRenderer.Settle(window);

                    var card = Assert.Single(vm.Conference.SettingCards);
                    Assert.Equal("O ajuste Reticências gera 4 erros", card.Title);
                    var view = window.GetVisualDescendants().OfType<ConferenceView>().Single();
                    var button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.Conference.ApplySettingCommand && b.IsEffectivelyVisible);
                    Assert.Contains("accent", button.Classes);
                    Assert.Equal("Usar três pontos", button.GetVisualDescendants().OfType<TextBlock>().Single().Text);
                    Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
                    var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
                    Assert.Contains(card.Title, texts);
                    Assert.Contains(card.Text, texts);
                    Assert.DoesNotContain(texts, t => t is not null && t.StartsWith("O ajuste Reticências (", StringComparison.Ordinal));

                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("conferir-ajuste-card", theme)));
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
    public async Task Renderiza_Ajustes_com_a_opcao_desligada_e_a_faixa_de_aviso(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp, EllipsisStyle.Dots);

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
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Settings);
                    await vm.Settings.EstimateLoading;
                    vm.Settings.SelectedTab = 2;
                    ScreenRenderer.Settle(window);

                    // Com a estratégia cp1252 a opção "Caractere …" fica desligada, com a dica, e nenhuma faixa aparece: o ajuste escolhido é gravado.
                    var view = window.GetVisualDescendants().OfType<SettingsView>().Single();
                    var character = Segment(view, "Reticências", "Caractere …");
                    Assert.False(character.IsEffectivelyEnabled);
                    Assert.Equal("A estratégia cp1252 não grava as reticências (…).", ToolTip.GetTip(character));
                    Assert.True(Segment(view, "Reticências", "Três pontos").IsEffectivelyEnabled);
                    Assert.False(Segment(view, "Aspas curvas", "Aspas 「 」").IsEffectivelyEnabled);
                    Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.Contains("não grava", StringComparison.Ordinal) == true && t.Classes.Contains("body-sm"));
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("ajustes-formatacao-opcao-desligada", theme)));

                    // A estratégia trocada com o ajuste "Caractere …" escolhido levanta a faixa e o botão que troca o ajuste.
                    vm.Settings.EncodingStrategy = "sjis";
                    vm.Settings.SelectedEllipsis = vm.Settings.EllipsisStyles.Single(e => e.Value == EllipsisStyle.Character);
                    vm.Settings.EncodingStrategy = "cp1252";
                    ScreenRenderer.Settle(window);

                    Assert.True(vm.Settings.HasEncodingConflicts);
                    var button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.Settings.EncodingConflicts[0].FixCommand && b.IsEffectivelyVisible);
                    Assert.Equal("Usar três pontos", button.GetVisualDescendants().OfType<TextBlock>().Single().Text);
                    Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "A estratégia cp1252 não grava as reticências (…).");
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("ajustes-formatacao-faixa-de-aviso", theme)));
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static ListBoxItem Segment(SettingsView view, string group, string label)
    {
        var list = view.GetVisualDescendants().OfType<ListBox>().Single(l => Avalonia.Automation.AutomationProperties.GetName(l) == group);
        return list.GetVisualDescendants().OfType<ListBoxItem>().Single(item => item.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == label));
    }
}
