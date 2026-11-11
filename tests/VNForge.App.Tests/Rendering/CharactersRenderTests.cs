using Avalonia;
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

    private async Task Abrir(ThemeVariant theme, Func<MainWindow, MainWindowViewModel, Task> verificar, bool manyTraits = false)
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                await CharactersSamples.SeedAsync(config.Path, matched: true, showSpoilers: true, TestContext.Current.CancellationToken, manyTraits);
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
            var facet = vm.Characters.TraitFacets[0];
            facet.IsSelected = true;
            ScreenRenderer.Settle(window);
            var chip = Assert.Single(page.GetVisualDescendants().OfType<VNForge.App.Controls.FilterChip>(), c => c.IsEffectivelyVisible && c.Removable);
            Assert.Equal(32, chip.Bounds.Height, 0.5);
            Assert.True(chip.IsChecked);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("personagens-lista", theme)));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Com_mais_de_seis_tracos_o_link_Ver_os_N_tracos_e_uma_linha_de_texto_e_nao_empurra_o_resto(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, (window, vm) =>
        {
            var page = Page(window);
            var link = page.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "MoreTraits" && b.IsEffectivelyVisible);
            var linkBottom = link.TranslatePoint(default, page)!.Value.Y + link.Bounds.Height;
            var divider = page.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Height == 1 && b.IsEffectivelyVisible && b.TranslatePoint(default, page)!.Value.Y > linkBottom - 1)
                .MinBy(b => b.TranslatePoint(default, page)!.Value.Y)!;

            // No quadro o link é uma linha de texto (20 px) e o divisor vem 16 px depois dele.
            Assert.Equal(20, link.Bounds.Height, 0.5);
            Assert.Equal(16, divider.TranslatePoint(default, page)!.Value.Y - linkBottom, 0.5);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("personagens-lista-sete-tracos", theme)));
            return Task.CompletedTask;
        }, manyTraits: true);
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
            Assert.Contains("Aparência", texts);
            Assert.Contains("Personalidade", texts);
            var traits = Page(window).GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TraitsCard");
            // A coluna da direita é fixa em 540 mesmo sem o cartão Na tradução (que só existe com projeto): o Traços fica com o resto, 740.
            Assert.Equal(740, traits.Bounds.Width, 1.5);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("personagens-pagina", theme)));

            vm.Characters.ShowSpoilers = false;
            ScreenRenderer.Settle(window);
            texts = Texts(Page(window));

            Assert.Contains("Spoiler", texts);
            Assert.Contains("Nível 2", texts);
            Assert.Contains("1 traço com spoiler", texts);
            Assert.DoesNotContain("Escondidos", texts);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("personagens-pagina-spoiler", theme)));
            return Task.CompletedTask;
        });
    }
}
