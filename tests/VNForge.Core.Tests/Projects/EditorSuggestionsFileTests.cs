using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public sealed class EditorSuggestionsFileTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;

    public EditorSuggestionsFileTests()
    {
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig("jogo", "Jogo.exe"),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private static EditorSuggestion Suggest(string key, string id, string translation, string proposed = "Vocês dois") =>
        new(key, id, EditorSuggestionKind.Fluency, translation, proposed, "Soa mais natural.", Noon);

    private static TextUnit Unit(string id, string? translation) =>
        new(id, TextUnitKind.Dialogue, null, "原文", translation, TranslationStatus.Machine);

    [Fact]
    public void Sem_arquivo_as_sugestoes_comecam_vazias()
    {
        Assert.Empty(EditorSuggestionsFile.Load(_project).Suggestions);
    }

    [Fact]
    public void Gravar_e_ler_devolve_as_mesmas_sugestoes()
    {
        var file = EditorSuggestionsFile.Empty.ReplacingScript("y", [Suggest("y", "y:1", "Os dois"), Suggest("y", "y:2", "Tarde")]);

        EditorSuggestionsFile.Save(_project, file);

        var read = EditorSuggestionsFile.Load(_project);
        Assert.Equal(file.Suggestions, read.Suggestions);
        Assert.Contains("\"kind\": \"fluency\"", File.ReadAllText(EditorSuggestionsFile.PathFor(_project)));
    }

    [Fact]
    public void O_arquivo_fica_no_cache_do_projeto()
    {
        Assert.Equal(Path.Combine(_project.CacheDirectory, "editor-suggestions.json"), EditorSuggestionsFile.PathFor(_project));
    }

    [Fact]
    public void Gravar_sem_sugestoes_apaga_o_arquivo()
    {
        EditorSuggestionsFile.Save(_project, EditorSuggestionsFile.Empty.ReplacingScript("y", [Suggest("y", "y:1", "A")]));

        EditorSuggestionsFile.Save(_project, EditorSuggestionsFile.Empty);

        Assert.False(File.Exists(EditorSuggestionsFile.PathFor(_project)));
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("{\"version\":99,\"suggestions\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("[]")]
    public void Arquivo_ilegivel_ou_de_outra_versao_conta_como_vazio_sem_derrubar_o_Editor(string content)
    {
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(EditorSuggestionsFile.PathFor(_project), content);

        Assert.Empty(EditorSuggestionsFile.Load(_project).Suggestions);
    }

    [Fact]
    public void Tipo_desconhecido_no_arquivo_conta_como_vazio()
    {
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(
            EditorSuggestionsFile.PathFor(_project),
            """{"version":1,"suggestions":[{"scriptKey":"y","unitId":"y:1","kind":"estilo","base":"a","proposed":"b","reason":"r","at":"2026-11-09T15:00:00+00:00"}]}""");

        Assert.Empty(EditorSuggestionsFile.Load(_project).Suggestions);
    }

    [Fact]
    public void A_sugestao_vale_enquanto_a_linha_tem_a_traducao_em_que_ela_foi_feita()
    {
        var file = EditorSuggestionsFile.Empty.ReplacingScript("y", [Suggest("y", "y:1", "Os dois")]);

        Assert.NotNull(file.For("y", Unit("y:1", "Os dois")));
        Assert.Null(file.For("y", Unit("y:1", "Os dois, atrasados.")));
        Assert.Null(file.For("y", Unit("y:1", null)));
        Assert.Null(file.For("outra", Unit("y:1", "Os dois")));
        Assert.Null(file.For("y", Unit("y:2", "Os dois")));
    }

    [Fact]
    public void LiveIn_devolve_as_vivas_na_ordem_das_linhas()
    {
        var file = EditorSuggestionsFile.Empty.ReplacingScript("y", [Suggest("y", "y:3", "C"), Suggest("y", "y:1", "A"), Suggest("y", "y:2", "velha")]);

        var live = file.LiveIn("y", [Unit("y:1", "A"), Unit("y:2", "B mudada"), Unit("y:3", "C")]);

        Assert.Equal(["y:1", "y:3"], live.Select(s => s.UnitId));
    }

    [Fact]
    public void Uma_passada_nova_substitui_so_as_da_cena_dela()
    {
        var file = EditorSuggestionsFile.Empty
            .ReplacingScript("y", [Suggest("y", "y:1", "A")])
            .ReplacingScript("z", [Suggest("z", "z:1", "Z")])
            .ReplacingScript("y", [Suggest("y", "y:2", "B")]);

        Assert.Equal(["z:1", "y:2"], file.Suggestions.Select(s => s.UnitId));
    }

    [Fact]
    public void Without_tira_so_a_sugestao_da_linha_e_devolve_o_proprio_arquivo_quando_nao_ha_nada_a_tirar()
    {
        var file = EditorSuggestionsFile.Empty.ReplacingScript("y", [Suggest("y", "y:1", "A"), Suggest("y", "y:2", "B")]);

        var without = file.Without("y", "y:1");

        Assert.Equal(["y:2"], without.Suggestions.Select(s => s.UnitId));
        Assert.Same(file, file.Without("y", "y:9"));
    }

    [Theory]
    [InlineData(EditorSuggestionKind.Fluency, "fluidez")]
    [InlineData(EditorSuggestionKind.Voice, "voz")]
    [InlineData(EditorSuggestionKind.Consistency, "consistência")]
    public void O_tipo_tem_nome_em_portugues(EditorSuggestionKind kind, string label)
    {
        Assert.Equal(label, (Suggest("y", "y:1", "A") with { Kind = kind }).KindLabel);
    }

    [Fact]
    public void Gravar_numa_pasta_que_sumiu_falha_sem_recriar_o_caminho_antigo()
    {
        var gone = ProjectFolder.Open(_project.Root);
        Directory.Delete(_project.Root, recursive: true);

        Assert.Throws<DirectoryNotFoundException>(() => EditorSuggestionsFile.Save(gone, EditorSuggestionsFile.Empty.ReplacingScript("y", [Suggest("y", "y:1", "A")])));
        Assert.False(Directory.Exists(_project.Root));
    }
}
