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
        return new GameLibrary([confirmed, pending], new LibraryPrivacy(ShowSpoilers: true, ShowAdult: false));
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
    public void O_arquivo_fica_em_library_json_com_enums_em_texto_e_sem_temporario_sobrando()
    {
        var file = new LibraryFile(Path.Combine(_dir.Path, "VNForge"));

        file.Save(Sample());

        Assert.Equal(Path.Combine(_dir.Path, "VNForge", "library.json"), file.Path);
        var text = File.ReadAllText(file.Path);
        Assert.Contains("\"confirmation\": \"manual\"", text);
        Assert.Contains("\"source\": \"signature\"", text);
        Assert.Equal([file.Path], Directory.GetFiles(Path.Combine(_dir.Path, "VNForge")));
    }

    [Fact]
    public void Privacidade_esconde_spoiler_e_adulto_por_padrao()
    {
        var privacy = LibraryFile.Parse("{\"version\":1,\"games\":[]}").Privacy;

        Assert.False(privacy.ShowSpoilers);
        Assert.False(privacy.ShowAdult);
        Assert.False(new GameLibrary().Privacy.ShowAdult);
    }

    [Fact]
    public void Sem_arquivo_a_biblioteca_e_vazia()
    {
        var library = new LibraryFile(Path.Combine(_dir.Path, "nada")).Load();

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
    public void O_json_tem_a_versao_e_e_valido()
    {
        using var document = JsonDocument.Parse(LibraryFile.Serialize(Sample()));

        Assert.Equal(LibraryFile.CurrentVersion, document.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("games").GetArrayLength());
    }

    [Fact]
    public void Id_nao_depende_de_maiusculas_nem_da_barra_final_e_muda_com_a_pasta()
    {
        var a = Path.Combine(_dir.Path, "Jogos", "Clannad");

        Assert.Equal(GameLibrary.IdFor(a), GameLibrary.IdFor(a.ToUpperInvariant() + Path.DirectorySeparatorChar));
        Assert.NotEqual(GameLibrary.IdFor(a), GameLibrary.IdFor(a + "2"));
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
    public void Jogo_fora_da_pasta_varrida_nao_e_dado_como_sumido()
    {
        var library = Sample();

        var merge = library.Merge([], Path.Combine(_dir.Path, "outra-unidade"));

        Assert.Empty(merge.Missing);
        Assert.Equal(2, library.Games.Count);
    }

    [Fact]
    public void Update_rele_o_arquivo_e_nao_perde_o_que_outro_gravou_no_meio()
    {
        var file = new LibraryFile(_dir.Path);
        file.Save(Sample());
        var stale = file.Load();

        new LibraryFile(_dir.Path).Update(l => l.Privacy = new LibraryPrivacy(ShowAdult: true));
        file.Update(l => l.Replace(l.Games[1] with { Title = "Mudou" }));

        var result = file.Load();
        Assert.Equal("Mudou", result.Games[1].Title);
        Assert.True(result.Privacy.ShowAdult);
        Assert.False(stale.Privacy.ShowAdult);
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

        Assert.Throws<LibraryException>(() => file.Update(l => l.Privacy = new LibraryPrivacy(ShowAdult: true)));

        Assert.Equal("{ isto não é json", File.ReadAllText(file.Path));
    }

    [Fact]
    public void Save_espera_um_leitor_soltar_o_arquivo_em_vez_de_falhar()
    {
        // O leitor só solta quando o Save pede a espera: nenhum relógio nem thread do pool entra na conta.
        FileStream? reader = null;
        var waits = 0;
        var file = new LibraryFile(_dir.Path, _ =>
        {
            waits++;
            reader?.Dispose();
        });
        file.Save(Sample());
        reader = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

        file.Save(new GameLibrary([Game(Path.Combine(_dir.Path, "novo"), "Novo")]));

        Assert.Equal("Novo", file.Load().Games.Single().Title);
        if (OperatingSystem.IsWindows())
        {
            // No Windows a troca por cima de um arquivo aberto falha, então o Save tem de ter esperado.
            Assert.Equal(1, waits);
        }

        reader.Dispose();
    }
}
