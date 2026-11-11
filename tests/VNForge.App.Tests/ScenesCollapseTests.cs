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
        // A lista aberta tem no máximo 220 px (quadro); recolhida vira uma faixa de 44 e a grade leva a diferença.
        Assert.True(collapsedLines > openLines + 75, $"as linhas ganham o espaço da lista ({openLines:0} para {collapsedLines:0})");
        Assert.True(restoredPanel);
        Assert.False(vm.IsScenesCollapsed);
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
