using Microsoft.Extensions.Time.Testing;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.Core.Injection;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Qa;

namespace VNForge.App.Tests;

/// <summary>"Original ou traduzido com um clique" (#552) na Biblioteca, sobre a pasta de jogo sintética e os backups do projeto de demonstração: a troca passa pelo backup e pela injeção do Core.</summary>
public sealed class LibraryVersionTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly string _config;
    private readonly string _project;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    public LibraryVersionTests()
    {
        _project = DemoBuild.Create(_temp.SubFolder("demo"));
        _config = _temp.SubFolder("config");
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string GameFolder => Path.GetFullPath(Path.Combine(_project, "..", "jogo"));

    private string Archive => Path.Combine(GameFolder, DemoBuild.Archive);

    private LibraryGame Game(string? projectPath) =>
        new(GameLibrary.IdFor(GameFolder), GameLibrary.NormalizeFolder(GameFolder), "MinhaVN.exe", new LibraryEngine("yuris", "Yu-RIS", LibraryEngineSource.Engine), "Hoshikuzu Diary", null, LibrarySamples.Now)
        {
            ProjectPath = projectPath,
        };

    private async Task<LibraryViewModel> LoadedAsync(LibraryGame game)
    {
        LibrarySamples.Save(_config, new GameLibrary([game]));
        var switcher = new LibraryTranslationSwitcher(new GameTester([new DemoBuildEngine()], new NoStarter(), new QaEngine([]), _clock));
        var vm = new LibraryViewModel(new LibraryFile(_config), new FakeFolderPicker(null), action => action(), new LibraryServices { ReadTranslation = _ => null, Switcher = switcher, Engines = [new DemoBuildEngine()] });
        await vm.LoadAsync();
        vm.Select(vm.Cards.Single());
        return vm;
    }

    private static async Task SwitchToAsync(LibraryViewModel vm, bool translated)
    {
        vm.Drawer!.SelectedVersion = vm.Drawer.Versions.Single(v => v.Translated == translated);
        await vm.Switching;
    }

    [Fact]
    public async Task Troca_para_o_original_e_de_volta_pelo_backup_e_pela_injecao()
    {
        var vm = await LoadedAsync(Game(_project));
        Assert.Equal("Patch no jogo", vm.Cards.Single().VersionTag);
        Assert.Equal(DemoBuild.Payload(4_350_000, 3), await File.ReadAllBytesAsync(Archive, Ct));

        await SwitchToAsync(vm, translated: false);

        Assert.Null(vm.SwitchProblem);
        Assert.Equal(DemoBuild.Payload(DemoBuild.OriginalBytes, 1), await File.ReadAllBytesAsync(Archive, Ct));
        Assert.Equal("Original", vm.Cards.Single().VersionTag);
        Assert.Equal("pend", vm.Cards.Single().VersionTone);
        Assert.Equal("Original", vm.Drawer!.SelectedVersion!.Label);
        Assert.False(vm.Drawer.HasLastSwitch);

        await SwitchToAsync(vm, translated: true);

        Assert.Null(vm.SwitchProblem);
        Assert.Equal(DemoBuild.Payload(DemoBuild.RebuiltBytes, 4), await File.ReadAllBytesAsync(Archive, Ct));
        Assert.Equal("PT-BR no jogo", vm.Cards.Single().VersionTag);
        Assert.Equal("ok", vm.Drawer!.VersionTone);
        Assert.Equal("Patch do VNForge no jogo", vm.Drawer.VersionPill);
        Assert.Equal("Traduzido", vm.Drawer.SelectedVersion!.Label);
        Assert.True(vm.Drawer.HasLastSwitch);
        Assert.Equal("1, todos com cópia", vm.Drawer.FilesText);
        Assert.Same(vm.Selected, vm.Drawer.Card);
        Assert.Same(vm.Cards.Single(), vm.Selected);
        Assert.False(vm.IsSwitching);
    }

    [Fact]
    public async Task Jogo_que_mudou_depois_da_injecao_recusa_a_volta_e_o_dialogo_explica()
    {
        var changed = DemoBuild.Payload(1000, 9);
        await File.WriteAllBytesAsync(Archive, changed, Ct);
        var vm = await LoadedAsync(Game(_project));
        var drawer = vm.Drawer!;
        var renotified = false;
        drawer.PropertyChanged += (_, e) => renotified |= e.PropertyName == nameof(LibraryDrawerViewModel.SelectedVersion);

        await SwitchToAsync(vm, translated: false);

        Assert.True(renotified);
        Assert.False(vm.IsSwitching);
        Assert.Null(vm.Drawer!.SelectedVersion);
        Assert.NotNull(vm.SwitchProblem);
        Assert.True(vm.HasSwitchProblem);
        Assert.Equal(changed, await File.ReadAllBytesAsync(Archive, Ct));
        Assert.Equal("Patch no jogo", vm.Cards.Single().VersionTag);

        vm.HasSwitchProblem = false;

        Assert.Null(vm.SwitchProblem);
    }

    [Fact]
    public async Task Extrair_de_novo_no_dialogo_do_jogo_que_mudou_refaz_a_extracao_e_mostra_o_resumo()
    {
        var project = ProjectFolder.Open(_project);
        project.SaveState(new PipelineState([new OriginalFile(DemoBuild.Archive, 1, "extraido-antes")], new Dictionary<string, StageState>()));
        await File.WriteAllBytesAsync(Archive, DemoBuild.Payload(1000, 9), Ct);
        var vm = await LoadedAsync(Game(_project));
        await SwitchToAsync(vm, translated: true);
        Assert.Equal("O jogo mudou depois da extração", vm.SwitchProblem!.Title);
        Assert.True(vm.ExtractAgainCommand.CanExecute(null));

        await vm.ExtractAgainCommand.ExecuteAsync(null);

        Assert.True("O texto foi extraído de novo" == vm.SwitchProblem!.Title, vm.SwitchProblem.Message);
        Assert.Contains(" mantidas, ", vm.SwitchProblem.Message, StringComparison.Ordinal);
        Assert.Empty(project.LoadState().Originals);
        Assert.False(vm.IsSwitching);
        Assert.False(vm.ExtractAgainCommand.CanExecute(null));
    }

    [Fact]
    public void Injecao_recusada_porque_o_jogo_mudou_depois_da_extracao_diz_quantos_arquivos_diferem()
    {
        var refused = new InjectionException("recusada") { ChangedFiles = [new ChangedGameFile("a.ypf", "x", "y"), new ChangedGameFile("b.ypf", "x", null)] };

        var problem = LibraryViewModel.Describe(refused);

        Assert.Equal("O jogo mudou depois da extração", problem.Title);
        Assert.Equal("2 arquivos", problem.Differing);
        Assert.Equal("recusada", LibraryViewModel.Describe(new InjectionException("recusada")).Message);
        Assert.Equal("A troca não terminou", LibraryViewModel.Describe(new InvalidOperationException("estado ruim")).Title);
    }

    [Fact]
    public void Traduzido_sem_o_patch_descrito_e_desconhecido()
    {
        var state = new LibraryTranslationState(LibraryTranslationKind.Translated);

        Assert.False(LibraryVersionLabel.IsKnown(state));
        Assert.Null(LibraryVersionLabel.For(state).Tag);
    }

    private sealed class NoStarter : IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory) => throw new InvalidOperationException("Trocar não abre o jogo.");
    }
}
