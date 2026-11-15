using System.Text.Json;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

[Trait("Risco", "R2")]
[Trait("Risco", "R7")]
public sealed class LibraryFileTests : IDisposable
{
    private static readonly DateTimeOffset Added = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    private static LibraryGame Game(string folder, string title = "Jogo") =>
        new(GameLibrary.IdFor(folder), GameLibrary.NormalizeFolder(folder), "Jogo.exe", new LibraryEngine("kirikiri", "KiriKiri", LibraryEngineSource.Signature), title, "Janela", Added);

    private GameLibrary Sample()
    {
        var confirmed = Game(Path.Combine(_dir.Path, "jogos", "Clannad"), "CLANNAD") with
        {
            VndbId = "v4",
            Confirmation = LibraryConfirmation.Manual,
            MatchScore = 0.97,
            SearchedAt = Added,
            Candidates = [new LibraryCandidate("v4", "CLANNAD", "クラナド", "2004-04-28", 0.97, ["janela", "pasta"])],
            Info = new LibraryVndbInfo(
                "CLANNAD",
                "クラナド",
                "2004-04-28",
                "https://example.test/cover.jpg",
                "Sinopse [spoiler]final[/spoiler].",
                5,
                4000,
                120,
                [new LibraryTag("g1", "Drama", "cont", 2.9, 0), new LibraryTag("g2", "Final", "cont", 2.0, 2)],
                Adult: false),
            ProjectPath = Path.Combine(_dir.Path, "projetos", "clannad"),
        };
        var pending = Game(Path.Combine(_dir.Path, "jogos", "Outro"), "Outro") with
        {
            SearchedAt = Added,
            Candidates = [new LibraryCandidate("v9", "Outro", null, null, 0.5, ["exe"])],
        };
        return new GameLibrary([confirmed, pending], new LibraryPrivacy(ShowSpoilers: true, Adult: AdultMode.Blur));
    }

    [Fact]
    public void Ida_e_volta_pelo_arquivo_guarda_tudo()
    {
        var file = new LibraryFile(_dir.Path);
        var library = Sample();

        file.Save(library);
        var loaded = file.Load();

        Assert.Equal(LibraryFile.Serialize(library), LibraryFile.Serialize(loaded));
        var confirmed = loaded.Games[0];
        Assert.Equal("v4", confirmed.VndbId);
        Assert.Equal(LibraryConfirmation.Manual, confirmed.Confirmation);
        Assert.Equal(0.97, confirmed.MatchScore);
        Assert.Equal("クラナド", confirmed.Info?.OriginalTitle);
        Assert.Equal(["Drama", "Final"], confirmed.Info!.Tags.Select(t => t.Name));
        Assert.Equal(2, confirmed.Info.Tags[1].Spoiler);
        Assert.Equal(["janela", "pasta"], confirmed.Candidates[0].Evidence);
        Assert.Equal(Added, confirmed.AddedAt);
        Assert.Equal(library.Games[0].ProjectPath, confirmed.ProjectPath);
        Assert.True(loaded.Privacy.ShowSpoilers);
        Assert.False(loaded.Privacy.ShowAdult);
        Assert.Null(loaded.Games[1].VndbId);
        Assert.False(loaded.Games[1].IsConfirmed);
    }

    [Fact]
    public void Privacidade_esconde_spoiler_e_desfoca_adulto_por_padrao()
    {
        var privacy = LibraryFile.Parse("{\"version\":1,\"games\":[]}").Privacy;

        Assert.False(privacy.ShowSpoilers);
        Assert.Equal(AdultMode.Blur, privacy.Adult);
        Assert.Equal(AdultMode.Blur, new GameLibrary().Privacy.Adult);
    }

    [Fact]
    public void Arquivo_ilegivel_recusa_e_nao_e_sobrescrito()
    {
        var file = new LibraryFile(_dir.Path);
        File.WriteAllText(file.Path, "{ isto não é json");

        var e = Assert.Throws<LibraryException>(file.Load);

        Assert.Contains(file.Path, e.Message);
        Assert.Equal("{ isto não é json", File.ReadAllText(file.Path));
    }

    [Fact]
    public void Id_repetido_e_recusado()
    {
        var one = Game(Path.Combine(_dir.Path, "a"));
        var json = LibraryFile.Serialize(new GameLibrary([one, one with { Folder = "outra" }]));

        var e = Assert.Throws<LibraryException>(() => LibraryFile.Parse(json));

        Assert.Contains("mais de uma vez", e.Message);
    }

    [Fact]
    public void Update_com_arquivo_ilegivel_recusa_e_nao_o_sobrescreve()
    {
        var file = new LibraryFile(_dir.Path);
        File.WriteAllText(file.Path, "{ isto não é json");

        Assert.Throws<LibraryException>(() => file.Update(l => l.Privacy = new LibraryPrivacy(Adult: AdultMode.Show)));

        Assert.Equal("{ isto não é json", File.ReadAllText(file.Path));
    }

    [Fact]
    public void Erro_de_escrita_sai_como_LibraryException_no_Save_e_no_Update()
    {
        // Uma pasta no lugar do library.json faz a troca do temporário falhar com IOException ou UnauthorizedAccessException.
        var file = new LibraryFile(_dir.Path, _ => { });
        Directory.CreateDirectory(file.Path);

        var save = Assert.Throws<LibraryException>(() => file.Save(Sample()));
        var update = Assert.Throws<LibraryException>(() => file.Update(l => l.Privacy = new LibraryPrivacy(Adult: AdultMode.Show)));

        Assert.Contains("library.json", save.Message, StringComparison.Ordinal);
        Assert.NotNull(update.InnerException);
    }
}
