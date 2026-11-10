using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using static VNForge.Core.Tests.Library.LibraryCharactersSamples;

namespace VNForge.Core.Tests.Library;

/// <summary>#558: o arquivo <c>library-editions.json</c> que guarda a edição casada com a pasta de cada jogo.</summary>
public sealed class GameEditionsFileTests : IDisposable
{
    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Ida_e_volta_grava_e_le_as_edicoes_sem_perder_nada()
    {
        var file = new GameEditionsFile(_dir.Path);
        var editions = new GameEditions();
        editions.Set("g2", Star, ["r3"], Now);
        editions.Set("g1", Moon, ["r2", "r1", "r1"], Now.AddHours(1));
        editions.Set("g3", Outside, [], Now, incomplete: true);

        file.Save(editions);
        var loaded = file.Load();

        Assert.Equal(["g1", "g2", "g3"], loaded.GameIds);
        Assert.Equal((Moon, Now.AddHours(1)), (loaded.Of("g1")!.VisualNovelId, loaded.Of("g1")!.MatchedAt));
        Assert.Equal(["r1", "r2"], loaded.Of("g1")!.ReleaseIds);
        Assert.Equal(["r3"], loaded.Of("g2")!.ReleaseIds);
        Assert.Empty(loaded.Of("g3")!.ReleaseIds);
        Assert.Equal((false, true), (loaded.Of("g1")!.Incomplete, loaded.Of("g3")!.Incomplete));
        Assert.DoesNotContain("incomplete", File.ReadAllText(file.Path).Split("\"g3\"")[0], StringComparison.Ordinal);
        Assert.Equal(GameEditionsFile.Serialize(editions), GameEditionsFile.Serialize(loaded));
        Assert.Equal(Path.Combine(_dir.Path, "library-editions.json"), file.Path);
    }

    [Fact]
    public void Arquivo_que_nao_existe_e_uma_lista_vazia()
    {
        Assert.Empty(new GameEditionsFile(_dir.Path).Load().GameIds);
    }

    [Fact]
    public void Update_le_de_novo_sob_a_trava_e_nao_perde_o_que_outro_gravou()
    {
        var file = new GameEditionsFile(_dir.Path);
        file.Update(e => e.Set("g1", Moon, ["r1"], Now));
        new GameEditionsFile(_dir.Path).Update(e => e.Set("g2", Star, ["r3"], Now));

        var result = file.Update(e => e.GameIds.Count);

        Assert.Equal(2, result);
        Assert.Equal(["g1", "g2"], file.Load().GameIds);
    }

    [Theory]
    [InlineData("{ nao e json")]
    [InlineData("null")]
    [InlineData("""{ "version": 7, "games": {} }""")]
    [InlineData("""{ "version": 1, "games": { " ": { "visualNovelId": "v1", "releaseIds": [] } } }""")]
    [InlineData("""{ "version": 1, "games": { "g1": { "releaseIds": [] } } }""")]
    [InlineData("""{ "version": 1, "games": { "g1": { "visualNovelId": "v1" } } }""")]
    [InlineData("""{ "version": 1, "games": { "g1": { "visualNovelId": "v1", "releaseIds": [" "] } } }""")]
    [InlineData("""{ "version": 1, "games": { "g1": null } }""")]
    public void Arquivo_ilegivel_e_recusado_citando_o_arquivo_e_nunca_sobrescrito(string json)
    {
        var file = new GameEditionsFile(_dir.Path);
        File.WriteAllText(file.Path, json);

        var load = Assert.Throws<LibraryException>(() => file.Load());
        Assert.Throws<LibraryException>(() => file.Update(e => e.Set("g9", Moon, [], Now)));

        Assert.Contains("library-editions.json", load.Message, StringComparison.Ordinal);
        Assert.Equal(json, File.ReadAllText(file.Path));
    }

    [Fact]
    public void O_registro_so_vale_para_o_jogo_do_VNDB_em_que_foi_feito()
    {
        var editions = new GameEditions();
        editions.Set("g1", Moon, ["r1"], Now);
        var game = Owned("g1", "Moon Flower", Moon, "Moon Flower");
        var other = game with { VndbId = Star };
        var unmatched = game with { VndbId = null };

        Assert.True(editions.IsCurrent(game));
        Assert.Equal(["r1"], editions.CurrentFor(game));
        Assert.False(editions.IsCurrent(other));
        Assert.Empty(editions.CurrentFor(other));
        Assert.False(editions.IsCurrent(unmatched));
        Assert.True(editions.Clear("g1"));
        Assert.False(editions.Clear("g1"));
    }
}
