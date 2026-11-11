using VNForge.Core.Glossary;
using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Glossary;

/// <summary>Renomear um personagem leva junto os tratamentos que o citam (#514).</summary>
public sealed class AddressTermRenamerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-address-rename-" + Guid.NewGuid().ToString("N"));

    public AddressTermRenamerTests() => Directory.CreateDirectory(_root);

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

    private static readonly AddressTerm KentaChamaMisaki = new("健太", "美咲", "美咲ちゃん", "Misaki-chan");
    private static readonly AddressTerm MisakiChamaKenta = new("美咲", "健太", "先輩", "senpai");

    private static Dictionary<string, string> Renomear(string de, string para) => new() { [de] = para };

    [Fact]
    public void Novo_nome_que_ja_tem_o_mesmo_par_fica_com_o_que_ja_existia()
    {
        var existente = new AddressTerm("健太", "みさき", "みさきさん", "Misaki-san");
        var antes = new[] { KentaChamaMisaki, existente, MisakiChamaKenta };

        var depois = AddressTermRenamer.Apply(antes, Renomear("美咲", "みさき"));

        Assert.Equal([existente, new AddressTerm("みさき", "健太", "先輩", "senpai")], depois);
    }

    [Fact]
    public void Sem_renomes_devolve_os_mesmos_pares()
    {
        var antes = new[] { KentaChamaMisaki };

        Assert.Same(antes, AddressTermRenamer.Apply(antes, new Dictionary<string, string>()));
    }

    [Fact]
    public void NameAfter_troca_o_nome_citado_e_deixa_os_outros()
    {
        var renomes = Renomear("美咲", "みさき");

        Assert.Equal("みさき", AddressTermRenamer.NameAfter(" 美咲", renomes));
        Assert.Equal("健太", AddressTermRenamer.NameAfter("健太", renomes));
    }
}
