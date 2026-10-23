using System.Text.Json;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

public sealed class GlossaryCharsCommandTests : IDisposable
{
    private readonly CliHarness _cli = new();
    private readonly string _projectPath;
    private readonly ProjectFolder _project;

    public GlossaryCharsCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = ProjectFolder.Create(_projectPath, new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
    }

    public void Dispose() => _cli.Dispose();

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync([.. args, "--project", _projectPath], TestContext.Current.CancellationToken);

    [Fact]
    public async Task Glossary_add_grava_o_termo_e_json_devolve_a_entrada()
    {
        var result = await Run("glossary", "add", "先輩", "veterano", "--notes", "tratamento", "--json");

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("先輩", data.GetProperty("source").GetString());
        Assert.Equal("veterano", data.GetProperty("target").GetString());
        Assert.Equal("added", data.GetProperty("action").GetString());
        Assert.Equal(new GlossaryTerm("先輩", "veterano", "tratamento"), Assert.Single(_project.LoadGlossary()));
    }

    [Fact]
    public async Task Glossary_add_de_termo_existente_falha_sem_force_e_nao_altera_o_arquivo()
    {
        await Run("glossary", "add", "先輩", "veterano");

        var result = await Run("glossary", "add", "先輩", "sênior");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("veterano", Assert.Single(_project.LoadGlossary()).Target);
    }

    [Fact]
    public async Task Glossary_add_com_force_substitui_o_termo()
    {
        await Run("glossary", "add", "先輩", "veterano");

        var result = await Run("glossary", "add", "先輩", "sênior", "--force", "--json");

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal("updated", doc.RootElement.GetProperty("data").GetProperty("action").GetString());
        Assert.Equal("sênior", Assert.Single(_project.LoadGlossary()).Target);
    }

    [Fact]
    public async Task Glossary_add_com_original_vazio_falha()
    {
        var result = await Run("glossary", "add", " ", "x");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(_project.LoadGlossary());
    }

    [Fact]
    public async Task Glossary_list_json_devolve_os_termos_na_ordem_do_arquivo()
    {
        await Run("glossary", "add", "先輩", "veterano");
        await Run("glossary", "add", "後輩", "calouro");

        var result = await Run("glossary", "list", "--json");

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        var terms = doc.RootElement.GetProperty("data").GetProperty("terms").EnumerateArray().ToList();
        Assert.Equal(["先輩", "後輩"], terms.Select(t => t.GetProperty("source").GetString()));
    }

    [Fact]
    public async Task Chars_set_cria_o_personagem_com_genero_e_tom()
    {
        var result = await Run("chars", "set", "美咲", "Misaki", "--gender", "female", "--tone", "formal", "--json");

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("added", data.GetProperty("action").GetString());
        Assert.Equal("female", data.GetProperty("gender").GetString());
        Assert.Equal(new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "formal"), Assert.Single(_project.LoadCharacters()));
    }

    [Fact]
    public async Task Chars_set_em_personagem_existente_muda_so_o_que_foi_informado()
    {
        await Run("chars", "set", "美咲", "Misaki", "--gender", "female", "--tone", "formal");

        var result = await Run("chars", "set", "美咲", "Misaki-chan", "--json");

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal("updated", doc.RootElement.GetProperty("data").GetProperty("action").GetString());
        Assert.Equal(new CharacterEntry("美咲", "Misaki-chan", CharacterGender.Female, "formal"), Assert.Single(_project.LoadCharacters()));
    }

    [Fact]
    public async Task Chars_set_com_genero_invalido_falha_listando_os_validos()
    {
        var result = await Run("chars", "set", "美咲", "Misaki", "--gender", "x");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(_project.LoadCharacters());
    }

    [Fact]
    public async Task Chars_list_json_e_texto_mostram_os_personagens()
    {
        await Run("chars", "set", "美咲", "Misaki", "--gender", "female");
        await Run("chars", "set", "健", "Ken", "--gender", "male", "--tone", "casual");

        var json = await Run("chars", "list", "--json");
        Assert.Equal(0, json.ExitCode);
        using var doc = JsonDocument.Parse(json.Output);
        var characters = doc.RootElement.GetProperty("data").GetProperty("characters").EnumerateArray().ToList();
        Assert.Equal(["美咲", "健"], characters.Select(c => c.GetProperty("source").GetString()));
        Assert.Equal("casual", characters[1].GetProperty("tone").GetString());

        var text = await Run("chars", "list");
        Assert.Equal(0, text.ExitCode);
        Assert.Contains("美咲", text.Output);
        Assert.Contains("Ken", text.Output);
    }

    [Fact]
    public async Task Chars_list_sem_arquivo_devolve_lista_vazia()
    {
        var result = await Run("chars", "list", "--json");

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Empty(doc.RootElement.GetProperty("data").GetProperty("characters").EnumerateArray());
    }

    [Fact]
    public async Task Traducao_vazia_falha_nos_dois_comandos()
    {
        Assert.Equal(1, (await Run("glossary", "add", "先輩", " ")).ExitCode);
        Assert.Equal(1, (await Run("chars", "set", "美咲", "")).ExitCode);
        Assert.Empty(_project.LoadGlossary());
        Assert.Empty(_project.LoadCharacters());
    }
}
