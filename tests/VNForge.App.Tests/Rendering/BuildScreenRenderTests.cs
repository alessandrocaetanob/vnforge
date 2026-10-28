using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Qa;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// Renderiza a página Build e injeção com o estado do artboard 6 (build de 22/10 válido para injetar, 2 erros novos de QA, dois backups) em
/// <c>build-pagina-escuro.png</c> e <c>build-pagina-claro.png</c>. O build é de verdade, com uma engine falsa sobre o projeto de demonstração; a
/// moldura da janela (barra de título, trilho, barra de status) é da #241 e fica de fora.
/// </summary>
public sealed class BuildScreenRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    public static TheoryData<string> Themes => ["Dark", "Light"];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_a_pagina_build_com_o_estado_do_artboard(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = DemoBuild.Create(temp.SubFolder("demo"));
        var qa = new DemoQaRule();
        var vm = new BuildViewModel(
            new BackgroundTaskService(),
            [new DemoBuildEngine()],
            new NoLaunchStarter(),
            new QaEngine([qa]),
            new BuildClock(new DateTimeOffset(2026, 10, 22, 23, 14, 2, TimeSpan.Zero), TimeSpan.FromMilliseconds(6800)))
        {
            Project = DemoProject.Summary(root),
        };

        await fixture.Session.Dispatch(
            async () =>
            {
                await vm.ReloadAsync();
                await vm.BuildCommand.ExecuteAsync(null);
                Assert.Null(vm.ErrorMessage);

                // O build é de 22/10 23:14; depois dele surgiram os erros de QA do artboard.
                foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "build"), "*", SearchOption.AllDirectories))
                {
                    File.SetLastWriteTimeUtc(file, new DateTime(2026, 10, 22, 23, 14, 8, DateTimeKind.Utc));
                }

                qa.Enabled = true;
                await vm.ReloadAsync();
                Assert.True(vm.HasQaErrors);

                var path = RenderOutput.PathFor(ScreenRenderer.FileName("testar-no-jogo-detalhes", theme));
                ScreenRenderer.Render(new BuildView { DataContext = vm, TechnicalExpanded = true }, theme, path, inspect: window => Inspect(window, vm));
                Assert.True(new FileInfo(path).Length > 10_000);
                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static void Inspect(Window window, BuildViewModel vm)
    {
        Assert.Equal("Build pronto, ainda não injetado.", vm.ResultTitle);
        Assert.Equal(2, vm.Backups.Count);
        Assert.Equal(2, vm.QaErrorCount);
        Assert.Equal(37, vm.QaWarningCount);
        Assert.Equal("22/10/2026 23:14 · 6,8 s", vm.LogSummary);
        Assert.Equal("14 reconstruídos de 312", vm.Facts[0].Value);

        var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty) is "Injetar no jogo" or "Abrir jogo").ToList();
        Assert.Equal(2, buttons.Count);
        Assert.Equal(buttons[0].Bounds.Width, buttons[1].Bounds.Width);

        // Uma lista de opções só, no corpo; a faixa dos detalhes explica o botão do topo e não tem botão próprio.
        var names = window.GetVisualDescendants().OfType<Control>().Select(c => c.GetValue(Avalonia.Automation.AutomationProperties.NameProperty)).ToList();
        Assert.Single(names, n => n is not null && n.StartsWith("Opções", StringComparison.Ordinal));
        Assert.DoesNotContain("Ir para Testar no jogo", names);
        Assert.Single(window.GetVisualDescendants().OfType<ToggleRule>(), r => r.Title == "Reconstruir só scripts alterados");
    }

    private sealed class NoLaunchStarter : IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory)
        {
        }
    }

    /// <summary>O relógio do build: a hora anda meio segundo a cada leitura e o cronômetro dá a duração fixa de uma operação.</summary>
    private sealed class BuildClock(DateTimeOffset start, TimeSpan duration) : TimeProvider
    {
        private DateTimeOffset now = start;
        private long ticks;

        public override DateTimeOffset GetUtcNow()
        {
            var current = now;
            now += TimeSpan.FromMilliseconds(500);
            return current;
        }

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp()
        {
            var current = ticks;
            ticks += (long)duration.TotalMilliseconds;
            return current;
        }
    }
}
