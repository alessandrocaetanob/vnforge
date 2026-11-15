using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Spelling;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Glossary;

[Trait("Risco", "R3")]
public sealed class AddressTermsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-address-" + Guid.NewGuid().ToString("N"));

    public AddressTermsTests() => Directory.CreateDirectory(_root);

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

    private static IReadOnlyList<AddressTerm> Tratamentos() =>
    [
        new("健太", "美咲", "美咲ちゃん", "Misaki-chan"),
        new("美咲", "健太", "先輩", "senpai"),
        new("美咲", "先生", "田中先生", "professor \"Tanaka\""),
    ];

    [Fact]
    public void Tratamentos_ida_e_volta_byte_a_byte()
    {
        var json = AddressTermSerializer.Serialize(Tratamentos());

        var lido = AddressTermSerializer.Parse(json, "address-terms.json");

        Assert.Equal(Tratamentos(), lido);
        Assert.Equal(json, AddressTermSerializer.Serialize(lido));
    }

    [Fact]
    public void Projeto_salva_e_le_os_tratamentos_com_o_arquivo_gravado_byte_a_byte()
    {
        var projeto = NovoProjeto();

        projeto.SaveAddressTerms(Tratamentos());

        Assert.Equal(Tratamentos(), projeto.LoadAddressTerms());
        var gravado = File.ReadAllBytes(projeto.AddressTermsPath);
        projeto.SaveAddressTerms(projeto.LoadAddressTerms());
        Assert.Equal(gravado, File.ReadAllBytes(projeto.AddressTermsPath));
    }

    [Fact]
    public void Arquivo_com_par_repetido_ou_quem_fala_vazio_e_recusado_na_leitura()
    {
        const string repetido = """{"version":1,"entries":[{"from":"a","to":"b","source":"c","target":"d"},{"from":" a","to":"b ","source":"e","target":"f"}]}""";
        const string semFalante = """{"version":1,"entries":[{"from":"","to":"b","source":"c","target":"d"}]}""";

        Assert.Contains("mais de uma vez", Assert.Throws<GlossaryFileException>(() => AddressTermSerializer.Parse(repetido, "x")).Reason);
        Assert.Contains("quem fala", Assert.Throws<GlossaryFileException>(() => AddressTermSerializer.Parse(semFalante, "x")).Reason);
    }
}
