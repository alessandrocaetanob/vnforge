using Avalonia.Controls;
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

    [Fact]
    public async Task Tela_mostra_os_botoes_habilitados_conforme_o_estado_e_a_lista_do_que_muda()
    {
        using var temp = new TempFolder();
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
        var vm = new BuildViewModel(new BackgroundTaskService(), [new FakeEngine()], new NoopStarter());
        vm.Project = ProjectSummaryReader.Read(folder.Root);
        await vm.ReloadAsync();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);

                Assert.True(Botao(window, "Injetar no jogo").IsEffectivelyEnabled);
                Assert.True(Botao(window, "Abrir jogo").IsEffectivelyEnabled);
                Assert.False(Botao(window, "Restaurar originais").IsEffectivelyEnabled);
                Assert.True(Botao(window, "Novo build").IsEffectivelyEnabled);

                vm.InjectCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.True(Botao(window, "Confirmar").IsEffectivelyVisible);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "ysbin.ypf");
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Detalhes_tecnicos_abrem_com_a_faixa_que_aponta_o_Testar_no_jogo_e_o_link_volta_ao_botao()
    {
        using var temp = new TempFolder();
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
        var vm = new BuildViewModel(new BackgroundTaskService(), [new FakeEngine()], new NoopStarter());
        vm.Project = ProjectSummaryReader.Read(folder.Root);
        await vm.ReloadAsync();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                try
                {
                    var faixa = window.GetVisualDescendants().OfType<Border>()
                        .Single(b => b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty) == "Testar no jogo faz tudo");
                    Assert.True(faixa.IsEffectivelyVisible);
                    var texto = string.Concat(faixa.GetVisualDescendants().OfType<TextBlock>().SelectMany(t => t.Inlines!.OfType<Run>()).Select(r => r.Text));
                    Assert.Contains("Testar no jogo", texto, StringComparison.Ordinal);
                    Assert.Contains("Não precisa restaurar os originais entre um teste e outro", texto, StringComparison.Ordinal);

                    var testar = Botao(window, "Testar no jogo");
                    testar.Focusable = true;
                    Assert.False(testar.IsFocused);

                    var link = Botao(window, "Ir para Testar no jogo");
                    Assert.DoesNotContain("accent", link.Classes);
                    link.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(testar.IsFocused);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

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
    public async Task Banner_de_erro_e_linhas_do_log_deixam_selecionar_o_texto()
    {
        using var temp = new TempFolder();
        var vm = await ComErroEmLog(temp);

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                var selecionaveis = window.GetVisualDescendants().OfType<SelectableTextBlock>().ToList();

                Assert.Contains(selecionaveis, t => t.Text == vm.ErrorMessage);
                Assert.All(
                    selecionaveis,
                    t => Assert.Contains(((MenuFlyout)t.ContextFlyout!).Items.OfType<MenuItem>(), i => i.Header as string == "Copiar"));
                foreach (var linha in vm.Log)
                {
                    Assert.Contains(selecionaveis, t => t.Text?.Contains(linha.Message, StringComparison.Ordinal) == true);
                }
            },
            CancellationToken.None);
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

    [Fact]
    public async Task Copiar_log_fica_desabilitado_enquanto_nao_ha_log()
    {
        using var temp = new TempFolder();
        var vm = new BuildViewModel(new BackgroundTaskService(), [new FakeEngine()], new NoopStarter());

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);

                Assert.False(Botao(window, "Copiar log").IsEffectivelyEnabled);
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

    [Theory]
    [InlineData(1200)]
    [InlineData(900)]
    public async Task Log_ocupa_o_cartao_inteiro_e_os_backups_rolam_por_dentro_sem_esticar_a_linha(double width)
    {
        var vm = ComLogEBackupsLongos();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm, width);
                try
                {
                    var log = Cartao(window, "Log do build");
                    var backups = Cartao(window, "Backups");
                    var logScroll = log.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "LogScroll");
                    var backupScroll = backups.GetVisualDescendants().OfType<ScrollViewer>().Single();

                    // Sem vão: a área de rolagem do log vai do cabeçalho do cartão (48) até o pé, descontada a borda de 1 px.
                    Assert.True(logScroll.Bounds.Height >= log.Bounds.Height - 48 - 2, $"log {log.Bounds.Height}, rolagem {logScroll.Bounds.Height}");
                    Assert.True(logScroll.Extent.Height > logScroll.Viewport.Height);

                    // Backups rolam dentro do cartão e a linha não cresce com a lista.
                    Assert.True(backupScroll.Extent.Height > backupScroll.Viewport.Height);
                    Assert.True(backups.Bounds.Height < 600, $"backups {backups.Bounds.Height}");
                    Assert.Equal(log.Bounds.Height, backups.Bounds.Height);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Log_acompanha_o_fim_e_usa_a_altura_do_cartao()
    {
        var vm = new BuildViewModel(new BackgroundTaskService(), [new FakeEngine()], new NoopStarter());

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                try
                {
                    for (var i = 0; i < 80; i++)
                    {
                        vm.Log.Add(new BuildLogLine("11:16:09", $"AVISO yst{i:00000}:{i * 1000} [caixa-acima-do-limite] caixa 1 com 4 linhas (máximo 3).", "2026-10-28"));
                    }

                    var logScroll = Cartao(window, "Log do build").GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "LogScroll");
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();

                    Assert.Equal(logScroll.Extent.Height - logScroll.Viewport.Height, logScroll.Offset.Y, 1);
                    Assert.True(logScroll.Viewport.Height >= 250, $"viewport {logScroll.Viewport.Height}");
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    private sealed class NoopStarter : IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory)
        {
        }
    }
}
