using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Rendering;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class TextAppearanceSettingsTests
{
    private static SettingsViewModel Settings(AppSettingsFile file) => new(new MemoryApiKeyStore(), file, _ => null);

    [Fact]
    public void Padrao_e_100_por_cento_com_a_fonte_do_app_e_o_arquivo_nao_grava_o_padrao()
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.SubFolder("config"));
        var loaded = file.Load();
        Assert.Equal(100, loaded.TextScalePercent);
        Assert.Equal(JapaneseFontChoice.Default, loaded.JapaneseFont);

        file.Save(loaded);

        var json = File.ReadAllText(file.Path);
        Assert.DoesNotContain("textScale", json);
        Assert.DoesNotContain("japaneseFont", json);
    }

    [Fact]
    public void Tamanho_e_fonte_vao_e_voltam_do_arquivo()
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.SubFolder("config"));

        file.Save(new AppSettings(2) { TextScalePercent = 150, JapaneseFont = JapaneseFontChoice.Meiryo });

        var loaded = file.Load();
        Assert.Equal(150, loaded.TextScalePercent);
        Assert.Equal(JapaneseFontChoice.Meiryo, loaded.JapaneseFont);
    }

    [Theory]
    [InlineData("""{ "concurrency": 4, "textScale": 137, "japaneseFont": "papyrus" }""")]
    [InlineData("""{ "concurrency": 4, "textScale": "150", "japaneseFont": 3 }""")]
    [InlineData("""{ "concurrency": 4, "textScale": null }""")]
    public void Valor_invalido_volta_ao_padrao_sem_perder_o_resto(string json)
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.SubFolder("config"));
        File.WriteAllText(file.Path, json);

        var loaded = file.Load();

        Assert.Equal(4, loaded.Concurrency);
        Assert.Equal(100, loaded.TextScalePercent);
        Assert.Equal(JapaneseFontChoice.Default, loaded.JapaneseFont);
    }

    [Fact]
    public void Aumentar_e_diminuir_andam_pelos_tamanhos_param_nas_pontas_e_gravam()
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.SubFolder("config"));
        var settings = Settings(file);
        var raised = 0;
        settings.TextAppearanceChanged += (_, _) => raised++;
        Assert.Equal([90, 100, 110, 125, 150], settings.TextSizes.Select(t => t.Value));

        settings.IncreaseTextSizeCommand.Execute(null);
        Assert.Equal(110, settings.TextScalePercent);
        Assert.Equal(110, file.Load().TextScalePercent);

        for (var i = 0; i < 5; i++)
        {
            settings.IncreaseTextSizeCommand.Execute(null);
        }

        Assert.Equal(150, settings.TextScalePercent);
        Assert.Equal(3, raised);

        for (var i = 0; i < 9; i++)
        {
            settings.DecreaseTextSizeCommand.Execute(null);
        }

        Assert.Equal(90, settings.TextScalePercent);
        Assert.Equal(90, file.Load().TextScalePercent);

        settings.ResetTextSizeCommand.Execute(null);
        Assert.Equal(100, settings.TextScalePercent);
        Assert.Equal(100, file.Load().TextScalePercent);
    }

    [Fact]
    public void Escolher_a_fonte_do_japones_grava_avisa_e_nao_perde_o_tamanho()
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.SubFolder("config"));
        var settings = Settings(file);
        var raised = 0;
        settings.TextAppearanceChanged += (_, _) => raised++;
        settings.IncreaseTextSizeCommand.Execute(null);

        settings.SelectedJapaneseFont = settings.JapaneseFonts.Single(f => f.Value == JapaneseFontChoice.YuGothic);

        Assert.Equal(2, raised);
        var loaded = file.Load();
        Assert.Equal(JapaneseFontChoice.YuGothic, loaded.JapaneseFont);
        Assert.Equal(110, loaded.TextScalePercent);
        Assert.Equal(JapaneseFontChoice.YuGothic, Settings(file).JapaneseFont);
    }

    [Fact]
    public void Mudar_a_concorrencia_ou_o_tema_nao_perde_o_tamanho_do_texto()
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.SubFolder("config"));
        var settings = Settings(file);
        settings.DecreaseTextSizeCommand.Execute(null);

        settings.Concurrency = 5;
        settings.SelectedTheme = settings.Themes.Single(t => t.Value == ThemeChoice.Dark);

        Assert.Equal(90, file.Load().TextScalePercent);
    }

    [Fact]
    public void Build_multiplica_tamanho_e_altura_de_linha_e_a_linha_da_grade()
    {
        var at100 = TextAppearance.Build(100);
        var at150 = TextAppearance.Build(150);

        Assert.Equal(13d, at100[TextAppearance.FontSizeKey("BodySm")]);
        Assert.Equal(19.5, at150[TextAppearance.FontSizeKey("BodySm")]);
        Assert.Equal(27d, at150[TextAppearance.LineHeightKey("BodySm")]);
        Assert.Equal(36d, at100["Vnf.RowHeight"]);
        Assert.Equal(54d, at150["Vnf.RowHeight"]);
        Assert.All(TextAppearance.Scales, s => Assert.True(
            (double)at150[TextAppearance.LineHeightKey(s.Name)]! >= (double)at150[TextAppearance.FontSizeKey(s.Name)]!, s.Name));
    }

    [Fact]
    public void Familia_do_japones_poe_a_escolhida_antes_da_noto_embutida()
    {
        Assert.Equal("Noto Sans JP", TextAppearance.JapaneseFamily(JapaneseFontChoice.Default).FamilyNames[0]);
        var meiryo = TextAppearance.JapaneseFamily(JapaneseFontChoice.Meiryo);
        Assert.Equal("Meiryo UI", meiryo.FamilyNames[0]);
        Assert.Equal("Noto Sans JP", meiryo.FamilyNames[1]);
    }
}

/// <summary>O Ctrl + e o Ctrl - mudam o texto do Editor; as outras páginas, o trilho e a barra de status seguem em 100%.</summary>
public class TextAppearanceHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private Task OnUi(Func<Task> test) => fixture.Session.Dispatch<int>(
        async () =>
        {
            await test();
            return 0;
        },
        TestContext.Current.CancellationToken);

    [Fact]
    public Task Tokens_do_tema_sao_os_de_100_por_cento() => OnUi(() =>
    {
        var app = Application.Current!;
        foreach (var (name, size, line) in TextAppearance.Scales)
        {
            Assert.True(app.TryGetResource(TextAppearance.FontSizeKey(name), null, out var s), name);
            Assert.True(app.TryGetResource(TextAppearance.LineHeightKey(name), null, out var l), name);
            Assert.Equal(size, s);
            Assert.Equal(line, l);
        }

        Assert.True(app.TryGetResource("Vnf.RowHeight", null, out var row));
        Assert.Equal(36d, row);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Ctrl_mais_e_Ctrl_menos_mudam_o_texto_do_Editor_e_so_dele() => OnUi(async () =>
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Machine, TranslationStatus.Machine);
        var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), TimeProvider.System);
        var vm = new MainWindowViewModel(start, settings: new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("app")), _ => null));
        var window = new MainWindow { Width = 1440, Height = 1000, DataContext = vm };
        try
        {
            window.Show();
            await start.OpenFolderCommand.ExecuteAsync(null);
            await vm.ProjectOpening;
            await vm.Editor.Loading;
            vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
            ScreenRenderer.Settle(window);

            var before = EditorRowFontSize(window);
            Assert.Equal(13d, before);

            window.KeyPress(Key.OemPlus, RawInputModifiers.Control, PhysicalKey.Equal, "=");
            ScreenRenderer.Settle(window);
            Assert.Equal(110, vm.Settings.TextScalePercent);
            Assert.Equal(14.3, EditorRowFontSize(window));

            window.KeyPress(Key.OemMinus, RawInputModifiers.Control, PhysicalKey.Minus, "-");
            window.KeyPress(Key.OemMinus, RawInputModifiers.Control, PhysicalKey.Minus, "-");
            ScreenRenderer.Settle(window);
            Assert.Equal(90, vm.Settings.TextScalePercent);
            Assert.Equal(11.7, EditorRowFontSize(window));

            window.KeyPress(Key.D0, RawInputModifiers.Control, PhysicalKey.Digit0, "0");
            ScreenRenderer.Settle(window);
            Assert.Equal(before, EditorRowFontSize(window));

            var caption = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("caption") && t.Text == vm.Nav[0].Label);
            var captionBefore = caption.FontSize;
            vm.Settings.IncreaseTextSizeCommand.Execute(null);
            vm.Settings.IncreaseTextSizeCommand.Execute(null);
            vm.Settings.IncreaseTextSizeCommand.Execute(null);
            ScreenRenderer.Settle(window);
            Assert.Equal(150, vm.Settings.TextScalePercent);
            Assert.Equal(captionBefore, caption.FontSize);

            vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Settings);
            ScreenRenderer.Settle(window);
            var bodySm = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.Classes.Contains("body-sm"));
            Assert.Equal(13d, bodySm.FontSize);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Fonte_do_japones_escolhida_chega_ao_texto_original() => OnUi(async () =>
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Machine);
        var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), TimeProvider.System);
        var vm = new MainWindowViewModel(start, settings: new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("app")), _ => null));
        var window = new MainWindow { Width = 1440, Height = 1000, DataContext = vm };
        try
        {
            window.Show();
            await start.OpenFolderCommand.ExecuteAsync(null);
            await vm.ProjectOpening;
            await vm.Editor.Loading;
            vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
            ScreenRenderer.Settle(window);

            var original = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.Classes.Contains("jp-row"));
            Assert.Equal("Noto Sans JP", original.FontFamily.FamilyNames[0]);

            vm.Settings.SelectedJapaneseFont = vm.Settings.JapaneseFonts.Single(f => f.Value == JapaneseFontChoice.Meiryo);
            ScreenRenderer.Settle(window);

            Assert.Equal("Meiryo UI", original.FontFamily.FamilyNames[0]);
        }
        finally
        {
            window.Close();
        }
    });

    private static double EditorRowFontSize(Window window) =>
        window.GetVisualDescendants().OfType<LineRow>().First(r => r.IsEffectivelyVisible)
            .GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("jp-row")).FontSize;
}
