using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Rendering;

/// <summary>A área Personagens (#558) montada de verdade nos dois temas a 1440×1000, dentro do shell: a lista com filtro e a página de um personagem. Os PNG seguem <c>VNFORGE_RENDER_DIR</c>.</summary>
public sealed class CharactersRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static ThemeVariant ThemeOf(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    private async Task Abrir(ThemeVariant theme, Func<MainWindow, MainWindowViewModel, Task> verificar)
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                await CharactersSamples.SeedAsync(config.Path, matched: true, showSpoilers: true, TestContext.Current.CancellationToken);
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(config.Path), new FixedClock(CharactersSamples.Now));
                var vm = new MainWindowViewModel(start);
                vm.Characters.ConfigDirectory = config.Path;
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Characters);
                    await vm.Characters.Loading;
                    ScreenRenderer.Settle(window);
                    await verificar(window, vm);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static CharactersView Page(Window window) => Assert.Single(window.GetVisualDescendants().OfType<CharactersView>(), v => v.IsEffectivelyVisible);

    private static List<string?> Texts(Avalonia.Visual root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text)];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_lista_tem_o_filtro_por_papel_e_traco_a_busca_e_a_grade_como_no_quadro(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, (window, vm) =>
        {
            var page = Page(window);
            var texts = Texts(page);

            Assert.Contains("Personagens", texts);
            Assert.Contains("4 de 4 · só os dos jogos da sua Biblioteca", texts);
            Assert.Contains("Mostrar spoiler", texts);
            Assert.Equal(4, page.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("libcard") && b.IsEffectivelyVisible));
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("personagens-lista", theme)));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_pagina_do_personagem_tem_o_retrato_a_descricao_os_jogos_e_os_tracos_como_no_quadro(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, (window, vm) =>
        {
            vm.Characters.OpenCharacterCommand.Execute(vm.Characters.Items.Single(i => i.Name == "Hana Tsukino"));
            ScreenRenderer.Settle(window);
            var texts = Texts(Page(window));

            Assert.Contains("Hana Tsukino", texts);
            Assert.Contains("Aparece em", texts);
            Assert.Contains("Moon Flower", texts);
            Assert.Contains("Star Garden", texts);
            Assert.Equal("Personagens › Hana Tsukino", vm.Breadcrumb);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("personagens-pagina", theme)));
            return Task.CompletedTask;
        });
    }
}
