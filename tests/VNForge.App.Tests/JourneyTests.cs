using VNForge.AI.Batches;
using VNForge.AI.Cost;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>As duas jornadas que faltavam no App: criar um projeto a partir de um jogo da Biblioteca e a tradução noturna de ponta a ponta. Pelo ViewModel, sem janela; o que se confere é o arquivo gravado e o projeto aberto.</summary>
[Trait("Risco", "R6")]
public sealed class JourneyTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    [Fact]
    public async Task Criar_projeto_a_partir_de_um_jogo_da_Biblioteca_grava_o_vnforge_json_liga_o_jogo_e_abre_o_projeto()
    {
        var gameFolder = temp.SubFolder("jogo");
        File.WriteAllText(Path.Combine(gameFolder, "Jogo.exe"), "MZ");
        var game = new LibraryGame(GameLibrary.IdFor(gameFolder), GameLibrary.NormalizeFolder(gameFolder), "Jogo.exe", new LibraryEngine("yuris", "Yu-RIS", LibraryEngineSource.Engine), "Nanairo Ressha", "七色列車", Now);
        var file = LibrarySamples.Save(temp.SubFolder("biblioteca"), LibrarySamples.Library([game]));
        var library = new LibraryViewModel(file, new FakeFolderPicker(null), action => action());
        var wizard = new NewProjectWizardViewModel(new FakeFolderPicker(null), [new FakeEngine()]);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now)), library: library)
        {
            NewProjectWizard = () => wizard,
        };
        await library.LoadAsync();
        library.OpenGameCommand.Execute(library.Cards.Single());
        Assert.True(vm.InProjectlessGame);

        vm.TranslateProjectlessGameCommand.Execute(null);
        await vm.WizardPreparing;
        Assert.Same(wizard, vm.CurrentPage);
        var root = Path.Combine(temp.Path, "meu-projeto");
        wizard.SetProjectDirectory(root);
        for (var advances = 0; !wizard.IsDoneStep && wizard.NextCommand.CanExecute(null) && advances < 10; advances++)
        {
            await wizard.NextCommand.ExecuteAsync(null);
        }

        Assert.True(wizard.IsDoneStep, $"O assistente parou no passo {wizard.Step} ({wizard.StepTitle}).");

        wizard.FinishCommand.Execute(null);
        await vm.ProjectOpening;
        await library.LoadAsync();

        var config = ProjectFolder.Open(root).LoadConfig();
        Assert.Equal(game.Folder, GameLibrary.NormalizeFolder(Path.GetFullPath(config.Game.Path, root)));
        Assert.Equal(GameLibrary.NormalizeFolder(root), file.Load().Find(game.Id).ProjectPath);
        Assert.Equal("meu-projeto", vm.ProjectTitle);
        Assert.False(vm.InProjectlessGame);
    }

    [Fact]
    public async Task Traducao_noturna_enviada_sobrevive_ao_fechar_o_app_e_grava_as_linhas_e_o_gasto_ao_reabrir()
    {
        var client = new FakeMessageBatchClient();
        var root = StartFixtures.Create(
            temp.SubFolder("vn"),
            "Minha VN",
            lines: [("yst00001", TranslationStatus.Untranslated), ("yst00001", TranslationStatus.Untranslated), ("yst00001", TranslationStatus.Approved), ("yst00002", TranslationStatus.Untranslated)]);

        var first = await OpenAsync(root, client);
        first.Editor.TranslatePendingCommand.Execute(null);
        await first.TranslateOpening;
        first.Translate.SelectedWhen = first.Translate.Whens.Single(w => w.Overnight);
        first.Translate.StartCommand.Execute(null);
        await first.TranslationRun;

        Assert.Equal(1, client.Creates);
        var sent = Assert.Single(new NightBatchStore(ProjectFolder.Open(root)).Open());
        Assert.Equal(TranslationStatus.Untranslated, ProjectFolder.Open(root).LoadText("yst00001")[0].Status);

        // Fechar o app com o lote na API é a pausa: o estado do lote vive na pasta do projeto.
        var second = await OpenAsync(root, client);
        var task = Assert.Single(second.TaskService.Tasks);
        Assert.True(task.IsRunning);

        client.Finish(sent.Id);
        task.CheckNowCommand!.Execute(null);
        await second.NightBatchWatching;
        await task.Completion;

        var project = ProjectFolder.Open(root);
        Assert.Equal(TranslationStatus.Machine, project.LoadText("yst00001")[0].Status);
        Assert.Equal(TranslationStatus.Approved, project.LoadText("yst00001")[2].Status);
        Assert.Empty(new NightBatchStore(project).Open());
        Assert.True(AiUsageLog.Load(project).Dollars > 0m);
        Assert.Equal(1, client.Creates);
    }

    private async Task<MainWindowViewModel> OpenAsync(string root, FakeMessageBatchClient client)
    {
        var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now));
        var settings = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("ajustes")), _ => null);
        var vm = new MainWindowViewModel(start, new ResolvedApiKey("chave", ApiKeySource.Vault), null, settings)
        {
            MessageBatchClientFactory = () => client,
            NightBatchPollInterval = TimeSpan.FromHours(1),
        };
        await start.OpenAsync(root);
        start.OpenSelectedInEditorCommand.Execute(null);
        await vm.Editor.Loading;
        vm.Editor.SelectedScript = vm.Editor.Scripts.Single(s => s.Key == "yst00001");
        return vm;
    }
}
