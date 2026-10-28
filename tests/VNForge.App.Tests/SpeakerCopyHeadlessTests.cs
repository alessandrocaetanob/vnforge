using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Tests.Rendering;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>
/// O nome de quem fala é texto selecionável, com "Copiar" em português no menu, na lista do Editor, no detalhe da linha e na Conferência
/// (o Modo revisão está em <c>ReviewModeRenderTests</c>), e o card do erro do falante cadastra o personagem.
/// </summary>
public sealed class SpeakerCopyHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const string Wide = "Ｃ氏";

    private const string Original = "犬井";

    private static void AssertCopyMenu(SelectableTextBlock block)
    {
        var flyout = Assert.IsAssignableFrom<MenuFlyout>(block.ContextFlyout);
        Assert.Contains(flyout.Items.OfType<MenuItem>(), i => Equals(i.Header, "Copiar nome original"));
    }

    /// <summary>Clique direito no centro do bloco, sem selecionar nada antes; devolve os itens que o menu mostra.</summary>
    private static IReadOnlyList<MenuItem> RightClick(Window window, SelectableTextBlock block)
    {
        var center = block.TranslatePoint(new Point(block.Bounds.Width / 2, block.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Right);
        window.MouseUp(center, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        var flyout = Assert.IsAssignableFrom<MenuFlyout>(block.ContextFlyout);
        Assert.True(flyout.IsOpen);
        return [.. flyout.Items.OfType<MenuItem>().Where(i => i.IsVisible)];
    }

    /// <summary>Escolhe o item do menu e devolve o que ficou na área de transferência.</summary>
    private static async Task<string?> Choose(Window window, SelectableTextBlock block, IReadOnlyList<MenuItem> items, string header)
    {
        var item = Assert.Single(items, i => Equals(i.Header, header));
        Assert.True(item.IsEnabled);
        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        ((MenuFlyout)block.ContextFlyout!).Hide();
        return await window.Clipboard!.TryGetTextAsync();
    }

    private static SelectableTextBlock SpeakerOf(Control root) => root.GetVisualDescendants().OfType<SelectableTextBlock>().Single(b => b.Name == "PART_Speaker");

    private static Window Show(Control content, int width = 1200, int height = 800)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        ScreenRenderer.Settle(window);
        return window;
    }

    private static ProjectFolder CreateCp1252Project(TempFolder temp, params TextUnit[] units)
    {
        var folder = ProjectFolder.Create(temp.SubFolder("vn"), new ProjectConfig(
            "Teste",
            new GameConfig("D:/Jogos/Teste", "Teste.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default));
        folder.SaveText("yst00001", units);
        return folder;
    }

    [Fact]
    public async Task LineRow_o_nome_e_selecionavel_e_clicar_nele_ainda_seleciona_a_linha()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var list = new ListBox { Classes = { "projects", "lines" } };
                list.Items.Add(new ListBoxItem { Content = new LineRow { Number = 1, Speaker = "Misaki", Original = "原文" } });
                list.Items.Add(new ListBoxItem { Content = new LineRow { Number = 2, Speaker = "Kenta", Original = "原文" } });
                var window = Show(list);

                var rows = list.GetVisualDescendants().OfType<LineRow>().ToList();
                var speaker = rows[1].GetVisualDescendants().OfType<SelectableTextBlock>().Single(b => b.Name == "PART_Speaker");
                Assert.Equal("Kenta", speaker.Text);
                AssertCopyMenu(speaker);

                var center = speaker.TranslatePoint(new Point(speaker.Bounds.Width / 2, speaker.Bounds.Height / 2), window)!.Value;
                window.MouseDown(center, MouseButton.Left);
                window.MouseUp(center, MouseButton.Left);

                Assert.Equal(1, list.SelectedIndex);
                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Detalhe_da_linha_mostra_o_falante_e_o_contexto_como_texto_selecionavel()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Machine);
        ProjectFolder.Open(root).SaveText("yst00001",
        [
            new TextUnit("yst00001:000000", TextUnitKind.Dialogue, "美咲", "原文", "Bom dia.", TranslationStatus.Approved),
            new TextUnit("yst00001:000001", TextUnitKind.Dialogue, "健太", "原文", "Oi.", TranslationStatus.Machine),
        ]);
        var vm = new EditorViewModel(postToUi: action => action());
        vm.Project = EditorFixtures.Summary("Minha VN", root);
        await vm.Loading;
        vm.SelectedLine = vm.Lines[1];

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(new LineDetailView { DataContext = vm.Detail }, 420, 1000);
                var blocks = window.GetVisualDescendants().OfType<SelectableTextBlock>().Where(b => b.IsEffectivelyVisible).ToList();

                var pair = Assert.Single(blocks, b => AutomationProperties.GetName(b) == "Falante");
                Assert.Equal("健太", pair.Text);
                AssertCopyMenu(pair);

                var context = Assert.Single(blocks, b => AutomationProperties.GetName(b) == "Quem fala no contexto");
                Assert.Contains("健太", string.Concat(context.Inlines!.OfType<Run>().Select(r => r.Text)));
                AssertCopyMenu(context);
                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Conferencia_mostra_quem_fala_com_o_nome_exato_e_o_botao_cadastra_o_personagem()
    {
        using var temp = new TempFolder();
        var folder = CreateCp1252Project(temp, new TextUnit("yst00001:000000", TextUnitKind.Dialogue, Wide, "原文", "Bom dia!", TranslationStatus.Translated));
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(folder.Root) };
        await vm.RefreshAsync();
        string? requested = null;
        vm.CharacterRegistrationRequested += (_, name) => requested = name;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(new ConferenceView { DataContext = vm });

                var speaker = Assert.Single(window.GetVisualDescendants().OfType<SelectableTextBlock>(), b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Quem fala");
                Assert.Equal(Wide, speaker.Text);
                AssertCopyMenu(speaker);

                var register = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == vm.RegisterCharacterCommand);
                Assert.Equal("Cadastrar personagem", AutomationProperties.GetName(register));
                register.Command!.Execute(register.CommandParameter);
                Assert.Equal(Wide, requested);
                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Conferencia_sem_erro_de_falante_nao_mostra_o_botao_de_cadastrar()
    {
        using var temp = new TempFolder();
        var folder = CreateCp1252Project(temp, new TextUnit("yst00001:000000", TextUnitKind.Dialogue, "Misaki", "原文", "duas  palavras", TranslationStatus.Translated));
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(folder.Root) };
        await vm.RefreshAsync();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(new ConferenceView { DataContext = vm });

                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == vm.RegisterCharacterCommand);
                Assert.Single(window.GetVisualDescendants().OfType<SelectableTextBlock>(), b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Quem fala");
                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task LineRow_clique_direito_sem_selecao_copia_o_nome_original_mesmo_com_a_traducao_na_grade()
    {
        await fixture.Session.Dispatch(
            async () =>
            {
                var window = Show(new LineRow { Number = 1, Speaker = "Inui", SpeakerOriginal = Original, Original = "原文" });
                var speaker = SpeakerOf(window);
                Assert.Equal("Inui", speaker.Text);
                Assert.True(string.IsNullOrEmpty(speaker.SelectedText));

                var items = RightClick(window, speaker);
                Assert.Equal(["Copiar nome original", "Copiar tradução"], items.Select(i => i.Header));
                Assert.Equal(Original, await Choose(window, speaker, items, "Copiar nome original"));

                items = RightClick(window, speaker);
                Assert.Equal("Inui", await Choose(window, speaker, items, "Copiar tradução"));
                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task LineRow_sem_personagem_cadastrado_so_oferece_o_nome_original()
    {
        await fixture.Session.Dispatch(
            async () =>
            {
                var window = Show(new LineRow { Number = 1, Speaker = Original, SpeakerOriginal = Original, Original = "原文" });
                var speaker = SpeakerOf(window);

                var items = RightClick(window, speaker);
                Assert.Equal(["Copiar nome original"], items.Select(i => i.Header));
                Assert.Equal(Original, await Choose(window, speaker, items, "Copiar nome original"));
                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task LineRow_narracao_nao_tem_menu_e_a_linha_que_vira_narracao_perde_o_menu()
    {
        await fixture.Session.Dispatch(
            () =>
            {
                var row = new LineRow { Number = 1, Speaker = "Inui", SpeakerOriginal = Original, Original = "原文" };
                var window = Show(row);
                var speaker = SpeakerOf(window);
                Assert.NotNull(speaker.ContextFlyout);

                row.Speaker = null;
                row.SpeakerOriginal = null;
                ScreenRenderer.Settle(window);
                Assert.Equal(LineRow.NarrationLabel, speaker.Text);
                Assert.Null(speaker.ContextFlyout);
                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Detalhe_da_linha_com_personagem_cadastrado_copia_o_original_e_a_traducao()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [new TextUnit("yst00001:000000", TextUnitKind.Dialogue, Original, "原文", "Bom dia.", TranslationStatus.Approved)]);
        folder.SaveCharacters([new CharacterEntry(Original, "Inui")]);
        var vm = new EditorViewModel(postToUi: action => action());
        vm.Project = EditorFixtures.Summary("Minha VN", root);
        await vm.Loading;
        vm.SelectedLine = vm.Lines[0];

        await fixture.Session.Dispatch(
            async () =>
            {
                var window = Show(new LineDetailView { DataContext = vm.Detail }, 420, 1000);
                var blocks = window.GetVisualDescendants().OfType<SelectableTextBlock>().Where(b => b.IsEffectivelyVisible).ToList();

                foreach (var name in new[] { "Falante", "Quem fala no contexto" })
                {
                    var block = Assert.Single(blocks, b => AutomationProperties.GetName(b) == name);
                    var items = RightClick(window, block);
                    Assert.Equal(["Copiar nome original", "Copiar tradução"], items.Select(i => i.Header));
                    Assert.Equal(Original, await Choose(window, block, items, "Copiar nome original"));
                    items = RightClick(window, block);
                    Assert.Equal("Inui", await Choose(window, block, items, "Copiar tradução"));
                }

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Conferencia_clique_direito_sem_selecao_copia_o_nome_exato_do_texto_extraido()
    {
        using var temp = new TempFolder();
        var folder = CreateCp1252Project(temp, new TextUnit("yst00001:000000", TextUnitKind.Dialogue, Wide, "原文", "Bom dia!", TranslationStatus.Translated));
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(folder.Root) };
        await vm.RefreshAsync();

        await fixture.Session.Dispatch(
            async () =>
            {
                var window = Show(new ConferenceView { DataContext = vm });
                var speaker = Assert.Single(window.GetVisualDescendants().OfType<SelectableTextBlock>(), b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Quem fala");

                var items = RightClick(window, speaker);
                Assert.Equal(Wide, await Choose(window, speaker, items, "Copiar nome original"));
                window.Close();
            },
            CancellationToken.None);
    }
}
