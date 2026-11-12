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
    // Defeito achado pelo smoke (#671) e ainda sem quadro aprovado para corrigir: no mapa de atalhos a descrição do Ctrl + passa 20 px da coluna e sobe sobre as teclas.
    // Mudar o texto ou a quebra muda o que a pessoa vê, então fica anotado aqui até a tela ter o visual aprovado. Quando o defeito sumir o smoke falha
    // pedindo para tirar a entrada, para a lista ficar limpa.
    private static readonly (string Screen, string Text)[] KnownClipped = [("atalhos", "Aumentar o texto do Editor (grade, inspetor e Modo revisão)")];

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
                    found.AddRange(WithoutKnown(name, ClippedTextHeadlessTests.FindClipped(host.Window)).Select(p => $"{name}: {p}"));
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
        var unexpected = WithoutKnown(name, clipped);
        Assert.True(unexpected.Count == 0, $"{name}: texto cortado: {string.Join("; ", unexpected)}");
    }

    /// <summary>Tira do achado o defeito conhecido da tela, comparando o texto inteiro; se a tela tem uma entrada conhecida e ela não veio, é um problema novo.</summary>
    private static List<string> WithoutKnown(string screen, List<string> clipped)
    {
        var result = new List<string>(clipped);
        foreach (var known in KnownClipped.Where(k => k.Screen == screen))
        {
            var prefix = $"\"{known.Text}\" (";
            if (result.RemoveAll(c => c.StartsWith(prefix, StringComparison.Ordinal)) == 0)
            {
                result.Add($"o defeito conhecido \"{known.Text}\" não aparece mais: tire a entrada de KnownClipped (#671)");
            }
        }

        return result;
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
