using System.Text.Json;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

public sealed class GlossaryCharsCommandTests : IDisposable
{
    private const string GenderOption = "--gender";
    private const string JsonOption = "--json";
    private const string Misaki = "Misaki";
    private const string ActionKey = "action";
    private const string CharsVerb = "chars";
    private const string Female = "female";
    private const string Formal = "formal";
    private const string GlossaryVerb = "glossary";
    private const string Veterano = "veterano";

    private readonly CliHarness _cli = new();
    private readonly string _projectPath;
    private readonly ProjectFolder _project;

    public GlossaryCharsCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = ProjectFolder.Create(_projectPath, new ProjectConfig(
            "Minha VN",
            new GameConfig(Path.Combine(_cli.Root, "jogo"), "MinhaVN.exe"),
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
        var result = await Run(GlossaryVerb, "add", "先輩", Veterano, "--notes", "tratamento", JsonOption);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("先輩", data.GetProperty("source").GetString());
        Assert.Equal(Veterano, data.GetProperty("target").GetString());
        Assert.Equal("added", data.GetProperty(ActionKey).GetString());
        Assert.Equal(new GlossaryTerm("先輩", Veterano, "tratamento"), Assert.Single(_project.LoadGlossary()));
    }

    [Fact]
    public async Task Glossary_add_de_termo_existente_falha_sem_force_e_nao_altera_o_arquivo()
    {
        await Run(GlossaryVerb, "add", "先輩", Veterano);

        var result = await Run(GlossaryVerb, "add", "先輩", "sênior");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(Veterano, Assert.Single(_project.LoadGlossary()).Target);
    }

    [Fact]
    public async Task Glossary_add_com_force_substitui_o_termo()
    {
        await Run(GlossaryVerb, "add", "先輩", Veterano);

        var result = await Run(GlossaryVerb, "add", "先輩", "sênior", "--force", JsonOption);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal("updated", doc.RootElement.GetProperty("data").GetProperty(ActionKey).GetString());
        Assert.Equal("sênior", Assert.Single(_project.LoadGlossary()).Target);
    }

    [Fact]
    public async Task Glossary_add_com_original_vazio_falha()
    {
        var result = await Run(GlossaryVerb, "add", " ", "x");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(_project.LoadGlossary());
    }

    [Fact]
    public async Task Glossary_list_json_devolve_os_termos_na_ordem_do_arquivo()
    {
        await Run(GlossaryVerb, "add", "先輩", Veterano);
        await Run(GlossaryVerb, "add", "後輩", "calouro");

        var result = await Run(GlossaryVerb, "list", JsonOption);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        var terms = doc.RootElement.GetProperty("data").GetProperty("terms").EnumerateArray().ToList();
        Assert.Equal(["先輩", "後輩"], terms.Select(t => t.GetProperty("source").GetString()));
    }

    [Fact]
    public async Task Chars_set_cria_o_personagem_com_genero_e_tom()
    {
        var result = await Run(CharsVerb, "set", "美咲", Misaki, GenderOption, Female, "--tone", Formal, JsonOption);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("added", data.GetProperty(ActionKey).GetString());
        Assert.Equal(Female, data.GetProperty("gender").GetString());
        Assert.Equal(new CharacterEntry("美咲", Misaki, CharacterGender.Female, Formal), Assert.Single(_project.LoadCharacters()));
    }

    [Fact]
    public async Task Chars_set_em_personagem_existente_muda_so_o_que_foi_informado()
    {
        await Run(CharsVerb, "set", "美咲", Misaki, GenderOption, Female, "--tone", Formal);

        var result = await Run(CharsVerb, "set", "美咲", "Misaki-chan", JsonOption);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal("updated", doc.RootElement.GetProperty("data").GetProperty(ActionKey).GetString());
        Assert.Equal(new CharacterEntry("美咲", "Misaki-chan", CharacterGender.Female, Formal), Assert.Single(_project.LoadCharacters()));
    }

    [Fact]
    public async Task Chars_set_com_genero_invalido_falha_listando_os_validos()
    {
        var result = await Run(CharsVerb, "set", "美咲", Misaki, GenderOption, "x");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(_project.LoadCharacters());
    }

    [Fact]
    public async Task Chars_list_json_e_texto_mostram_os_personagens()
    {
        await Run(CharsVerb, "set", "美咲", Misaki, GenderOption, Female);
        await Run(CharsVerb, "set", "健", "Ken", GenderOption, "male", "--tone", "casual");

        var json = await Run(CharsVerb, "list", JsonOption);
        Assert.Equal(0, json.ExitCode);
        using var doc = JsonDocument.Parse(json.Output);
        var characters = doc.RootElement.GetProperty("data").GetProperty("characters").EnumerateArray().ToList();
        Assert.Equal(["美咲", "健"], characters.Select(c => c.GetProperty("source").GetString()));
        Assert.Equal("casual", characters[1].GetProperty("tone").GetString());

        var text = await Run(CharsVerb, "list");
        Assert.Equal(0, text.ExitCode);
        Assert.Contains("美咲", text.Output);
        Assert.Contains("Ken", text.Output);
    }

    [Fact]
    public async Task Glossary_list_texto_e_uma_tabela_com_cabecalho_e_uma_linha_por_termo()
    {
        await Run(GlossaryVerb, "add", "先輩", Veterano, "--notes", "tratamento");
        await Run(GlossaryVerb, "add", "後輩", "calouro");

        var lines = Lines(await Run(GlossaryVerb, "list"));

        Assert.Equal(["Original", "Tradução", "Notas"], lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(["先輩", Veterano, "tratamento"], lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(["後輩", "calouro"], lines[2].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public async Task Glossary_list_sem_termos_avisa_que_esta_vazio()
    {
        var result = await Run(GlossaryVerb, "list");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("O glossário está vazio.", Lines(result).Single());
    }

    [Fact]
    public async Task Chars_list_texto_e_uma_tabela_com_genero_e_tom()
    {
        await Run(CharsVerb, "set", "美咲", Misaki, GenderOption, Female, "--tone", Formal);
        await Run(CharsVerb, "set", "健", "Ken");

        var lines = Lines(await Run(CharsVerb, "list"));

        Assert.Equal(["Original", "Tradução", "Gênero", "Tom"], lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(["美咲", Misaki, Female, Formal], lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(["健", "Ken", "unknown"], lines[2].Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Chars_list_sem_personagens_avisa_que_nao_ha_nenhum()
    {
        var result = await Run(CharsVerb, "list");

        Assert.Equal("Nenhum personagem cadastrado.", Lines(result).Single());
    }

    private static string[] Lines(RunResult result) =>
        [.. result.Output.TrimEnd().ReplaceLineEndings("\n").Split('\n').Select(l => l.TrimEnd())];

    [Fact]
    public async Task Chars_list_sem_arquivo_devolve_lista_vazia()
    {
        var result = await Run(CharsVerb, "list", JsonOption);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Empty(doc.RootElement.GetProperty("data").GetProperty("characters").EnumerateArray());
    }

    [Fact]
    public async Task Traducao_vazia_falha_nos_dois_comandos()
    {
        Assert.Equal(1, (await Run(GlossaryVerb, "add", "先輩", " ")).ExitCode);
        Assert.Equal(1, (await Run(CharsVerb, "set", "美咲", "")).ExitCode);
        Assert.Empty(_project.LoadGlossary());
        Assert.Empty(_project.LoadCharacters());
    }

    [Theory]
    [InlineData("MALE", CharacterGender.Male)]
    [InlineData(" other ", CharacterGender.Other)]
    [InlineData("Unknown", CharacterGender.Unknown)]
    public async Task Chars_set_aceita_genero_sem_distinguir_caixa(string text, CharacterGender expected)
    {
        var result = await Run(CharsVerb, "set", "健", "Ken", GenderOption, text);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, Assert.Single(_project.LoadCharacters()).Gender);
    }
}
