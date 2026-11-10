using Microsoft.Extensions.Time.Testing;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
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
        var vm = new LibraryViewModel(new LibraryFile(_config), new FakeFolderPicker(null), action => action(), readTranslation: _ => null, switcher: switcher);
        await vm.LoadAsync();
        vm.Select(vm.Cards.Single());
        return vm;
    }

    private async Task SwitchToAsync(LibraryViewModel vm, bool translated)
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
        Assert.False(vm.IsSwitching);
    }

    [Fact]
    public async Task Jogo_que_mudou_depois_da_injecao_recusa_a_volta_e_o_dialogo_explica()
    {
        var changed = DemoBuild.Payload(1000, 9);
        await File.WriteAllBytesAsync(Archive, changed, Ct);
        var vm = await LoadedAsync(Game(_project));

        await SwitchToAsync(vm, translated: false);

        Assert.NotNull(vm.SwitchProblem);
        Assert.True(vm.HasSwitchProblem);
        Assert.Equal(changed, await File.ReadAllBytesAsync(Archive, Ct));
        Assert.Equal("Patch no jogo", vm.Cards.Single().VersionTag);

        vm.HasSwitchProblem = false;

        Assert.Null(vm.SwitchProblem);
    }

    [Fact]
    public async Task Jogo_sem_projeto_nao_tem_etiqueta_nem_controle_de_versao()
    {
        var vm = await LoadedAsync(Game(null));

        Assert.False(vm.Cards.Single().HasVersionTag);
        Assert.False(vm.Drawer!.HasVersion);
        Assert.False(vm.Drawer.IsVersionKnown);
    }

    private sealed class NoStarter : IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory) => throw new InvalidOperationException("Trocar não abre o jogo.");
    }
}
