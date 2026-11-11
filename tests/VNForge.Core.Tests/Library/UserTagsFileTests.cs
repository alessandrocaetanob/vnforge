using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

public sealed class UserTagsFileTests : IDisposable
{
    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    private static UserTags Sample()
    {
        var tags = new UserTags();
        tags.Add("gb2", "rejogar");
        tags.Add("ga1", "favorito", "#ff8800");
        tags.Add("ga1", "pra traduzir");
        tags.Add("gb2", "favorito");
        tags.Define("sem uso");
        tags.SavedFilter = LibraryTagFilter.Create(["favorito", "vndb:g1"], any: true, ["minha:rejogar"]);
        return tags;
    }

    [Fact]
    public void Ida_e_volta_pelo_arquivo_guarda_tudo()
    {
        var file = new UserTagsFile(_dir.Path);
        var tags = Sample();

        file.Save(tags);
        var loaded = file.Load();

        Assert.Equal(UserTagsFile.Serialize(tags), UserTagsFile.Serialize(loaded));
        Assert.Equal(["favorito", "pra traduzir", "rejogar", "sem uso"], loaded.Tags.Select(t => t.Name));
        Assert.Equal("#ff8800", loaded.Get("favorito").Color);
        Assert.Equal(["favorito", "pra traduzir"], loaded.TagsOf("ga1"));
        Assert.Equal(["favorito", "rejogar"], loaded.TagsOf("gb2"));
        Assert.Empty(loaded.GamesWith("sem uso"));
        Assert.Equal(["favorito", "vndb:g1"], loaded.SavedFilter.Include);
        Assert.True(loaded.SavedFilter.Any);
        Assert.Equal(["minha:rejogar"], loaded.SavedFilter.Exclude);
    }

    [Fact]
    public void Gravacao_atomica_nao_deixa_temporario_e_troca_o_arquivo_inteiro()
    {
        var file = new UserTagsFile(_dir.Path);
        file.Save(Sample());
        var other = new UserTags();
        other.Add("g9", "unica");

        file.Save(other);

        Assert.Equal([UserTagsFile.FileName], Directory.GetFiles(_dir.Path).Select(System.IO.Path.GetFileName));
        Assert.Equal(["unica"], file.Load().Tags.Select(t => t.Name));
    }

    [Fact]
    public void Update_em_paralelo_serializa_as_gravacoes_sem_perder_nenhuma()
    {
        var file = new UserTagsFile(_dir.Path);

        Parallel.For(0, 12, new ParallelOptions { CancellationToken = TestContext.Current.CancellationToken, MaxDegreeOfParallelism = 6 }, i =>
            new UserTagsFile(_dir.Path).Update(t => t.Add($"g{i}", $"tag {i}")));

        var loaded = file.Load();
        Assert.Equal(12, loaded.Tags.Count);
        Assert.Equal(12, loaded.GameIds.Count);
        Assert.Equal([file.Path], Directory.GetFiles(_dir.Path).Where(f => !f.EndsWith(".lock", StringComparison.Ordinal)));
    }

    [Fact]
    public void Update_devolve_o_resultado_e_com_arquivo_ilegivel_recusa_sem_sobrescrever()
    {
        var file = new UserTagsFile(_dir.Path);
        Assert.Equal(1, file.Update(t => t.Add("g1", "a") is { } ? 1 : 0));
        File.WriteAllText(file.Path, "{ isto não é json");

        Assert.Throws<LibraryException>(() => file.Update(t => t.Add("g1", "b")));

        Assert.Equal("{ isto não é json", File.ReadAllText(file.Path));
    }

    [Fact]
    public void Update_que_falha_na_mudanca_nao_grava()
    {
        var file = new UserTagsFile(_dir.Path);
        file.Update(t => t.Add("g1", "a"));

        Assert.Throws<LibraryException>(() => file.Update(t => t.Rename("a", "vndb:x")));

        Assert.Equal(["a"], file.Load().Tags.Select(t => t.Name));
    }

    [Fact]
    public void Save_que_nao_consegue_trocar_o_arquivo_cita_o_caminho_e_nao_deixa_temporario()
    {
        var file = new UserTagsFile(_dir.Path);
        Directory.CreateDirectory(file.Path);

        var error = Assert.Throws<LibraryException>(() => file.Save(Sample()));

        Assert.Contains(file.Path, error.Message);
        Assert.NotNull(error.InnerException);
        Assert.DoesNotContain(Directory.GetFiles(_dir.Path), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void Arquivo_ilegivel_e_recusado_e_continua_intacto()
    {
        var file = new UserTagsFile(_dir.Path);
        File.WriteAllText(file.Path, "{ isto não é json");

        var error = Assert.Throws<LibraryException>(file.Load);

        Assert.Contains(UserTagsFile.FileName, error.Message);
        Assert.Equal("{ isto não é json", File.ReadAllText(file.Path));
    }

    [Theory]
    [InlineData("{\"version\":2}", "versão 2")]
    [InlineData("null", "vazio")]
    [InlineData("{\"version\":1,\"tags\":[{\"name\":\"a\"},{\"name\":\" A \"}]}", "mais de uma vez")]
    [InlineData("{\"version\":1,\"tags\":[{\"color\":\"#fff000\"}]}", "sem nome")]
    [InlineData("{\"version\":1,\"games\":{\"g1\":[\"fantasma\"]}}", "fantasma")]
    [InlineData("{\"version\":1,\"tags\":[{\"name\":\"a\"}],\"filter\":{\"include\":[\" \"]}}", "vazia")]
    public void Arquivo_fora_do_formato_diz_o_que_esta_errado(string json, string expected)
    {
        var error = Assert.Throws<LibraryException>(() => UserTagsFile.Parse(json, "tags.json"));

        Assert.Contains("tags.json", error.Message);
        Assert.Contains(expected, error.Message);
    }
}
