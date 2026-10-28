using System.Text;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Ajustes &gt; Estilo lê o título do jogo num projeto extraído antes de a extração lê-lo, sem a CLI (#433).</summary>
public class SettingsGameTitleReadTests
{
    private const string CachedTitleFile = "titulo.cfg";

    private static SettingsViewModel Create(TempFolder temp, params IEngine[] engines) =>
        new(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("config")), _ => null, engines: engines);

    private static async Task LoadAsync(SettingsViewModel vm, ProjectSummary summary)
    {
        vm.Load(summary);
        await vm.EstimateLoading;
    }

    /// <summary>Um projeto como o <c>vnf extract</c> da teste.12 deixava: texto e <c>.cache/</c>, mas sem <c>game.title</c> no <c>vnforge.json</c>.</summary>
    private static (ProjectSummary Summary, ProjectFolder Folder) OldProject(TempFolder temp, string? cachedTitle)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        if (cachedTitle is not null)
        {
            var pack = Directory.CreateDirectory(Path.Combine(folder.GameCacheDirectory, "pacote"));
            File.WriteAllText(Path.Combine(pack.FullName, CachedTitleFile), cachedTitle);
        }

        return (ProjectSummaryReader.Read(root), folder);
    }

    [Fact]
    public async Task Projeto_sem_titulo_oferece_ler_e_o_botao_preenche_o_original_sem_tocar_no_texto()
    {
        using var temp = new TempFolder();
        var (summary, folder) = OldProject(temp, "原題");
        var textBefore = File.ReadAllBytes(folder.TextPath("yst00001"));
        var vm = Create(temp, new TitleEngine());
        var saved = 0;
        vm.ProjectSaved += (_, _) => saved++;

        await LoadAsync(vm, summary);
        Assert.False(vm.HasGameTitle);
        Assert.True(vm.CanReadGameTitle);

        vm.ReadGameTitleCommand.Execute(null);

        Assert.True(vm.HasGameTitle);
        Assert.Equal("原題", vm.GameTitleOriginal);
        Assert.Equal(string.Empty, vm.GameTitleTranslation);
        Assert.False(vm.CanReadGameTitle);
        Assert.Null(vm.GameTitleReadMessage);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(1, saved);
        Assert.Equal(new GameTitleConfig("原題", null), ProjectFolder.Open(summary.Root).LoadConfig().Game.Title);
        Assert.Equal(textBefore, File.ReadAllBytes(folder.TextPath("yst00001")));
    }

    [Fact]
    public async Task Depois_de_ler_digitar_a_traducao_grava_junto_do_original()
    {
        using var temp = new TempFolder();
        var (summary, _) = OldProject(temp, "原題");
        var vm = Create(temp, new TitleEngine());
        await LoadAsync(vm, summary);
        vm.ReadGameTitleCommand.Execute(null);

        vm.GameTitleTranslation = "Meu Título";

        Assert.Equal(new GameTitleConfig("原題", "Meu Título"), ProjectFolder.Open(summary.Root).LoadConfig().Game.Title);
    }

    [Fact]
    public async Task Cache_sem_o_arquivo_do_titulo_explica_e_mantem_o_botao()
    {
        using var temp = new TempFolder();
        var (summary, _) = OldProject(temp, cachedTitle: null);
        var vm = Create(temp, new TitleEngine());
        await LoadAsync(vm, summary);

        vm.ReadGameTitleCommand.Execute(null);

        Assert.False(vm.HasGameTitle);
        Assert.True(vm.CanReadGameTitle);
        Assert.True(vm.HasGameTitleReadMessage);
        Assert.Contains(".cache", vm.GameTitleReadMessage, StringComparison.Ordinal);
        Assert.Null(ProjectFolder.Open(summary.Root).LoadConfig().Game.Title);
    }

    [Fact]
    public async Task Titulo_ilegivel_vira_mensagem_e_nao_erro_do_projeto()
    {
        using var temp = new TempFolder();
        var (summary, _) = OldProject(temp, "原題");
        var vm = Create(temp, new TitleEngine { Failure = new VNForgeException("o yscfg.ybn é de uma versão que não conheço.") });
        await LoadAsync(vm, summary);

        vm.ReadGameTitleCommand.Execute(null);

        Assert.False(vm.HasGameTitle);
        Assert.Contains("yscfg.ybn", vm.GameTitleReadMessage, StringComparison.Ordinal);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Engine_sem_titulo_nao_oferece_o_botao()
    {
        using var temp = new TempFolder();
        var (summary, _) = OldProject(temp, "原題");
        var vm = Create(temp, new FakeEngine());

        await LoadAsync(vm, summary);

        Assert.False(vm.CanReadGameTitle);
    }

    [Fact]
    public async Task Projeto_que_ja_tem_titulo_e_tela_sem_projeto_nao_oferecem_o_botao()
    {
        using var temp = new TempFolder();
        var (summary, folder) = OldProject(temp, "原題");
        var vm = Create(temp, new TitleEngine());
        Assert.False(vm.CanReadGameTitle);

        var config = folder.LoadConfig();
        folder.SaveConfig(config with { Game = config.Game with { Title = new GameTitleConfig("原題", "Meu Jogo") } });
        await LoadAsync(vm, summary);

        Assert.True(vm.HasGameTitle);
        Assert.False(vm.CanReadGameTitle);
    }

    [Fact]
    public async Task Trocar_de_projeto_limpa_a_mensagem_da_leitura()
    {
        using var temp = new TempFolder();
        var (summary, _) = OldProject(temp, cachedTitle: null);
        var vm = Create(temp, new TitleEngine());
        await LoadAsync(vm, summary);
        vm.ReadGameTitleCommand.Execute(null);
        Assert.NotNull(vm.GameTitleReadMessage);

        await LoadAsync(vm, summary);

        Assert.Null(vm.GameTitleReadMessage);
    }

    /// <summary>A engine <c>yuris</c> dos exemplos, com o título no <c>titulo.cfg</c> de qualquer subpasta do cache.</summary>
    private sealed class TitleEngine : IEngine, IGameTitleSource
    {
        public VNForgeException? Failure { get; init; }

        public string Id => "yuris";

        public EngineDetection Detect(string gameDir) => EngineDetection.NotDetected;

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];

        public GameTitleFile? ReadTitle(string unpackedDirectory)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            var path = Directory.EnumerateFiles(unpackedDirectory, CachedTitleFile, SearchOption.AllDirectories).FirstOrDefault();
            return path is null ? null : new GameTitleFile(path, File.ReadAllBytes(path), File.ReadAllText(path));
        }

        public byte[] WithTitle(GameTitleFile file, string title, ITextEncoder encoder) => Encoding.UTF8.GetBytes(title);
    }
}
