using System.Text.Json;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

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

    [Theory]
    [InlineData("\"showAdult\":true", AdultMode.Show)]
    [InlineData("\"showAdult\":true,\"adult\":\"hide\"", AdultMode.Hide)]
    public void O_arquivo_antigo_com_showAdult_e_o_novo_com_adult_dao_o_modo(string fields, AdultMode expected)
    {
        var privacy = LibraryFile.Parse("{\"version\":1,\"privacy\":{\"showSpoilers\":true," + fields + "},\"games\":[]}").Privacy;

        Assert.True(privacy.ShowSpoilers);
        Assert.Equal(expected, privacy.Adult);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"adult\":\"borrar\"}")]
    [InlineData("{\"adult\":3}")]
    [InlineData("{\"showAdult\":\"sim\"}")]
    [InlineData("{\"showAdult\":null,\"showSpoilers\":\"sim\"}")]
    public void Uma_privacidade_estranha_vale_o_padrao_e_nao_derruba_o_arquivo(string privacyJson)
    {
        var library = LibraryFile.Parse("{\"version\":1,\"privacy\":" + privacyJson + ",\"games\":[]}");

        Assert.Equal(AdultMode.Blur, library.Privacy.Adult);
        Assert.Empty(library.Games);
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

    [Theory]
    [InlineData("{\"version\":2,\"games\":[]}", "versão 2")]
    [InlineData("{\"version\":1,\"games\":[{\"id\":\"\",\"folder\":\"C:\\\\a\",\"engine\":{\"id\":\"x\",\"name\":\"X\",\"source\":\"engine\"},\"title\":\"t\",\"addedAt\":\"2026-11-09T12:00:00+00:00\"}]}", "não tem id")]
    [InlineData("null", "vazio")]
    public void Formato_invalido_vira_erro_esperado_com_o_arquivo_no_texto(string json, string expected)
    {
        var e = Assert.Throws<LibraryException>(() => LibraryFile.Parse(json, "library.json"));

        Assert.Contains(expected, e.Message);
        Assert.Contains("library.json", e.Message);
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
    public void Achar_por_id_nome_da_pasta_ou_pedaco_do_titulo()
    {
        var library = Sample();
        var clannad = library.Games[0];

        Assert.Equal(clannad, library.Find(clannad.Id));
        Assert.Equal(clannad, library.Find("clannad"));
        Assert.Equal(library.Games[1], library.Find("outr"));
        Assert.Throws<LibraryException>(() => library.Find("nada"));
        var fates = new GameLibrary([Game(Path.Combine(_dir.Path, "Fate Zero"), "Fate Zero"), Game(Path.Combine(_dir.Path, "Fate Stay"), "Fate Stay")]);
        Assert.Contains("combina com 2", Assert.Throws<LibraryException>(() => fates.Find("fate")).Message);
        Assert.Equal("Fate Stay", fates.Find("Fate Stay").Title);
    }

    [Fact]
    public void Varrer_de_novo_guarda_o_casamento_e_diz_quem_sumiu()
    {
        var library = Sample();
        var root = Path.Combine(_dir.Path, "jogos");
        var seenAgain = library.Games[0] with { Exe = "Novo.exe", Engine = new LibraryEngine("renpy", "Ren'Py", LibraryEngineSource.Signature), VndbId = null, Info = null };
        var brandNew = Game(Path.Combine(root, "Terceiro"), "Terceiro");

        var merge = library.Merge([seenAgain, brandNew], root);

        Assert.Equal([brandNew.Id], merge.Added.Select(g => g.Id));
        Assert.Equal([seenAgain.Id], merge.Updated.Select(g => g.Id));
        Assert.Equal([library.Games[1].Id], merge.Missing.Select(g => g.Id));
        var kept = library.Games.Single(g => g.Id == seenAgain.Id);
        Assert.Equal("v4", kept.VndbId);
        Assert.NotNull(kept.Info);
        Assert.Equal("Novo.exe", kept.Exe);
        Assert.Equal("renpy", kept.Engine.Id);
        Assert.Equal("CLANNAD", kept.Title);
        Assert.Equal(3, library.Games.Count);
    }

    [Fact]
    public void Update_em_paralelo_serializa_as_gravacoes()
    {
        var file = new LibraryFile(_dir.Path);

        Parallel.For(0, 12, new ParallelOptions { CancellationToken = TestContext.Current.CancellationToken, MaxDegreeOfParallelism = 6 }, i =>
            new LibraryFile(_dir.Path).Update(l => l.Merge([Game(Path.Combine(_dir.Path, "jogos", $"J{i}"), $"J{i}")], Path.Combine(_dir.Path, "jogos"))));

        Assert.Equal(12, file.Load().Games.Count);
        Assert.Equal([file.Path], Directory.GetFiles(_dir.Path).Where(f => !f.EndsWith(".lock", StringComparison.Ordinal)));
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
