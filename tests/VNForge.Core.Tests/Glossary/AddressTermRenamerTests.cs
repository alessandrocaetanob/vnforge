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
    private static readonly AddressTerm MisakiChamaProfessor = new("美咲", "先生", "田中先生", "professor Tanaka");

    private static Dictionary<string, string> Renomear(string de, string para) => new() { [de] = para };

    [Fact]
    public void Quem_chama_e_quem_e_chamado_seguem_o_novo_nome()
    {
        var antes = new[] { KentaChamaMisaki, MisakiChamaKenta, MisakiChamaProfessor };

        var depois = AddressTermRenamer.Apply(antes, Renomear("美咲", "みさき"));

        Assert.Equal(
        [
            new AddressTerm("健太", "みさき", "美咲ちゃん", "Misaki-chan"),
            new AddressTerm("みさき", "健太", "先輩", "senpai"),
            new AddressTerm("みさき", "先生", "田中先生", "professor Tanaka"),
        ], depois);
    }

    [Fact]
    public void O_tratamento_em_si_nao_muda_so_o_par()
    {
        var depois = AddressTermRenamer.Apply([KentaChamaMisaki], Renomear("健太", "ケンタ"));

        var par = Assert.Single(depois);
        Assert.Equal(("ケンタ", "美咲", "美咲ちゃん", "Misaki-chan"), (par.From, par.To, par.Source, par.Target));
    }

    [Fact]
    public void Pares_de_outros_personagens_ficam_como_estao()
    {
        var antes = new[] { KentaChamaMisaki, MisakiChamaProfessor };

        var depois = AddressTermRenamer.Apply(antes, Renomear("凛", "りん"));

        Assert.Equal(antes, depois);
    }

    [Fact]
    public void Nome_com_espacos_e_largura_diferente_casa_como_no_livro()
    {
        var antes = new[] { new AddressTerm(" 学生Ｃ ", "美咲", "あいつ", "ele") };

        var depois = AddressTermRenamer.Apply(antes, Renomear("学生C", "学生D"));

        Assert.Equal("学生D", Assert.Single(depois).From);
    }

    [Fact]
    public void Novo_nome_com_espacos_nas_pontas_e_aparado()
    {
        var depois = AddressTermRenamer.Apply([KentaChamaMisaki], Renomear("美咲", "  みさき "));

        Assert.Equal("みさき", Assert.Single(depois).To);
    }

    [Fact]
    public void Novo_nome_que_ja_tem_o_mesmo_par_fica_com_o_que_ja_existia()
    {
        var existente = new AddressTerm("健太", "みさき", "みさきさん", "Misaki-san");
        var antes = new[] { KentaChamaMisaki, existente, MisakiChamaKenta };

        var depois = AddressTermRenamer.Apply(antes, Renomear("美咲", "みさき"));

        Assert.Equal([existente, new AddressTerm("みさき", "健太", "先輩", "senpai")], depois);
    }

    [Fact]
    public void Novo_nome_que_ja_tem_o_mesmo_par_vale_tambem_quando_o_existente_vem_depois()
    {
        var existente = new AddressTerm("みさき", "健太", "健太くん", "Kenta-kun");

        var depois = AddressTermRenamer.Apply([MisakiChamaKenta, existente], Renomear("美咲", "みさき"));

        Assert.Equal([existente], depois);
    }

    [Fact]
    public void Par_que_viraria_o_mesmo_falante_nos_dois_lados_some()
    {
        var antes = new[] { KentaChamaMisaki, MisakiChamaKenta };

        var depois = AddressTermRenamer.Apply(antes, Renomear("美咲", "健太"));

        Assert.Empty(depois);
    }

    [Fact]
    public void Dois_personagens_que_trocam_de_nome_trocam_os_pares_sem_se_misturar()
    {
        var renomes = new Dictionary<string, string> { ["健太"] = "美咲", ["美咲"] = "健太" };

        var depois = AddressTermRenamer.Apply([KentaChamaMisaki, MisakiChamaKenta], renomes);

        Assert.Equal(
        [
            new AddressTerm("美咲", "健太", "美咲ちゃん", "Misaki-chan"),
            new AddressTerm("健太", "美咲", "先輩", "senpai"),
        ], depois);
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

    [Fact]
    public void ApplyEach_devolve_uma_posicao_por_par_com_nulo_onde_o_par_some()
    {
        var existente = new AddressTerm("健太", "みさき", "みさきさん", "Misaki-san");
        var outro = new AddressTerm("凛", "健太", "先輩", "senpai");
        var antes = new[] { KentaChamaMisaki, existente, outro };

        var depois = AddressTermRenamer.ApplyEach(antes, Renomear("美咲", "みさき"));

        Assert.Equal(3, depois.Count);
        Assert.Null(depois[0]);
        Assert.Same(existente, depois[1]);
        Assert.Same(outro, depois[2]);
    }

    [Fact]
    public void Renamer_monta_o_indice_uma_vez_e_troca_so_os_nomes_citados()
    {
        var troca = AddressTermRenamer.Renamer(Renomear("美咲", "みさき"));

        Assert.Equal("みさき", troca("美咲"));
        Assert.Equal("凛", troca("凛"));
    }

    [Fact]
    public void Projeto_sintetico_a_ficha_acha_o_par_pelo_novo_nome_e_nao_pelo_antigo()
    {
        var folder = NovoProjeto();
        folder.SaveCharacters([new CharacterEntry("美咲", "Misaki"), new CharacterEntry("健太", "Kenta")]);
        folder.SaveAddressTerms([KentaChamaMisaki, MisakiChamaKenta]);

        var renomeados = AddressTermRenamer.Apply(folder.LoadAddressTerms(), Renomear("美咲", "みさき"));
        folder.SaveAddressTerms([.. renomeados]);
        var livro = new AddressBook(folder.LoadAddressTerms());

        Assert.Equal("Misaki-chan", livro.Find("健太", "みさき")?.Target);
        Assert.Equal("senpai", livro.Find("みさき", "健太")?.Target);
        Assert.Null(livro.Find("健太", "美咲"));
        Assert.Empty(livro.From("美咲"));
        Assert.Single(livro.From("みさき"));
    }
}
