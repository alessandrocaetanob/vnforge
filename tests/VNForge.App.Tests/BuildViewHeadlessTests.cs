using Avalonia.Controls;
using VNForge.App.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Time.Testing;
using Avalonia.VisualTree;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;

namespace VNForge.App.Tests;

/// <summary>A tela Build e injeção de verdade (tema, estilos, recursos): abre, mostra o que muda no jogo e habilita só os botões que valem.</summary>
public class BuildViewHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static Window Abrir(BuildViewModel vm, double width = 1200)
    {
        var window = new Window { Width = width, Height = 900, Content = new BuildView { DataContext = vm, TechnicalExpanded = true } };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    private static Button Botao(Window window, string name) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty) == name);

    /// <summary>Um build pronto e um jogo que mudou depois do plano: a injeção falha e a tela ganha banner de erro e linhas de log.</summary>
    private static async Task<BuildViewModel> ComErroEmLog(TempFolder temp)
    {
        var game = temp.SubFolder("jogo");
        File.WriteAllText(Path.Combine(game, "ysbin.ypf"), "original");
        File.WriteAllText(Path.Combine(game, "jogo.exe"), "MZ");
        var folder = ProjectFolder.Create(temp.SubFolder("projeto"), new ProjectConfig(
            "Teste",
            new GameConfig(game, "jogo.exe"),
            new EngineConfig("yuris"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        File.WriteAllText(Path.Combine(folder.BuildDirectory, "ysbin.ypf"), "traduzido");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 20, 19, 8, TimeSpan.Zero));
        var vm = new BuildViewModel(new BackgroundTaskService(), [new FakeEngine()], new NoopStarter(), clock: clock);
        vm.Project = ProjectSummaryReader.Read(folder.Root);
        await vm.ReloadAsync();
        vm.InjectCommand.Execute(null);
        File.WriteAllText(Path.Combine(game, "ysbin.ypf"), "mudou depois do plano");
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ErrorMessage);
        Assert.NotEmpty(vm.Log);
        return vm;
    }

    [Fact]
    public async Task Copiar_log_poe_o_log_inteiro_com_data_e_hora_na_area_de_transferencia()
    {
        using var temp = new TempFolder();
        var vm = await ComErroEmLog(temp);

        await fixture.Session.Dispatch(
            async () =>
            {
                var window = Abrir(vm);
                var botao = Botao(window, "Copiar log");
                Assert.True(botao.IsEffectivelyEnabled);

                botao.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                var copiado = await window.Clipboard!.TryGetTextAsync();
                Assert.Equal(vm.LogText, copiado);
                Assert.StartsWith("2026-10-26 20:19:08", copiado, StringComparison.Ordinal);
                foreach (var linha in vm.Log)
                {
                    Assert.Contains(linha.Message, copiado, StringComparison.Ordinal);
                }
            },
            CancellationToken.None);
    }

    private static Border Cartao(Window window, string name) =>
        window.GetVisualDescendants().OfType<Border>().Single(b => b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty) == name);

    /// <summary>Um build que troca dezenas de scripts e uma pasta de backup cheia: as duas listas passam muito da altura do cartão.</summary>
    private static BuildViewModel ComLogEBackupsLongos()
    {
        var vm = new BuildViewModel(new BackgroundTaskService(), [new FakeEngine()], new NoopStarter());
        for (var i = 0; i < 80; i++)
        {
            vm.Log.Add(new BuildLogLine("11:16:09", $"AVISO yst{i:00000}:{i * 1000} [caixa-acima-do-limite] caixa 1 com 4 linhas (máximo 3).", "2026-10-28"));
        }

        for (var i = 0; i < 40; i++)
        {
            vm.Backups.Add(new BackupRow($"backup{Path.DirectorySeparatorChar}2026102811{i:00}00", "12,4 MB", "ysbin.ypf, yst_cap1.ybn e outros originais · injeção de 28/10"));
        }

        return vm;
    }

    private sealed class NoopStarter : IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory)
        {
        }
    }
}
