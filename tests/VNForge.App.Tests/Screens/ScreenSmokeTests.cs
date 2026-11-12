using Avalonia.Styling;
using VNForge.App.Tests.Screens;

namespace VNForge.App.Tests;

/// <summary>
/// Smoke de cada tela do <see cref="ScreenCatalog"/>: abre a tela com o projeto de demonstração, no tema escuro, e confere que nenhuma exceção sobe e
/// que nenhum texto fica cortado. Sem comparação de pixel e sem medida: a lógica da tela é dos testes de ViewModel, e os PNG para o designer saem da
/// ferramenta <c>tools/VNForge.Capturas</c>.
/// </summary>
[Trait("Tipo", "Tela")]
public sealed class ScreenSmokeTests(HeadlessFixture fixture, ScreenSmokeTests.DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<ScreenSmokeTests.DemoFolderFixture>
{
    public static TheoryData<string> StandaloneNames => [.. ScreenCatalog.Standalone.Select(s => s.Name)];

    [Fact]
    public async Task As_paginas_do_projeto_abrem_sem_excecao_e_sem_texto_cortado()
    {
        var problems = await fixture.Session.Dispatch<List<string>>(
            async () =>
            {
                using var host = await ScreenHost.OpenProjectAsync(demoFolder.Root, ThemeVariant.Dark);
                var found = new List<string>();
                foreach (var (name, key) in ScreenCatalog.Pages)
                {
                    await host.GoToAsync(key);
                    found.AddRange(ClippedTextHeadlessTests.FindClipped(host.Window).Select(p => $"{name}: {p}"));
                }

                return found;
            },
            TestContext.Current.CancellationToken);

        ReleaseNativeMemory();
        Assert.True(problems.Count == 0, $"texto cortado: {string.Join("; ", problems)}");
    }

    [Theory]
    [MemberData(nameof(StandaloneNames))]
    public async Task A_tela_abre_sem_excecao_e_sem_texto_cortado(string name)
    {
        var screen = ScreenCatalog.Standalone.Single(s => s.Name == name);

        var clipped = await fixture.Session.Dispatch<List<string>>(
            async () =>
            {
                using var host = await screen.Open(demoFolder.Root, ThemeVariant.Dark);
                host.Settle();
                return ClippedTextHeadlessTests.FindClipped(host.Window);
            },
            TestContext.Current.CancellationToken);

        ReleaseNativeMemory();
        Assert.True(clipped.Count == 0, $"{name}: texto cortado: {string.Join("; ", clipped)}");
    }

    /// <summary>
    /// A janela fechada ainda segura superfícies do Skia até o finalizador rodar. Sem coletar aqui o processo do App passa de 900 MB só com o smoke,
    /// porque o coletor não tem pressa em um computador com memória sobrando.
    /// </summary>
    private static void ReleaseNativeMemory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>A pasta do projeto de demonstração, gravada uma vez por classe de teste e apagada no fim.</summary>
    public sealed class DemoFolderFixture : IDisposable
    {
        private readonly TempFolder temp = new();

        public DemoFolderFixture() => Root = ScreenCatalog.CreateDemoFolder(temp, trimmed: true);

        public string Root { get; }

        public void Dispose() => temp.Dispose();
    }
}
