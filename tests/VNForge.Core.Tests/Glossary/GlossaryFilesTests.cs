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
        new("美咲", "Misaki", CharacterGender.Female, "animada, fala informal", "rose"),
        new("先生", "Professor", CharacterGender.Male, null, "green"),
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
    public void Cor_do_personagem_vai_e_volta_e_some_do_arquivo_quando_nao_definida()
    {
        var json = CharacterSerializer.Serialize([new("美咲", "Misaki", Color: "rose"), new("先生", "Professor")]);

        Assert.Contains("\"color\":\"rose\"", json);
        Assert.Equal(1, json.Split("\"color\"").Length - 1);
        var lido = CharacterSerializer.Parse(json, "characters.json");
        Assert.Equal("rose", lido[0].Color);
        Assert.Null(lido[1].Color);
    }

    [Theory]
    [InlineData("""{ "version": 1 }""", "entries")]
    [InlineData("""{ "version": 1, "entries": [ null ] }""", "entries[0]")]
    public void Glossario_invalido_e_recusado_com_o_motivo(string json, string trecho)
    {
        var ex = Assert.Throws<GlossaryFileException>(() => GlossarySerializer.Parse(json, "glossary.json"));

        Assert.Contains(trecho, ex.Reason);
        Assert.Equal("glossary.json", ex.FilePath);
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
    public void ProjectFolder_recusa_arquivo_fora_de_utf8()
    {
        var pasta = NovoProjeto();
        File.WriteAllBytes(pasta.GlossaryPath, [0x7B, 0xFF, 0xFE, 0x7D]);

        Assert.Throws<GlossaryFileException>(pasta.LoadGlossary);
    }
}
