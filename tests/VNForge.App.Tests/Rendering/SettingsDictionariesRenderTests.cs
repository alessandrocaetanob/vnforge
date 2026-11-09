using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Dictionaries;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// Renderiza a aba Dicionários dos Ajustes (<c>ajustes-dicionarios</c>) e o diálogo da regra (<c>ajustes-dicionarios-nova-regra</c>) com o projeto de
/// demonstração, a 1440×1000, nos dois temas, para pôr ao lado do quadro da #539.
/// </summary>
public sealed class SettingsDictionariesRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static DictionaryRules Rules() => new(
        [
            new DictionaryRule("ぼ、ぼく", "ぼく"),
            new DictionaryRule("せやな", "そうだな") { Conditions = [new(DictionaryConditionKind.Speaker, "Ren")] },
            new DictionaryRule("ありがとー", "ありがとう") { Enabled = false, Conditions = [new(DictionaryConditionKind.SourceContains, "ー")] },
        ],
        [
            new DictionaryRule("você", "tu") { Conditions = [new(DictionaryConditionKind.Speaker, "Ren"), new(DictionaryConditionKind.Scene, "Capítulo 1")] },
            new DictionaryRule("senhora Misaki", "professora Misaki") { Conditions = [new(DictionaryConditionKind.SourceContains, "先生")] },
            new DictionaryRule("Sakura-san", "Sakura"),
        ]);

    /// <summary>
    /// As linhas que o quadro mostra na prévia da regra: a 0301 do Ren no Capítulo 1 (a regra troca) e a 0214 da Aoi (o falante diferente impede a troca).
    /// A 0302 fica fora do Capítulo 1: é a que o diálogo conta como "não muda".
    /// </summary>
    private static void SeedCapitulo1(ProjectFolder folder)
    {
        folder.SaveScriptNames(ScriptNames.From([new ScriptNameEntry("cap1", null, "Capítulo 1")]));
        folder.SaveText(
            "cap1",
            [
                Line("cap1:000301", "Ren", "先生、あいつは大丈夫でしょうか", "Professor, será que você vai dar conta?"),
                Line("cap1:000214", "Aoi", "君は早いね", "Você chegou cedo hoje, mas você some depois."),
            ]);
        folder.SaveText("cap2", [Line("cap2:000302", "Ren", "先生、あいつは大丈夫でしょうか", "Professor, você sabe onde ele foi?")]);
    }

    private static TextUnit Line(string id, string speaker, string source, string translation) =>
        new(id, TextUnitKind.Dialogue, speaker, source, translation, TranslationStatus.Translated);

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_a_aba_Dicionarios_e_o_dialogo_da_regra_em_1440_por_1000(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var demo = demoFolder;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                var folder = ProjectFolder.Open(demo.Root);
                folder.SaveFormatting(folder.LoadFormatting() with { Dictionary = Rules() });
                SeedCapitulo1(folder);
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demo.Root), new FixedClock(Now));
                var vault = new MemoryApiKeyStore();
                vault.Save("sk-ant-chave-da-demonstracao");
                var settings = new SettingsViewModel(vault, new AppSettingsFile(config.SubFolder("ajustes")), _ => null, _ => throw new InvalidOperationException("a renderização não chama a API"));
                var vm = new MainWindowViewModel(start, new ResolvedApiKey("sk-ant-chave-da-demonstracao", ApiKeySource.Vault), settings: settings);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Settings);
                    await vm.Settings.EstimateLoading;
                    await vm.Settings.DictionaryCounting;
                    vm.Settings.SelectedTab = 4;
                    ScreenRenderer.Settle(window);

                    var path = RenderOutput.PathFor(ScreenRenderer.FileName("ajustes-dicionarios", theme));
                    ScreenRenderer.Save(window, path);
                    using (var png = new Bitmap(path))
                    {
                        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
                        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
                    }

                    var texts = VisibleTexts(window);
                    Assert.Contains("Pré-dicionário · antes da IA", texts);
                    Assert.Contains("Pós-dicionário · depois da IA", texts);
                    Assert.Contains("Falante: Ren", texts);
                    Assert.Contains("Cena: Capítulo 1", texts);
                    Assert.Contains("Original contém 先生", texts);
                    Assert.Contains("Sempre", texts);
                    Assert.Contains("Prévia da regra", texts);
                    Assert.Contains("Pré-dicionário muda o cache", texts);
                    Assert.Equal(2, window.GetVisualDescendants().OfType<Button>().Count(b => b.IsEffectivelyVisible && AutomationName(b).StartsWith("Adicionar regra ao", StringComparison.Ordinal)));
                    Assert.Equal(6, window.GetVisualDescendants().OfType<ToggleSwitch>().Count(t => t.IsEffectivelyVisible && AutomationName(t) == "Regra ligada"));

                    vm.Settings.AddPostRuleCommand.Execute(null);
                    var editor = vm.Settings.Editor!;
                    editor.Pattern = "você";
                    editor.With = "tu";
                    editor.AddConditionCommand.Execute(null);
                    editor.Conditions[0].Value = "Ren";
                    editor.AddConditionCommand.Execute(null);
                    editor.Conditions[1].KindIndex = 1;
                    editor.Conditions[1].Value = "Capítulo 1";
                    editor.AddConditionCommand.Execute(null);
                    editor.Conditions[2].KindIndex = 2;
                    editor.Conditions[2].Value = "先生";
                    await vm.Settings.EditorImpactLoading;
                    ScreenRenderer.Settle(window);

                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("ajustes-dicionarios-nova-regra", theme)));
                    var layer = OverlayLayer.GetOverlayLayer(window)!;
                    var dialogTexts = layer.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!).ToList();
                    Assert.Contains("Nova regra do pós-dicionário", dialogTexts);
                    Assert.Contains("Só quando todas as condições baterem", dialogTexts);
                    Assert.Contains("Adicionar condição", dialogTexts);
                    Assert.Contains(dialogTexts, t => t.StartsWith("Aplica em", StringComparison.Ordinal));
                    Assert.Equal(["é", "está em", "contém"], editor.Conditions.Select(c => c.OperatorLabel));
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static string AutomationName(Control control) => Avalonia.Automation.AutomationProperties.GetName(control) ?? string.Empty;

    private static List<string> VisibleTexts(Window window) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!)];
}
