using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

public sealed class GameNotesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 20, 0, 0, TimeSpan.FromHours(-3));

    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData(null)]
    public void Set_com_texto_em_branco_apaga_a_nota(string? blank)
    {
        var notes = new GameNotes();
        notes.Set("g1", "algo", Now);

        Assert.Null(notes.Set("g1", blank, Now));

        Assert.Null(notes.Of("g1"));
        Assert.Empty(notes.GameIds);
    }

    [Fact]
    public void Append_poe_o_texto_numa_linha_nova_ou_comeca_a_nota()
    {
        var notes = new GameNotes();

        notes.Append("g1", "primeira", Now);
        var second = notes.Append("g1", "segunda\n", Now.AddHours(1));

        Assert.Equal("primeira\nsegunda", second.Text);
        Assert.Equal(Now.AddHours(1), second.UpdatedAt);
    }

    [Fact]
    public void Nota_longa_demais_e_recusada()
    {
        var notes = new GameNotes();

        var error = Assert.Throws<LibraryException>(() => notes.Set("g1", new string('a', GameNotes.MaxLength + 1), Now));

        Assert.Contains("100000", error.Message);
        Assert.Null(notes.Of("g1"));
    }

    [Fact]
    public void Clear_diz_se_o_jogo_tinha_nota()
    {
        var notes = new GameNotes();
        notes.Set("g1", "x", Now);

        Assert.True(notes.Clear("g1"));
        Assert.False(notes.Clear("g1"));
    }

    [Fact]
    public void Ida_e_volta_pelo_arquivo_guarda_texto_acentos_e_data()
    {
        var file = new GameNotesFile(_dir.Path);
        var notes = new GameNotes();
        notes.Set("gb2", "Tradução do título: \"Fim\" 終わり", Now);
        notes.Set("ga1", "linha 1\nlinha 2", Now.AddDays(1));

        file.Save(notes);
        var loaded = file.Load();

        Assert.Equal(["ga1", "gb2"], loaded.GameIds);
        Assert.Equal(new GameNote("linha 1\nlinha 2", Now.AddDays(1)), loaded.Of("ga1"));
        Assert.Equal("Tradução do título: \"Fim\" 終わり", loaded.Of("gb2")!.Text);
        Assert.Equal(GameNotesFile.Serialize(notes), File.ReadAllText(file.Path));
        Assert.EndsWith("library-notes.json", file.Path);
    }

    [Fact]
    public void Update_relê_o_arquivo_e_nao_grava_quando_a_mudanca_falha()
    {
        var file = new GameNotesFile(_dir.Path);
        file.Update(n => n.Set("g1", "antes", Now));
        var before = File.ReadAllText(file.Path);

        Assert.Throws<LibraryException>(() => file.Update(n =>
        {
            n.Set("g1", "depois", Now);
            n.Append("g2", " ", Now);
        }));

        Assert.Equal(before, File.ReadAllText(file.Path));
        Assert.Equal("antes", file.Load().Of("g1")!.Text);
    }

    [Fact]
    public void Update_sem_resultado_grava_a_mudanca()
    {
        var file = new GameNotesFile(_dir.Path);

        file.Update(n =>
        {
            n.Set("g1", "feita", Now);
        });

        Assert.Equal("feita", file.Load().Of("g1")!.Text);
    }

    [Fact]
    public void Arquivo_com_nota_longa_demais_e_recusado()
    {
        var json = $"{{\"version\":1,\"games\":{{\"g1\":{{\"text\":\"{new string('a', GameNotes.MaxLength + 1)}\",\"updatedAt\":\"2026-11-09T20:00:00-03:00\"}}}}}}";

        var error = Assert.Throws<LibraryException>(() => GameNotesFile.Parse(json, "notas.json"));

        Assert.Contains("passa de", error.Message);
    }

    [Fact]
    public void Arquivo_ilegivel_e_recusado_e_nao_e_sobrescrito()
    {
        var file = new GameNotesFile(_dir.Path);
        File.WriteAllText(file.Path, "{ não é json");

        var error = Assert.Throws<LibraryException>(() => file.Update(n => n.Set("g1", "x", Now)));

        Assert.Contains(file.Path, error.Message);
        Assert.Equal("{ não é json", File.ReadAllText(file.Path));
    }

    [Theory]
    [InlineData("{\"version\":2,\"games\":{}}", "versão 2")]
    [InlineData("{\"version\":1,\"games\":{\"g1\":{\"text\":\"\",\"updatedAt\":\"2026-11-09T20:00:00-03:00\"}}}", "sem texto")]
    [InlineData("{\"version\":1,\"games\":{\"g1\":null}}", "sem texto")]
    [InlineData("null", "vazio")]
    [InlineData("{\"version\":1,\"games\":{\" \":{\"text\":\"x\",\"updatedAt\":\"2026-11-09T20:00:00-03:00\"}}}", "sem id")]
    public void Arquivo_fora_do_formato_diz_o_que_esta_errado(string json, string part)
    {
        var error = Assert.Throws<LibraryException>(() => GameNotesFile.Parse(json, "notas.json"));

        Assert.Contains(part, error.Message);
        Assert.Contains("notas.json", error.Message);
    }
}
