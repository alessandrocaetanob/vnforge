using System.Text.Json;
using VNForge.Core.Glossary;
using VNForge.Core.Memory;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

/// <summary><c>vnf memory</c> e <c>vnf glossary personal</c> (#537): a memória de tradução entre projetos e o glossário pessoal. O texto é inventado aqui.</summary>
public sealed class MemoryCommandTests : IDisposable
{
    private const string Frase = "「相沢、佐倉。二人そろって五分遅刻だ」";
    private const string JsonOption = "--json";

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private string PathOf(string name) => Path.Combine(_cli.Root, name);

    private ProjectFolder NovoProjeto(string pasta, string nome, params TextUnit[] linhas)
    {
        var projeto = ProjectFolder.Create(PathOf(pasta), new ProjectConfig(
            nome,
            new GameConfig(PathOf("jogo"), "Jogo.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        projeto.SaveText("yst00001", linhas);
        return projeto;
    }

    private static TextUnit Linha(int n, string original, string? traducao, TranslationStatus status = TranslationStatus.Approved) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, "美咲", original, traducao, status);

    private Task<RunResult> Run(string pasta, params string[] args) =>
        _cli.RunAsync([.. args, "--project", PathOf(pasta)], TestContext.Current.CancellationToken);

    [Fact]
    public async Task Uma_linha_aprovada_num_projeto_aparece_como_sugestao_em_outro()
    {
        NovoProjeto("estrela", "Hoshizora no Kakera", Linha(1, Frase, "Aizawa, Sakura. Vocês dois, cinco minutos atrasados."));
        NovoProjeto("chuva", "Amaoto no Hakoniwa", Linha(1, Frase, null, TranslationStatus.Untranslated));

        var sync = await Run("estrela", "memory", "sync");
        var achou = await Run("chuva", "memory", "find", Frase, JsonOption);

        Assert.Equal(0, sync.ExitCode);
        Assert.Contains("1 novos", sync.Output);
        Assert.Equal(0, achou.ExitCode);
        using var doc = JsonDocument.Parse(achou.Output);
        var par = Assert.Single(doc.RootElement.GetProperty("data").GetProperty("matches").EnumerateArray());
        Assert.Equal(100, par.GetProperty("percent").GetInt32());
        Assert.True(par.GetProperty("exact").GetBoolean());
        Assert.Equal("Hoshizora no Kakera", par.GetProperty("project").GetString());
        Assert.Equal("Aizawa, Sakura. Vocês dois, cinco minutos atrasados.", par.GetProperty("target").GetString());
    }

    [Fact]
    public async Task Find_nao_mostra_os_pares_do_proprio_projeto_e_avisa_quando_nao_acha()
    {
        NovoProjeto("estrela", "Hoshizora no Kakera", Linha(1, Frase, "Atrasados."));
        await Run("estrela", "memory", "sync");

        var result = await Run("estrela", "memory", "find", Frase);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Nenhum par parecido", result.Output);
    }

    [Fact]
    public async Task Find_em_tabela_mostra_percentual_traducao_projeto_e_data()
    {
        NovoProjeto("estrela", "Hoshizora no Kakera", Linha(1, Frase, "Atrasados."));
        NovoProjeto("chuva", "Amaoto no Hakoniwa");
        await Run("estrela", "memory", "sync");

        var result = await Run("chuva", "memory", "find", Frase);

        Assert.Contains("100", result.Output);
        Assert.Contains("Atrasados.", result.Output);
        Assert.Contains("Hoshizora no Kakera", result.Output);
    }

    [Fact]
    public async Task Find_recusa_percentual_fora_de_1_a_100()
    {
        NovoProjeto("estrela", "E");

        var result = await Run("estrela", "memory", "find", Frase, "--min", "120");

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task Stats_conta_pares_e_projetos_e_sync_repetido_nao_muda_nada()
    {
        NovoProjeto("a", "A", Linha(1, "あ", "A."), Linha(2, "い", "I."));
        NovoProjeto("b", "B", Linha(1, "う", "U."));

        var vazia = await Run("a", "memory", "stats");
        await Run("a", "memory", "sync");
        await Run("b", "memory", "sync");
        var outra = await Run("a", "memory", "sync");
        var stats = await Run("a", "memory", "stats", JsonOption);

        Assert.Contains("vazia", vazia.Output);
        Assert.Contains("já estava em dia", outra.Output);
        using var doc = JsonDocument.Parse(stats.Output);
        Assert.Equal(3, doc.RootElement.GetProperty("data").GetProperty("entries").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("data").GetProperty("projects").GetInt32());
    }

    [Fact]
    public async Task Arquivo_da_memoria_ilegivel_falha_com_mensagem_em_portugues()
    {
        NovoProjeto("a", "A", Linha(1, "あ", "A."));
        Directory.CreateDirectory(_cli.UserConfigDirectory);
        File.WriteAllText(Path.Combine(_cli.UserConfigDirectory, TranslationMemory.FileName), "lixo");

        var result = await Run("a", "memory", "sync");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(TranslationMemory.FileName, result.Error + result.Output);
    }

    [Fact]
    public async Task Glossario_pessoal_guarda_usa_lista_e_remove()
    {
        var projeto = NovoProjeto("a", "A");
        projeto.SaveGlossary([new GlossaryTerm("先輩", "senpai", "manter em romaji"), new GlossaryTerm("遅刻", "atraso")]);
        var outro = NovoProjeto("b", "B");

        var guardou = await Run("a", "glossary", "personal", "keep", "先輩");
        var usou = await Run("b", "glossary", "personal", "use", "先輩");
        var lista = await Run("b", "glossary", "personal", "list", JsonOption);
        var removeu = await Run("b", "glossary", "personal", "remove", "先輩");

        Assert.Equal(0, guardou.ExitCode);
        Assert.Equal(0, usou.ExitCode);
        Assert.Equal(new GlossaryTerm("先輩", "senpai", "manter em romaji"), Assert.Single(outro.LoadGlossary()));
        using var doc = JsonDocument.Parse(lista.Output);
        var termo = Assert.Single(doc.RootElement.GetProperty("data").GetProperty("terms").EnumerateArray());
        Assert.Equal("senpai", termo.GetProperty("target").GetString());
        Assert.Equal(1, termo.GetProperty("projects").GetInt32());
        Assert.Equal("no-projeto", termo.GetProperty("state").GetString());
        Assert.Equal(0, removeu.ExitCode);
        Assert.Empty(new PersonalGlossary(_cli.UserConfigDirectory).Load());
    }

    [Fact]
    public async Task Use_nao_troca_a_traducao_do_projeto_sem_replace()
    {
        var projeto = NovoProjeto("a", "A");
        projeto.SaveGlossary([new GlossaryTerm("遅刻", "atraso")]);
        new PersonalGlossary(_cli.UserConfigDirectory).Upsert(new PersonalTerm("遅刻", "chegar tarde"));

        var sem = await Run("a", "glossary", "personal", "use", "遅刻");
        Assert.Equal("atraso", Assert.Single(projeto.LoadGlossary()).Target);
        Assert.Contains("--replace", sem.Output);

        await Run("a", "glossary", "personal", "use", "遅刻", "--replace");
        Assert.Equal("chegar tarde", Assert.Single(projeto.LoadGlossary()).Target);
    }

    [Fact]
    public async Task Keep_de_termo_que_o_projeto_nao_tem_e_use_de_termo_que_o_pessoal_nao_tem_falham()
    {
        NovoProjeto("a", "A");

        Assert.Equal(1, (await Run("a", "glossary", "personal", "keep", "先輩")).ExitCode);
        Assert.Equal(1, (await Run("a", "glossary", "personal", "use", "先輩")).ExitCode);
        Assert.Equal(1, (await Run("a", "glossary", "personal", "remove", "先輩")).ExitCode);
    }
}
