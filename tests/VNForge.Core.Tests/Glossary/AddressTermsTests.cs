using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Spelling;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Glossary;

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
    public void O_arquivo_tem_um_par_por_linha_e_o_japones_sem_escape()
    {
        var json = AddressTermSerializer.Serialize([new AddressTerm("健太", "美咲", "美咲ちゃん", "Misaki-chan")]);

        Assert.Equal(
            "{\n  \"version\": 1,\n  \"entries\": [\n    {\"from\":\"健太\",\"to\":\"美咲\",\"source\":\"美咲ちゃん\",\"target\":\"Misaki-chan\"}\n  ]\n}\n",
            json);
    }

    [Fact]
    public void Lista_vazia_vira_um_arquivo_valido_e_volta_vazia()
    {
        var json = AddressTermSerializer.Serialize([]);

        Assert.Equal("{\n  \"version\": 1,\n  \"entries\": []\n}\n", json);
        Assert.Empty(AddressTermSerializer.Parse(json, "address-terms.json"));
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
    public void Projeto_antigo_sem_o_arquivo_abre_sem_tratamentos_e_sem_mexer_nos_outros_arquivos()
    {
        var projeto = NovoProjeto();
        projeto.SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female)]);
        projeto.SaveGlossary([new GlossaryTerm("先輩", "veterano")]);
        var personagens = File.ReadAllText(projeto.CharactersPath);
        var glossario = File.ReadAllText(projeto.GlossaryPath);

        Assert.False(File.Exists(projeto.AddressTermsPath));
        Assert.Empty(projeto.LoadAddressTerms());
        Assert.Single(projeto.LoadCharacters());
        Assert.Equal(personagens, File.ReadAllText(projeto.CharactersPath));
        Assert.Equal(glossario, File.ReadAllText(projeto.GlossaryPath));
        Assert.False(File.Exists(projeto.AddressTermsPath));
    }

    [Fact]
    public void O_par_repetido_e_recusado_mesmo_com_espacos_e_largura_diferentes()
    {
        var repetido = new AddressTerm[] { new("健太", "学生C", "あいつ", "ele"), new(" 健太 ", "学生Ｃ", "きみ", "você") };

        var ex = Assert.Throws<GlossaryFileException>(() => AddressTermSerializer.Serialize(repetido));

        Assert.Contains("mais de uma vez", ex.Reason);
    }

    [Fact]
    public void O_mesmo_falante_com_destinos_diferentes_e_aceito()
    {
        var json = AddressTermSerializer.Serialize([new AddressTerm("健太", "美咲", "美咲", "Misaki"), new AddressTerm("健太", "凛", "凛", "Rin")]);

        Assert.Equal(2, AddressTermSerializer.Parse(json, "x").Count);
    }

    [Theory]
    [InlineData("", "美咲")]
    [InlineData("健太", "  ")]
    public void Quem_fala_e_com_quem_fala_nao_podem_ser_vazios(string from, string to)
    {
        Assert.Throws<GlossaryFileException>(() => AddressTermSerializer.Serialize([new AddressTerm(from, to, "a", "b")]));
    }

    [Fact]
    public void Arquivo_com_par_repetido_ou_quem_fala_vazio_e_recusado_na_leitura()
    {
        const string repetido = """{"version":1,"entries":[{"from":"a","to":"b","source":"c","target":"d"},{"from":" a","to":"b ","source":"e","target":"f"}]}""";
        const string semFalante = """{"version":1,"entries":[{"from":"","to":"b","source":"c","target":"d"}]}""";

        Assert.Contains("mais de uma vez", Assert.Throws<GlossaryFileException>(() => AddressTermSerializer.Parse(repetido, "x")).Reason);
        Assert.Contains("quem fala", Assert.Throws<GlossaryFileException>(() => AddressTermSerializer.Parse(semFalante, "x")).Reason);
    }

    [Fact]
    public void Para_quem_so_consulta_o_arquivo_ilegivel_vira_lista_vazia_com_o_motivo()
    {
        var projeto = NovoProjeto();
        File.WriteAllText(projeto.AddressTermsPath, "{ quebrado");

        var termos = projeto.LoadAddressTermsOrEmpty(out var problema);

        Assert.Empty(termos);
        Assert.Contains("address-terms.json", problema);
        Assert.Throws<GlossaryFileException>(projeto.LoadAddressTerms);

        File.Delete(projeto.AddressTermsPath);
        Assert.Empty(projeto.LoadAddressTermsOrEmpty(out problema));
        Assert.Null(problema);
    }

    [Fact]
    public void O_corretor_aceita_o_tratamento_combinado_como_palavra_do_projeto()
    {
        var sem = ProjectDictionary.Create([], []);
        var com = ProjectDictionary.Create([], [], [new AddressTerm("健太", "美咲", "美咲ちゃん", "Zorgblat-chan")]);

        Assert.False(sem.Contains("Zorgblat-chan"));
        Assert.True(com.Contains("Zorgblat-chan"));
    }

    [Theory]
    [InlineData("""{"version":1,"entries":[{"from":"a","to":"b","source":"c"}]}""")]
    [InlineData("""{"version":1,"entries":[{"from":"a","to":"b","source":"c","target":"d","extra":1}]}""")]
    [InlineData("""{"version":2,"entries":[]}""")]
    [InlineData("""{"entries":[]}""")]
    [InlineData("não é json")]
    public void Arquivo_fora_do_formato_e_recusado_com_a_mensagem_do_arquivo(string json)
    {
        var ex = Assert.Throws<GlossaryFileException>(() => AddressTermSerializer.Parse(json, "address-terms.json"));

        Assert.Equal("address-terms.json", ex.FilePath);
    }

    [Fact]
    public void O_livro_acha_o_par_ignorando_espacos_e_largura_e_lista_os_do_falante()
    {
        var livro = new AddressBook([new("健太", "学生Ｃ", "あいつ", "ele"), new("健太", "美咲", "美咲ちゃん", "Misaki-chan"), new("美咲", "健太", "先輩", "senpai")]);

        Assert.Equal("ele", livro.Find(" 健太", "学生C")?.Target);
        Assert.Null(livro.Find("美咲", "学生C"));
        Assert.Null(livro.Find(null, "美咲"));
        Assert.Equal(["学生Ｃ", "美咲"], livro.From("健太").Select(t => t.To));
        Assert.Empty(livro.From(" "));
    }
}
