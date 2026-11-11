using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

public sealed class LibraryProjectLinkerTests : IDisposable
{
    private readonly TempFolder _root = new();

    public void Dispose() => _root.Dispose();

    private string MakeProject(string name, string gameFolder, string? windowTitle = null)
    {
        var folder = Path.Combine(_root.Path, "projetos", name);
        var config = new ProjectConfig(
            name,
            new GameConfig(gameFolder, "Jogo.exe") { Title = windowTitle is null ? null : new GameTitleConfig(windowTitle, null) },
            new EngineConfig("fake"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default);
        ProjectFolder.Create(folder, config);
        return folder;
    }

    private LibraryGame GameAt(string name) =>
        new(
            GameLibrary.IdFor(Path.Combine(_root.Path, "jogos", name)),
            GameLibrary.NormalizeFolder(Path.Combine(_root.Path, "jogos", name)),
            "Jogo.exe",
            new LibraryEngine("kirikiri", "KiriKiri", LibraryEngineSource.Signature),
            name,
            null,
            DateTimeOffset.UnixEpoch);

    [Fact]
    public void Liga_o_projeto_ao_jogo_que_o_vnforge_json_aponta_e_traz_o_titulo_da_janela()
    {
        var library = new GameLibrary([GameAt("Um"), GameAt("Dois")]);
        var project = MakeProject("um", Path.Combine(_root.Path, "jogos", "Um"), "Título do Um");
        var other = MakeProject("fora", Path.Combine(_root.Path, "jogos", "NaoEstaNaBiblioteca"));

        var links = LibraryProjectLinker.LinkAll(library, [project, other, Path.Combine(_root.Path, "nao-e-projeto")]);

        var link = Assert.Single(links);
        Assert.Equal(GameLibrary.NormalizeFolder(project), link.ProjectPath);
        Assert.Equal(GameLibrary.NormalizeFolder(project), library.Find("Um").ProjectPath);
        Assert.Equal("Título do Um", library.Find("Um").WindowTitle);
        Assert.Null(library.Find("Dois").ProjectPath);
    }

    [Fact]
    public void Ligar_a_mao_aceita_projeto_de_jogo_que_mudou_de_lugar_e_recusa_pasta_que_nao_e_projeto()
    {
        var library = new GameLibrary([GameAt("Um")]);
        var project = MakeProject("antigo", Path.Combine(_root.Path, "outro-lugar"));

        var linked = LibraryProjectLinker.Link(library, library.Games[0], project);
        var e = Assert.Throws<LibraryException>(() => LibraryProjectLinker.Link(library, linked, _root.Path));

        Assert.Equal(GameLibrary.NormalizeFolder(project), linked.ProjectPath);
        Assert.Contains("não é um projeto", e.Message);
    }

    [Fact]
    public void Desligar_tira_so_a_ligacao_e_nao_toca_no_projeto()
    {
        var library = new GameLibrary([GameAt("Um")]);
        var project = MakeProject("um", Path.Combine(_root.Path, "jogos", "Um"));
        var linked = LibraryProjectLinker.Link(library, library.Games[0], project);

        var unlinked = LibraryProjectLinker.Unlink(library, linked);

        Assert.Null(unlinked.ProjectPath);
        Assert.True(File.Exists(Path.Combine(project, ProjectFolder.ConfigFileName)));
    }
}
