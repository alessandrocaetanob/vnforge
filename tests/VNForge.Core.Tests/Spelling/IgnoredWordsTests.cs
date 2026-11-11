using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Spelling;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Spelling;

public sealed class IgnoredWordsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-ignored-" + Guid.NewGuid().ToString("N"));

    public IgnoredWordsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private ProjectFolder NovoProjeto() => ProjectFolder.Create(_root, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static string[] Wrong(string text, ProjectDictionary project) => [.. SpellChecker.Portuguese.Find(text, project).Select(span => span.Text)];

    [Fact]
    public void Lista_ida_e_volta_byte_a_byte_com_uma_palavra_por_linha()
    {
        var json = IgnoredWordsSerializer.Serialize([new IgnoredWord("Zorgblat"), new IgnoredWord("kyuun")]);

        Assert.Equal("{\n  \"version\": 1,\n  \"entries\": [\n    {\"word\":\"Zorgblat\"},\n    {\"word\":\"kyuun\"}\n  ]\n}\n", json);
        var lido = IgnoredWordsSerializer.Parse(json, "ignored-words.json");
        Assert.Equal([new IgnoredWord("Zorgblat"), new IgnoredWord("kyuun")], lido);
        Assert.Equal(json, IgnoredWordsSerializer.Serialize(lido));
    }

    [Fact]
    public void Palavra_vazia_ou_repetida_ignorando_maiusculas_e_recusada()
    {
        Assert.Throws<GlossaryFileException>(() => IgnoredWordsSerializer.Serialize([new IgnoredWord("  ")]));

        var ex = Assert.Throws<GlossaryFileException>(() => IgnoredWordsSerializer.Serialize([new IgnoredWord("Zorgblat"), new IgnoredWord(" zorgblat ")]));

        Assert.Contains("mais de uma vez", ex.Reason);
    }

    [Fact]
    public void Projeto_grava_a_palavra_e_le_de_volta_sem_repetir_a_mesma_palavra()
    {
        var projeto = NovoProjeto();

        Assert.True(projeto.AddIgnoredWord("Zorgblat"));
        Assert.False(projeto.AddIgnoredWord("zorgblat"));
        Assert.True(projeto.AddIgnoredWord("kyuun"));

        Assert.Equal([new IgnoredWord("Zorgblat"), new IgnoredWord("kyuun")], projeto.LoadIgnoredWords());
        var gravado = File.ReadAllBytes(projeto.IgnoredWordsPath);
        projeto.SaveIgnoredWords(projeto.LoadIgnoredWords());
        Assert.Equal(gravado, File.ReadAllBytes(projeto.IgnoredWordsPath));
    }

    [Fact]
    public void Palavra_em_branco_nao_e_gravada()
    {
        var projeto = NovoProjeto();

        Assert.False(projeto.AddIgnoredWord("  "));

        Assert.False(File.Exists(projeto.IgnoredWordsPath));
    }

    [Fact]
    public void Palavra_ignorada_com_hifen_vale_inteira_e_nao_libera_a_parte()
    {
        var sem = ProjectDictionary.Create([], []);
        var com = ProjectDictionary.Create([], [], ignoredWords: ["xorgbla-zzq"]);

        Assert.Equal(["xorgbla-zzq"], Wrong("Foi xorgbla-zzq mesmo.", sem));
        Assert.Empty(Wrong("Foi xorgbla-zzq mesmo.", com));
        Assert.Equal(["xorgbla"], Wrong("Foi xorgbla mesmo.", com));
    }

    [Fact]
    public void Dicionario_com_a_palavra_acrescentada_nao_muda_o_original()
    {
        var original = ProjectDictionary.Create([], []);

        var novo = original.WithIgnoredWord("Zorgblat");

        Assert.True(novo.Contains("zorgblat"));
        Assert.False(original.Contains("Zorgblat"));
    }
}
