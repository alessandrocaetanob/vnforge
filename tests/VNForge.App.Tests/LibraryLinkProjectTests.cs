using System.Security.Cryptography;
using System.Text;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>"Ligar a um projeto que já existe" (#688): a gaveta da Biblioteca liga o jogo a um projeto que o vnf library link também ligaria.</summary>
public sealed class LibraryLinkProjectTests
{
    private const string Ypf = "ysbin.ypf";
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static LibraryGame GameIn(string folder) =>
        new(GameLibrary.IdFor(folder), GameLibrary.NormalizeFolder(folder), "jogo.exe", new LibraryEngine("yuris", "Yu-RIS", LibraryEngineSource.Engine), "Nanairo Ressha", "七色列車", Now);

    private static string WriteGame(string folder)
    {
        File.WriteAllText(Path.Combine(folder, Ypf), "original");
        File.WriteAllText(Path.Combine(folder, "jogo.exe"), "MZ");
        return folder;
    }

    // O projeto lê o jogo em <paramref name="projectGame"/>, a pasta onde o jogo estava; a Biblioteca já o tem em outra.
    private static string ProjectOf(TempFolder temp, string projectGame)
    {
        var root = SampleProjects.Create(temp.SubFolder("projeto"), "Nanairo Ressha", TranslationStatus.Machine, TranslationStatus.Approved);
        var project = ProjectFolder.Open(root);
        var config = project.LoadConfig();
        project.SaveConfig(config with { Game = config.Game with { Path = projectGame.Replace('\\', '/') } });
        var bytes = Encoding.UTF8.GetBytes("original");
        project.SaveState(new PipelineState([new OriginalFile(Ypf, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)))], new Dictionary<string, StageState>(), projectGame));
        return root;
    }

    private static (LibraryViewModel Library, LibraryFile File) LibraryOf(TempFolder temp, LibraryGame game, string? picked)
    {
        var file = LibrarySamples.Save(temp.SubFolder("biblioteca"), LibrarySamples.Library([game]));
        var editor = new LibraryEditor(file, () => throw new InvalidOperationException("sem VNDB"));
        return (new LibraryViewModel(file, new FakeFolderPicker(picked), action => action(), new LibraryServices { Editor = editor, ReadTranslation = LibraryCatalog.ReadTranslation }), file);
    }

    [Fact]
    public async Task Ligar_pelo_app_um_jogo_que_mudou_de_pasta_grava_o_mesmo_library_json_do_vnf_library_link()
    {
        using var temp = new TempFolder();
        var game = GameIn(WriteGame(temp.SubFolder("jogo-novo")));
        var root = ProjectOf(temp, temp.SubFolder("jogo-antigo"));
        var (library, file) = LibraryOf(temp, game, root);
        var expected = LibrarySamples.Save(temp.SubFolder("esperada"), LibrarySamples.Library([game]));
        await library.LoadAsync();

        await library.LinkProjectAsync(game);

        Assert.True(library.HasLinkDialog);
        Assert.True(library.LinkDialog!.CanConfirm);
        Assert.Equal(game.Folder, library.LinkDialog.ToFolder);
        await library.ConfirmLinkCommand.ExecuteAsync(null);

        // O vnf library link é a troca da pasta (FollowGame) seguida de Link: o projeto já foi trocado, então só o Link resta para a cópia.
        expected.Update(current => LibraryProjectLinker.Link(current, current.Find(game.Id), root));
        Assert.Equal(File.ReadAllText(expected.Path), File.ReadAllText(file.Path));
        Assert.Equal(game.Folder, GameLibrary.NormalizeFolder(Path.GetFullPath(ProjectFolder.Open(root).LoadConfig().Game.Path, root)));
        Assert.False(library.HasLinkDialog);
    }

    [Fact]
    public async Task Depois_de_ligar_o_projeto_abrir_o_jogo_abre_o_projeto_e_nao_o_assistente()
    {
        using var temp = new TempFolder();
        var game = GameIn(WriteGame(temp.SubFolder("jogo")));
        var root = ProjectOf(temp, game.Folder);
        var (library, _) = LibraryOf(temp, game, root);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now)), library: library);
        await library.LoadAsync();

        await vm.LinkExistingProjectCommand.ExecuteAsync(game);
        Assert.False(library.HasLinkDialog);
        Assert.NotNull(library.Cards[0].Card.ProgressPercent);

        library.OpenGameCommand.Execute(library.Cards[0]);
        await vm.ProjectOpening;

        Assert.Same(vm.Sheet, vm.CurrentPage);
        Assert.Equal(root, vm.CurrentProject!.Root);
    }

    [Fact]
    public async Task Ligar_pela_Ficha_sem_projeto_com_o_dialogo_da_pasta_diferente_abre_o_projeto_ao_confirmar()
    {
        using var temp = new TempFolder();
        var game = GameIn(WriteGame(temp.SubFolder("jogo-novo")));
        var root = ProjectOf(temp, temp.SubFolder("jogo-antigo"));
        var (library, _) = LibraryOf(temp, game, root);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now)), library: library);
        await library.LoadAsync();
        library.OpenGameCommand.Execute(library.Cards[0]);
        Assert.True(vm.InProjectlessGame);

        await vm.LinkExistingProjectCommand.ExecuteAsync(game);
        Assert.True(library.HasLinkDialog);
        Assert.Same(library, vm.CurrentPage);
        await library.ConfirmLinkCommand.ExecuteAsync(null);
        await vm.ProjectOpening;

        Assert.Equal(root, vm.CurrentProject!.Root);
        Assert.Same(vm.Sheet, vm.CurrentPage);
    }

    [Fact]
    public async Task Pasta_sem_vnforge_json_mostra_o_erro_em_portugues_e_nao_liga_nada()
    {
        using var temp = new TempFolder();
        var game = GameIn(WriteGame(temp.SubFolder("jogo")));
        var (library, file) = LibraryOf(temp, game, temp.SubFolder("vazia"));
        var before = File.ReadAllBytes(file.Path);
        await library.LoadAsync();

        await library.LinkProjectAsync(game);

        Assert.Contains("vnforge.json", library.ErrorMessage);
        Assert.Contains("não é um projeto do VNForge", library.ErrorMessage);
        Assert.False(library.HasLinkDialog);
        Assert.Equal(before, File.ReadAllBytes(file.Path));
    }
}
