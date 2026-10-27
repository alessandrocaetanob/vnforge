using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>"Detalhes técnicos" abertos continuam abertos quando a pessoa sai da página e volta: a view é recriada a cada visita e o estado não pode morrer com ela.</summary>
public class TechnicalDetailsMemoryHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static TechnicalDetails Details<TView>(Window window)
        where TView : Control =>
        Assert.Single(window.GetVisualDescendants().OfType<TView>(), v => v.IsEffectivelyVisible)
            .GetVisualDescendants().OfType<TechnicalDetails>().First();

    [Fact]
    public async Task Detalhes_tecnicos_do_Build_continuam_abertos_ao_trocar_de_pagina_e_voltar()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)));
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;

        await fixture.Session.Dispatch(
            async () =>
            {
                var window = new MainWindow { Width = 1440, Height = 900, DataContext = vm };
                TechnicalDetails.SetMemory(window, new TechnicalDetailsMemory());
                try
                {
                    window.Show();
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Build);
                    await vm.Build.Loading;
                    Settle(window);
                    var first = Details<BuildView>(window);
                    Assert.False(first.IsExpanded);

                    first.IsExpanded = true;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
                    Settle(window);
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Build);
                    Settle(window);

                    var again = Details<BuildView>(window);
                    Assert.NotSame(first, again);
                    Assert.True(again.IsExpanded);

                    again.IsExpanded = false;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
                    Settle(window);
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Build);
                    Settle(window);
                    Assert.False(Details<BuildView>(window).IsExpanded);
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
    public async Task Bloco_lembra_pela_chave_e_nao_lembra_sem_chave_nem_sem_memoria()
    {
        var memory = new TechnicalDetailsMemory();
        memory.Set("a", true);

        await fixture.Session.Dispatch(
            () =>
            {
                TechnicalDetails Show(string? key, TechnicalDetailsMemory? with)
                {
                    var details = new TechnicalDetails { Key = key, Content = new TextBlock { Text = "motor" } };
                    var window = new Window { Content = details };
                    TechnicalDetails.SetMemory(window, with);
                    window.Show();
                    Settle(window);
                    return details;
                }

                Assert.True(Show("a", memory).IsExpanded);
                Assert.False(Show("b", memory).IsExpanded);
                Assert.False(Show(null, memory).IsExpanded);
                Assert.False(Show("a", null).IsExpanded);

                var toggled = Show("b", memory);
                toggled.IsExpanded = true;
                Assert.True(memory.Get("b"));
                Assert.True(Show("b", memory).IsExpanded);

                var loose = Show(null, memory);
                loose.IsExpanded = true;
                Assert.Null(memory.Get(""));
                return 0;
            },
            TestContext.Current.CancellationToken);
    }
}
