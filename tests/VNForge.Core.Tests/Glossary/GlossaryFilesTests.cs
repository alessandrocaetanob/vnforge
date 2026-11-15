using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Glossary;

[Trait("Risco", "R2")]
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
}
