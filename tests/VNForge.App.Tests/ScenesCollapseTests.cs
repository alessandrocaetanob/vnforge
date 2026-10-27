using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A coluna das cenas recolhível do Editor (#393): onde a lista aparece, o estado lembrado entre sessões, o atalho e a faixa com o nome da cena.</summary>
public sealed class ScenesCollapseTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    [Theory]
    [InlineData(1280, false, ScenesPlacement.Flyout)]
    [InlineData(1399, true, ScenesPlacement.Flyout)]
    [InlineData(1400, false, ScenesPlacement.Column)]
    [InlineData(1440, true, ScenesPlacement.Strip)]
    [InlineData(1920, false, ScenesPlacement.Column)]
    [InlineData(1920, true, ScenesPlacement.Strip)]
    public void A_lista_de_cenas_vai_para_o_flyout_a_coluna_ou_a_faixa(double windowWidth, bool collapsed, ScenesPlacement expected) =>
        Assert.Equal(expected, EditorBreakpoints.PlaceScenes(windowWidth, collapsed));

    [Fact]
    public void Memoria_sem_pasta_comeca_aberta_e_lembra_so_ate_o_app_fechar()
    {
        var memory = new EditorLayoutMemory();

        Assert.False(memory.ScenesCollapsed);
        memory.ScenesCollapsed = true;
        Assert.True(memory.ScenesCollapsed);
    }

    [Fact]
    public void Memoria_com_pasta_grava_em_editor_layout_json_e_o_proximo_app_le_de_volta()
    {
        using var temp = new TempFolder();
        var config = temp.SubFolder("config");

        new EditorLayoutMemory(config).ScenesCollapsed = true;

        Assert.True(File.Exists(Path.Combine(config, "editor-layout.json")));
        Assert.True(new EditorLayoutMemory(config).ScenesCollapsed);
        Assert.False(File.Exists(Path.Combine(config, "technical-details.json")));
    }

    [Theory]
    [InlineData("{ não é json")]
    [InlineData("[true]")]
    [InlineData("{\"scenes-collapsed\": \"sim\"}")]
    public void Arquivo_ilegivel_vale_como_coluna_aberta(string content)
    {
        using var temp = new TempFolder();
        var config = temp.SubFolder("config");
        File.WriteAllText(Path.Combine(config, "editor-layout.json"), content);

        Assert.False(new EditorLayoutMemory(config).ScenesCollapsed);
    }

    [Fact]
    public void O_Editor_comeca_com_a_coluna_aberta_e_o_botao_alterna_e_grava()
    {
        using var temp = new TempFolder();
        var config = temp.SubFolder("config");
        var vm = new EditorViewModel(layoutMemory: new EditorLayoutMemory(config));
        var tips = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.ScenesToggleTip))
            {
                tips.Add(vm.ScenesToggleTip);
            }
        };

        Assert.False(vm.IsScenesCollapsed);
        Assert.Contains("Recolher", vm.ScenesToggleTip, StringComparison.Ordinal);

        vm.ToggleScenesCommand.Execute(null);

        Assert.True(vm.IsScenesCollapsed);
        Assert.True(new EditorLayoutMemory(config).ScenesCollapsed);
        Assert.Contains("Abrir", Assert.Single(tips), StringComparison.Ordinal);

        vm.ToggleScenesCommand.Execute(null);

        Assert.False(vm.IsScenesCollapsed);
        Assert.False(new EditorLayoutMemory(config).ScenesCollapsed);
    }

    [Fact]
    public void Um_Editor_novo_volta_recolhido_quando_a_sessao_anterior_terminou_assim()
    {
        using var temp = new TempFolder();
        var config = temp.SubFolder("config");
        new EditorViewModel(layoutMemory: new EditorLayoutMemory(config)).ToggleScenesCommand.Execute(null);

        var next = new EditorViewModel(layoutMemory: new EditorLayoutMemory(config));

        Assert.True(next.IsScenesCollapsed);
    }

    [Fact]
    public void O_mapa_de_atalhos_lista_o_Ctrl_B_do_Editor()
    {
        var editor = ShortcutMap.Groups.Single(g => g.Title == "Editor");

        Assert.Contains(editor.Entries, e => string.Join('+', e.Keys) == "Ctrl+B");
    }

    [Fact]
    public async Task A_1440_recolher_deixa_a_faixa_com_o_nome_da_cena_e_devolve_o_espaco_as_linhas()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Machine);
        var vm = new EditorViewModel(postToUi: acao => acao(), layoutMemory: new EditorLayoutMemory());
        vm.Project = EditorFixtures.Summary("Minha VN", root);
        await vm.Loading;
        vm.SelectedLine = vm.Lines[0];
        double openLines = 0, collapsedLines = 0, strip = 0;
        bool panelShownCollapsed = true, stripShownOpen = true, stripShownCollapsed = false, restoredPanel = false;
        string? stripText = null;

        await fixture.Session.Dispatch(
            () =>
            {
                var view = new EditorView { DataContext = vm };
                var window = new Window { Width = 1440, Height = 900, Content = view };
                window.Show();
                Settle(window);
                var columns = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "Columns");
                var panel = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ScenesBorder");
                var stripBorder = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ScenesStrip");
                var lines = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "LinesList");

                stripShownOpen = stripBorder.IsEffectivelyVisible;
                openLines = lines.Bounds.Width;
                Assert.True(lines.ContainerFromIndex(0)!.Focus());

                window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, null);
                window.KeyRelease(Key.B, RawInputModifiers.Control, PhysicalKey.B, null);
                Settle(window);
                panelShownCollapsed = panel.IsEffectivelyVisible;
                stripShownCollapsed = stripBorder.IsEffectivelyVisible;
                strip = stripBorder.Bounds.Width;
                collapsedLines = lines.Bounds.Width;
                stripText = stripBorder.GetVisualDescendants().OfType<TextBlock>().Single().Text;

                window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, null);
                window.KeyRelease(Key.B, RawInputModifiers.Control, PhysicalKey.B, null);
                Settle(window);
                restoredPanel = panel.IsEffectivelyVisible && !stripBorder.IsEffectivelyVisible && columns.ColumnDefinitions[0].Width.IsStar;
                window.Close();
            },
            CancellationToken.None);

        Assert.False(stripShownOpen);
        Assert.False(panelShownCollapsed);
        Assert.True(stripShownCollapsed);
        Assert.InRange(strip, EditorBreakpoints.ScenesStripWidth - 1, EditorBreakpoints.ScenesStripWidth + 1);
        Assert.Equal(vm.ScriptTitle, stripText);
        // A lista aberta tem no máximo 190 px (v4); recolhida vira uma faixa de 44 e a grade leva a diferença.
        Assert.True(collapsedLines > openLines + 75, $"as linhas ganham o espaço da lista ({openLines:0} para {collapsedLines:0})");
        Assert.True(restoredPanel);
        Assert.False(vm.IsScenesCollapsed);
    }

    [Fact]
    public async Task A_1366_o_estado_recolhido_nao_muda_o_flyout_e_o_Ctrl_B_abre_e_fecha_a_lista()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Machine);
        var vm = new EditorViewModel(postToUi: acao => acao(), layoutMemory: new EditorLayoutMemory());
        vm.Project = EditorFixtures.Summary("Minha VN", root);
        await vm.Loading;
        vm.ToggleScenesCommand.Execute(null);
        bool stripShown = true, triggerShown = false, flyoutOpened = false, flyoutClosed = false;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = 1366, Height = 768, Content = new EditorView { DataContext = vm } };
                window.Show();
                Settle(window);
                stripShown = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ScenesStrip").IsEffectivelyVisible;
                var trigger = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("scenes-trigger"));
                triggerShown = trigger.IsEffectivelyVisible;
                Assert.True(window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "LinesList").ContainerFromIndex(0)!.Focus());

                window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, null);
                window.KeyRelease(Key.B, RawInputModifiers.Control, PhysicalKey.B, null);
                Settle(window);
                flyoutOpened = ((Flyout)trigger.Flyout!).IsOpen;

                window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, null);
                window.KeyRelease(Key.B, RawInputModifiers.Control, PhysicalKey.B, null);
                Settle(window);
                flyoutClosed = !((Flyout)trigger.Flyout!).IsOpen;
                window.Close();
            },
            CancellationToken.None);

        Assert.False(stripShown);
        Assert.True(triggerShown);
        Assert.True(flyoutOpened);
        Assert.True(flyoutClosed);
        Assert.True(vm.IsScenesCollapsed);
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
