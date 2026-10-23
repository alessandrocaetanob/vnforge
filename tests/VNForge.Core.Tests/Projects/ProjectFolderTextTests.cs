using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public class ProjectFolderTextTests
{
    private const string ScriptId2 = "yst00002";
    private const string ScriptId1 = "yst00001";
    private const string ScriptId123 = "yst00123";
    private static readonly string GamePath = Path.Combine(Path.GetTempPath(), "Jogos", "MinhaVN");

    private static ProjectFolder NovoProjeto(string raiz) => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig(GamePath, "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static IReadOnlyList<TextUnit> Unidades(string key, int quantidade = 2) =>
        Enumerable.Range(1, quantidade)
            .Select(i => new TextUnit($"{key}:{i:D6}", TextUnitKind.Dialogue, "美咲", $"今日は{i}\\p", null, TranslationStatus.Untranslated))
            .ToList();

    [Fact]
    public void SaveText_grava_em_text_chave_json_em_utf8_sem_bom_e_lf()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);

        projeto.SaveText(ScriptId123, Unidades(ScriptId123));

        var caminho = Path.Combine(temp.Path, "text", "yst00123.json");
        Assert.Equal(caminho, projeto.TextPath(ScriptId123));
        var bytes = File.ReadAllBytes(caminho);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.Contains("今日は1", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void SaveText_duas_vezes_gera_arquivos_identicos()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);

        projeto.SaveText(ScriptId123, Unidades(ScriptId123));
        var primeiro = File.ReadAllBytes(projeto.TextPath(ScriptId123));
        projeto.SaveText(ScriptId123, Unidades(ScriptId123));

        Assert.Equal(primeiro, File.ReadAllBytes(projeto.TextPath(ScriptId123)));
        Assert.Empty(Directory.GetFiles(projeto.TextDirectory, "*.tmp"));
    }

    [Fact]
    public void LoadText_devolve_o_que_foi_gravado_e_cria_a_pasta_text_se_faltar()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        Directory.Delete(projeto.TextDirectory);

        projeto.SaveText(ScriptId123, Unidades(ScriptId123, 3));
        var lidas = projeto.LoadText(ScriptId123);

        Assert.Equal(Unidades(ScriptId123, 3).Select(TextUnitJson.Serialize), lidas.Select(TextUnitJson.Serialize));
    }

    [Fact]
    public void LoadText_aceita_arquivo_com_bom_e_crlf()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        var json = TextFileSerializer.Serialize(Unidades(ScriptId123)).Replace("\n", "\r\n");
        File.WriteAllText(projeto.TextPath(ScriptId123), json, new System.Text.UTF8Encoding(true));

        Assert.Equal(2, projeto.LoadText(ScriptId123).Count);
    }

    [Fact]
    public void LoadText_de_arquivo_ausente_ou_fora_de_utf8_ou_invalido_cita_o_caminho()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        var caminho = projeto.TextPath(ScriptId123);

        var ausente = Assert.Throws<TextFileException>(() => projeto.LoadText(ScriptId123));
        File.WriteAllBytes(caminho, [0x7B, 0xFF, 0x7D]);
        var encoding = Assert.Throws<TextFileException>(() => projeto.LoadText(ScriptId123));
        File.WriteAllText(caminho, "{ nope");
        var invalido = Assert.Throws<TextFileException>(() => projeto.LoadText(ScriptId123));

        foreach (var ex in new[] { ausente, encoding, invalido })
        {
            Assert.Contains(caminho, ex.Message);
        }

        Assert.Contains("UTF-8", encoding.Message);
    }

    [Fact]
    public void LoadText_recusa_ids_de_outro_script()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        File.WriteAllText(projeto.TextPath(ScriptId123), TextFileSerializer.Serialize(Unidades("yst00999")));

        var ex = Assert.Throws<TextFileException>(() => projeto.LoadText(ScriptId123));

        Assert.Contains("yst00999:000001", ex.Message);
    }

    [Fact]
    public void SaveText_recusa_ids_de_outro_script_sem_gravar()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);

        var ex = Assert.Throws<TextFileException>(() => projeto.SaveText(ScriptId123, Unidades("yst00999")));

        Assert.Contains("yst00999:000001", ex.Message);
        Assert.False(File.Exists(projeto.TextPath(ScriptId123)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData("a:b")]
    [InlineData("a.")]
    [InlineData("Yst00123")]
    public void Chave_invalida_e_recusada(string key)
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);

        Assert.Throws<ArgumentException>(() => projeto.TextPath(key));
        Assert.Throws<ArgumentException>(() => projeto.SaveText(key, []));
        Assert.Throws<ArgumentException>(() => projeto.LoadText(key));
    }

    [Fact]
    public void LoadAllText_le_todos_os_scripts_em_ordem_de_chave()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveText(ScriptId2, Unidades(ScriptId2));
        projeto.SaveText(ScriptId1, Unidades(ScriptId1, 1));

        var todos = projeto.LoadAllText();

        Assert.Equal([ScriptId1, ScriptId2], todos.Keys);
        Assert.Single(todos[ScriptId1]);
        Assert.Equal(2, todos[ScriptId2].Count);
    }

    [Fact]
    public void LoadAllText_ignora_outros_arquivos_e_aceita_pasta_vazia()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        File.WriteAllText(Path.Combine(projeto.TextDirectory, "leia-me.txt"), "oi");

        Assert.Empty(projeto.LoadAllText());
    }

    [Fact]
    public void LoadTextFiles_falha_quando_dois_arquivos_tem_a_mesma_chave()
    {
        using var temp = new TempFolder();
        var json = TextFileSerializer.Serialize(Unidades(ScriptId123));
        var a = Path.Combine(temp.Path, "a", "yst00123.json");
        var b = Path.Combine(temp.Path, "b", "YST00123.JSON");
        Directory.CreateDirectory(Path.GetDirectoryName(a)!);
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        File.WriteAllText(a, json);
        File.WriteAllText(b, json);

        var ex = Assert.Throws<TextFileException>(() => ProjectFolder.LoadTextFiles([a, b]));

        Assert.Contains("'yst00123'", ex.Message);
        Assert.Contains("mesma chave", ex.Message);
        Assert.Contains(a, ex.Message);
        Assert.Contains(b, ex.Message);
    }
}
