using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Glossary;

public class GlossaryFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-glossary-" + Guid.NewGuid().ToString("N"));

    public GlossaryFilesTests() => Directory.CreateDirectory(_root);

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

    private static IReadOnlyList<GlossaryTerm> Termos() =>
    [
        new("先輩", "veterano"),
        new("生徒会", "conselho estudantil") { Notes = "sempre minúsculo" },
        new("aspas \" e barra \\", "x\ty"),
    ];

    private static IReadOnlyList<CharacterEntry> Personagens() =>
    [
        new("美咲", "Misaki", CharacterGender.Female, "animada, fala informal"),
        new("先生", "Professor", CharacterGender.Male, null),
        new("？？？", "???", CharacterGender.Unknown, string.Empty),
        new("ナレーション", "Narrador", CharacterGender.Other, "neutro"),
    ];

    [Fact]
    public void Glossario_ida_e_volta_sem_perda()
    {
        var json = GlossarySerializer.Serialize(Termos());

        var lido = GlossarySerializer.Parse(json, "glossary.json");

        Assert.Equal(Termos(), lido);
        Assert.Equal(json, GlossarySerializer.Serialize(lido));
    }

    [Fact]
    public void Personagens_ida_e_volta_sem_perda()
    {
        var json = CharacterSerializer.Serialize(Personagens());

        var lido = CharacterSerializer.Parse(json, "characters.json");

        Assert.Equal(Personagens(), lido);
        Assert.Equal(json, CharacterSerializer.Serialize(lido));
    }

    [Fact]
    public void Gravacao_tem_envelope_uma_entrada_por_linha_e_japones_literal()
    {
        var json = GlossarySerializer.Serialize([new("先輩", "veterano"), new("生徒会", "conselho")]);

        var linhas = json.Split('\n');
        Assert.Equal("{", linhas[0]);
        Assert.Equal("  \"version\": 1,", linhas[1]);
        Assert.Equal("  \"entries\": [", linhas[2]);
        Assert.Equal("    {\"source\":\"先輩\",\"target\":\"veterano\"},", linhas[3]);
        Assert.Equal("    {\"source\":\"生徒会\",\"target\":\"conselho\"}", linhas[4]);
        Assert.DoesNotContain('\r', json);
        Assert.EndsWith("}\n", json);
    }

    [Fact]
    public void Lista_vazia_gera_entries_vazio()
    {
        Assert.Equal("{\n  \"version\": 1,\n  \"entries\": []\n}\n", GlossarySerializer.Serialize([]));
        Assert.Empty(GlossarySerializer.Parse(GlossarySerializer.Serialize([]), "g.json"));
    }

    [Fact]
    public void Personagem_sem_genero_nem_tom_le_como_desconhecido_sem_tom()
    {
        var json = """{ "version": 1, "entries": [ { "source": "美咲", "target": "Misaki" } ] }""";

        var personagem = Assert.Single(CharacterSerializer.Parse(json, "characters.json"));

        Assert.Equal(CharacterGender.Unknown, personagem.Gender);
        Assert.Null(personagem.Tone);
    }

    [Theory]
    [InlineData("""{ "version": 1 }""", "entries")]
    [InlineData("""{ "entries": [] }""", "version")]
    [InlineData("""{ "version": 2, "entries": [] }""", "mais nova")]
    [InlineData("""{ "version": 1, "entries": [ { "source": "a", "target": "b", "extra": 1 } ] }""", "extra")]
    [InlineData("""{ "version": 1, "entries": [ { "source": "a" } ] }""", "target")]
    [InlineData("""{ "version": 1, "entries": [ { "source": " ", "target": "b" } ] }""", "source")]
    [InlineData("""{ "version": 1, "entries": [ { "source": "a", "target": "b" }, { "source": "a", "target": "c" } ] }""", "aparece mais de uma vez")]
    [InlineData("""{ "version": 1, "entries": [ null ] }""", "entries[0]")]
    [InlineData("{ não é json", "malformado")]
    public void Glossario_invalido_e_recusado_com_o_motivo(string json, string trecho)
    {
        var ex = Assert.Throws<GlossaryFileException>(() => GlossarySerializer.Parse(json, "glossary.json"));

        Assert.Contains(trecho, ex.Reason);
        Assert.Equal("glossary.json", ex.FilePath);
    }

    [Theory]
    [InlineData("""{ "version": 1, "entries": [ { "source": "a", "target": "b", "gender": "robot" } ] }""", "gender")]
    [InlineData("""{ "version": 1, "entries": [ { "source": "a", "target": "b", "gender": 1 } ] }""", "gender")]
    [InlineData("""{ "version": 1, "entries": [ { "source": "a", "target": "b" }, { "source": "a", "target": "c" } ] }""", "aparece mais de uma vez")]
    public void Personagens_invalidos_sao_recusados_com_o_motivo(string json, string trecho)
    {
        var ex = Assert.Throws<GlossaryFileException>(() => CharacterSerializer.Parse(json, "characters.json"));

        Assert.Contains(trecho, ex.Reason);
    }

    [Fact]
    public void Serialize_recusa_origem_vazia_repetida_ou_so_diferente_por_espacos_e_entrada_nula()
    {
        Assert.Throws<GlossaryFileException>(() => GlossarySerializer.Serialize([new(" ", "x")]));
        Assert.Throws<GlossaryFileException>(() => GlossarySerializer.Serialize([new("a", "x"), new("a", "y")]));
        Assert.Throws<GlossaryFileException>(() => CharacterSerializer.Serialize([new("美咲", "Misaki"), new("美咲 ", "Misaki")]));
        Assert.Throws<GlossaryFileException>(() => GlossarySerializer.Serialize([null!]));
    }

    [Fact]
    public void Serialize_recusa_surrogate_solitario()
    {
        Assert.Throws<GlossaryFileException>(() => GlossarySerializer.Serialize([new("a\uD800", "x")]));
    }

    [Fact]
    public void Mensagem_de_erro_do_json_vem_em_portugues()
    {
        var ex = Assert.Throws<GlossaryFileException>(() =>
            CharacterSerializer.Parse("""{ "version": 1, "entries": [ { "source": "a", "target": "b", "gender": "robot" } ] }""", "c.json"));

        Assert.Contains("valor inválido", ex.Reason);
        Assert.DoesNotContain("could not", ex.Reason);
    }

    [Fact]
    public void Speaker_extraido_encontra_a_entrada_certa()
    {
        var livro = new CharacterBook(Personagens());

        Assert.Equal("Misaki", livro.Find("美咲")?.Target);
        Assert.Equal("Professor", livro.Find("先生")?.Target);
        Assert.Equal("???", livro.Find("？？？")?.Target);
    }

    [Fact]
    public void Speaker_com_espacos_nas_pontas_ainda_encontra_a_entrada()
    {
        var livro = new CharacterBook(Personagens());

        Assert.Equal("Misaki", livro.Find("　美咲 ")?.Target);
    }

    [Fact]
    public void Speaker_ausente_ou_desconhecido_nao_encontra_nada()
    {
        var livro = new CharacterBook(Personagens());

        Assert.Null(livro.Find(null));
        Assert.Null(livro.Find(string.Empty));
        Assert.Null(livro.Find("見知らぬ人"));
    }

    [Fact]
    public void Speaker_de_um_TextUnit_encontra_o_personagem()
    {
        var unidade = new TextUnit("yst00123:000001", TextUnitKind.Dialogue, "先生", "「おはよう」", null, TranslationStatus.Untranslated);
        var livro = new CharacterBook(Personagens());

        Assert.Equal("Professor", livro.Find(unidade.Speaker)?.Target);
    }

    [Fact]
    public void ProjectFolder_grava_e_le_os_dois_arquivos_na_raiz()
    {
        var pasta = NovoProjeto();

        pasta.SaveGlossary(Termos());
        pasta.SaveCharacters(Personagens());

        Assert.True(File.Exists(Path.Combine(_root, "glossary.json")));
        Assert.True(File.Exists(Path.Combine(_root, "characters.json")));
        Assert.Equal(Termos(), pasta.LoadGlossary());
        Assert.Equal(Personagens(), pasta.LoadCharacters());
    }

    [Fact]
    public void ProjectFolder_sem_os_arquivos_devolve_listas_vazias()
    {
        var pasta = NovoProjeto();

        Assert.Empty(pasta.LoadGlossary());
        Assert.Empty(pasta.LoadCharacters());
    }

    [Fact]
    public void ProjectFolder_recusa_arquivo_fora_de_utf8()
    {
        var pasta = NovoProjeto();
        File.WriteAllBytes(pasta.GlossaryPath, [0x7B, 0xFF, 0xFE, 0x7D]);

        Assert.Throws<GlossaryFileException>(pasta.LoadGlossary);
    }
}
