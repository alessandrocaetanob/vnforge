using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public class ProjectFolderTextTests
{
    private const string ScriptId123 = "yst00123";
    private static readonly string GamePath = Path.Combine(Path.GetTempPath(), "Jogos", "MinhaVN");

    private static ProjectFolder NovoProjeto(string raiz) => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig(GamePath, "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static List<TextUnit> Unidades(string key, int quantidade = 2) =>
        Enumerable.Range(1, quantidade)
            .Select(i => new TextUnit($"{key}:{i:D6}", TextUnitKind.Dialogue, "美咲", $"今日は{i}\\p", null, TranslationStatus.Untranslated))
            .ToList();

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
