using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Engines.YuRis;

namespace VNForge.App.Tests;

/// <summary>O cartão do patch da fonte na tela Build de verdade: aparece só com cp1252, e a confirmação lista o executável.</summary>
public class BuildPatchHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    /// <summary>Abre a tela, roda as conferências e fecha a janela: uma janela esquecida no Headless estraga os testes de render seguintes.</summary>
    private static void Abrir(BuildViewModel vm, Action<Window> check)
    {
        var window = new Window { Width = 1200, Height = 900, Content = new BuildView { DataContext = vm, TechnicalExpanded = true } };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            check(window);
        }
        finally
        {
            window.Close();
        }
    }

    private static Button Botao(Window window, string name) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty) == name);

    private static async Task<BuildViewModel> OpenAsync(TempFolder temp, string strategy)
    {
        var game = temp.SubFolder("jogo");
        File.WriteAllBytes(Path.Combine(game, "jogo.exe"), BuildPatchViewModelTests.SyntheticExe());
        var folder = ProjectFolder.Create(temp.SubFolder("projeto"), new ProjectConfig(
            "Teste",
            new GameConfig(game, "jogo.exe"),
            new EngineConfig("yuris"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig(strategy),
            AiConfig.Default));
        var vm = new BuildViewModel(new BackgroundTaskService(), [new YuRisEngine()], new NoopStarter());
        vm.Project = ProjectSummaryReader.Read(folder.Root);
        await vm.ReloadAsync();
        return vm;
    }

    [Fact]
    public async Task Nenhum_botao_dos_detalhes_tecnicos_tem_destaque_e_o_unico_da_pagina_e_o_Testar_no_jogo()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp, "cp1252");

        await fixture.Session.Dispatch(
            () => Abrir(vm, window =>
            {
                var detalhes = window.GetVisualDescendants().OfType<VNForge.App.Controls.TechnicalDetails>().Single();
                var botoes = detalhes.GetVisualDescendants().OfType<Button>().ToList();
                Assert.Contains(botoes, b => b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty) == "Aplicar patch da fonte");
                Assert.Contains(botoes, b => b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty) == "Injetar no jogo");
                Assert.Contains(botoes, b => b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty) == "Ver erros");
                Assert.DoesNotContain(botoes, b => b.Classes.Contains("accent"));

                var destaques = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Classes.Contains("accent")).ToList();
                Assert.Equal([Botao(window, "Testar no jogo")], destaques);
            }),
            CancellationToken.None);
    }

    [Fact]
    public async Task Com_cp1252_o_cartao_mostra_o_botao_e_a_confirmacao_lista_o_executavel()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp, "cp1252");
        await vm.PatchCommand.ExecuteAsync(null);

        await fixture.Session.Dispatch(
            () => Abrir(vm, window =>
            {
                Assert.True(Botao(window, "Aplicar patch da fonte").IsEffectivelyVisible);
                Assert.True(Botao(window, "Confirmar").IsEffectivelyVisible);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "jogo.exe");
            }),
            CancellationToken.None);

        vm.CancelConfirmationCommand.Execute(null);
    }

    [Fact]
    public async Task Com_outra_acentuacao_o_cartao_nao_aparece()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp, "fold");

        await fixture.Session.Dispatch(
            () => Abrir(vm, window => Assert.False(Botao(window, "Aplicar patch da fonte").IsEffectivelyVisible)),
            CancellationToken.None);
    }

    private sealed class NoopStarter : IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory)
        {
        }
    }
}
