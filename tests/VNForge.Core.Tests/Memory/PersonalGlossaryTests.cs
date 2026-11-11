using VNForge.Core.Engines;
using VNForge.Core.Glossary;
using VNForge.Core.Memory;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Memory;

/// <summary>O glossário pessoal (#537): termos que valem em toda VN, guardados fora dos projetos e usados como ponto de partida num projeto novo.</summary>
public sealed class PersonalGlossaryTests : IDisposable
{
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    private string Config => Path.Combine(temp.Path, "config");

    private PersonalGlossary Pessoal() => new(Config);

    private ProjectFolder NovoProjeto(string pasta, params GlossaryTerm[] termos)
    {
        var projeto = ProjectFolder.Create(Path.Combine(temp.Path, pasta), new ProjectConfig(
            pasta,
            new GameConfig("D:/Jogos/Qualquer", "Qualquer.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        if (termos.Length > 0)
        {
            projeto.SaveGlossary(termos);
        }

        return projeto;
    }

    [Fact]
    public void Guardar_de_dois_projetos_junta_as_origens_e_fica_com_a_traducao_mais_nova()
    {
        var a = NovoProjeto("a");
        var b = NovoProjeto("b");
        var pessoal = Pessoal();
        pessoal.Keep(a, new GlossaryTerm("先輩", "veterano"));

        pessoal.Keep(b, new GlossaryTerm("先輩", "senpai"));
        pessoal.Keep(b, new GlossaryTerm("先輩", "senpai"));

        var termo = Assert.Single(pessoal.Load());
        Assert.Equal("senpai", termo.Target);
        Assert.Equal(2, termo.ProjectCount);
    }

    [Fact]
    public void Remove_tira_o_termo_e_diz_se_ele_existia()
    {
        var pessoal = Pessoal();
        pessoal.Upsert(new PersonalTerm("先輩", "senpai"));

        Assert.True(pessoal.Remove("先輩"));
        Assert.False(pessoal.Remove("先輩"));
        Assert.Empty(pessoal.Load());
    }

    [Fact]
    public void O_estado_no_projeto_distingue_ausente_igual_e_diferente()
    {
        var projeto = new[] { new GlossaryTerm("先輩", "senpai"), new GlossaryTerm("遅刻", "atraso") };

        Assert.Equal(PersonalTermState.InProject, PersonalGlossary.StateIn(new PersonalTerm("先輩", "senpai"), projeto));
        Assert.Equal(PersonalTermState.Differs, PersonalGlossary.StateIn(new PersonalTerm("遅刻", "chegar tarde"), projeto));
        Assert.Equal(PersonalTermState.NotInProject, PersonalGlossary.StateIn(new PersonalTerm("すごい", "incrível"), projeto));
    }

    [Fact]
    public void Os_termos_a_guardar_sao_os_do_projeto_que_faltam_ou_diferem()
    {
        var pessoal = Pessoal();
        pessoal.Upsert(new PersonalTerm("先輩", "senpai"));
        pessoal.Upsert(new PersonalTerm("遅刻", "chegar tarde"));
        var projeto = new[]
        {
            new GlossaryTerm("先輩", "senpai"),
            new GlossaryTerm("遅刻", "atraso"),
            new GlossaryTerm("傘", "guarda-chuva"),
        };

        Assert.Equal(["遅刻", "傘"], pessoal.WorthKeeping(projeto).Select(t => t.Source));
    }

    [Fact]
    public void Usar_no_projeto_copia_o_termo_e_nao_troca_a_escolha_do_projeto_sem_pedir()
    {
        var projeto = NovoProjeto("a", new GlossaryTerm("遅刻", "atraso"));
        var pessoal = Pessoal();
        pessoal.Upsert(new PersonalTerm("すごい", "incrível", "sem gíria"));
        pessoal.Upsert(new PersonalTerm("遅刻", "chegar tarde"));

        Assert.Equal(PersonalTermState.InProject, pessoal.UseInProject(projeto, "すごい"));
        Assert.Equal(PersonalTermState.Differs, pessoal.UseInProject(projeto, "遅刻"));

        Assert.Equal(
            [new GlossaryTerm("遅刻", "atraso"), new GlossaryTerm("すごい", "incrível", "sem gíria")],
            projeto.LoadGlossary());

        Assert.Equal(PersonalTermState.InProject, pessoal.UseInProject(projeto, "遅刻", replace: true));
        Assert.Equal("chegar tarde", projeto.LoadGlossary().Single(t => t.Source == "遅刻").Target);
        Assert.Throws<KeyNotFoundException>(() => pessoal.UseInProject(projeto, "inexistente"));
    }

    [Fact]
    public void Projeto_novo_comeca_com_os_termos_marcados_e_o_que_ja_tem_fica_como_esta()
    {
        var pessoal = Pessoal();
        pessoal.Upsert(new PersonalTerm("先輩", "senpai"));
        pessoal.Upsert(new PersonalTerm("ふふっ", "hehe", Seed: false));
        pessoal.Upsert(new PersonalTerm("遅刻", "chegar tarde"));
        var projeto = NovoProjeto("novo", new GlossaryTerm("遅刻", "atraso"));

        var entraram = pessoal.SeedProject(projeto);

        Assert.Equal(1, entraram);
        Assert.Equal(["遅刻", "先輩"], projeto.LoadGlossary().Select(t => t.Source));
        Assert.Equal("atraso", projeto.LoadGlossary()[0].Target);
        Assert.Equal(0, pessoal.SeedProject(projeto));
    }

    [Fact]
    public void O_projeto_criado_pelo_inicializador_ja_nasce_com_os_termos()
    {
        var jogo = Path.Combine(temp.Path, "jogo");
        Directory.CreateDirectory(jogo);
        File.WriteAllText(Path.Combine(jogo, "Jogo.exe"), "MZ");
        var pessoal = Pessoal();
        pessoal.Upsert(new PersonalTerm("先輩", "senpai"));
        pessoal.Upsert(new PersonalTerm("お兄ちゃん", "mano"));
        var engine = new DetectedEngine(EngineDetection.Matched("parece fake", new Dictionary<string, string>()));

        var resultado = ProjectInitializer.Initialize(Path.Combine(temp.Path, "projeto"), jogo, [engine], personalGlossary: pessoal);

        Assert.Equal(2, resultado.SeededTerms);
        Assert.Equal(["先輩", "お兄ちゃん"], resultado.Folder.LoadGlossary().Select(t => t.Source));
    }

    [Fact]
    public void Glossario_pessoal_ilegivel_nao_impede_de_criar_o_projeto()
    {
        var jogo = Path.Combine(temp.Path, "jogo");
        Directory.CreateDirectory(jogo);
        File.WriteAllText(Path.Combine(jogo, "Jogo.exe"), "MZ");
        Directory.CreateDirectory(Config);
        File.WriteAllText(Path.Combine(Config, PersonalGlossary.FileName), "lixo");
        var engine = new DetectedEngine(EngineDetection.Matched("parece fake", new Dictionary<string, string>()));

        var resultado = ProjectInitializer.Initialize(Path.Combine(temp.Path, "projeto"), jogo, [engine], personalGlossary: Pessoal());

        Assert.Equal(0, resultado.SeededTerms);
        Assert.Empty(resultado.Folder.LoadGlossary());
    }

    [Fact]
    public void Arquivo_ilegivel_e_recusado_sem_ser_sobrescrito()
    {
        Directory.CreateDirectory(Config);
        var caminho = Path.Combine(Config, PersonalGlossary.FileName);
        File.WriteAllText(caminho, "{\"version\": 9, \"entries\": []}");
        var pessoal = Pessoal();

        var erro = Assert.Throws<MemoryFileException>(() => pessoal.Upsert(new PersonalTerm("先輩", "senpai")));

        Assert.Contains("versão 9", erro.Message);
        Assert.Contains("\"version\": 9", File.ReadAllText(caminho));
    }

    private sealed class DetectedEngine(EngineDetection detection) : IEngine
    {
        public string Id => "fake";

        public EngineDetection Detect(string gameDir) => detection;

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];
    }

    private void EscreverArquivo(string json)
    {
        Directory.CreateDirectory(Config);
        File.WriteAllText(Path.Combine(Config, PersonalGlossary.FileName), json);
    }

    [Fact]
    public void Termo_sem_original_ou_sem_traducao_e_erro_do_arquivo_e_nao_excecao_solta()
    {
        EscreverArquivo("""{"version":1,"entries":[{"target":"mano"}]}""");
        var pessoal = Pessoal();

        var erro = Assert.Throws<MemoryFileException>(() => pessoal.Load());
        Assert.Contains("termo 1", erro.Message);
        Assert.Throws<MemoryFileException>(() => pessoal.WorthKeeping([new GlossaryTerm("先輩", "senpai")]));
        Assert.Throws<MemoryFileException>(() => pessoal.SeedProject(NovoProjeto("p")));

        EscreverArquivo("""{"version":1,"entries":[{"source":"先輩"}]}""");
        Assert.Throws<MemoryFileException>(() => Pessoal().Load());
    }

    [Fact]
    public void Original_repetido_no_arquivo_e_erro_e_o_arquivo_fica_como_esta()
    {
        var json = """{"version":1,"entries":[{"source":"先輩","target":"a"},{"source":"先輩 ","target":"b"}]}""";
        EscreverArquivo(json);
        var pessoal = Pessoal();

        var erro = Assert.Throws<MemoryFileException>(() => pessoal.Upsert(new PersonalTerm("傘", "guarda-chuva")));

        Assert.Contains("先輩", erro.Message);
        Assert.Equal(json, File.ReadAllText(pessoal.Path));
    }
}
