using VNForge.Core.Bible;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Bible;

public sealed class BibleProjectFileTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;

    public BibleProjectFileTests()
    {
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Minha VN",
                new GameConfig(Path.Combine(Path.GetTempPath(), "Jogos", "MinhaVN"), "MinhaVN.exe"),
                new EngineConfig("yuris", 500, "0xD36FAC96"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("fold"),
                AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private static GameBible Bible(string name = "Atraso no portão") => GameBible.From([new BibleScene("yst1") { Name = name, Summary = "Resumo." }]);

    [Fact]
    public void Biblia_ilegivel_nao_derruba_quem_so_consulta()
    {
        File.WriteAllText(_project.BiblePath, "{ quebrado");

        var bible = _project.LoadBibleOrEmpty(out var problem);

        Assert.True(bible.IsEmpty);
        Assert.Contains("bible.json", problem, StringComparison.Ordinal);
        Assert.Throws<GlossaryFileException>(_project.LoadBible);
        Assert.NotNull(_project.LoadScriptNames());
    }

    [Fact]
    public void O_nome_da_biblia_vale_entre_o_do_usuario_e_o_da_engine_sem_ir_para_o_scripts_json()
    {
        _project.SaveScriptNames(ScriptNames.Empty.WithDetected(new Dictionary<string, string?> { ["yst1"] = "manha", ["yst2"] = "tarde" }).Rename("yst2", "Meu nome"));
        _project.SaveBible(GameBible.From([new BibleScene("yst1") { Name = "Atraso no portão" }, new BibleScene("yst2") { Name = "Da IA" }]));
        var scriptsBefore = File.ReadAllText(_project.ScriptNamesPath);

        var names = _project.LoadScriptNames();

        Assert.Equal("Atraso no portão", names.DisplayName("yst1"));
        Assert.Equal("Meu nome", names.DisplayName("yst2"));
        Assert.Equal("manha", names.DetectedName("yst1"));
        Assert.Null(names.CustomName("yst1"));

        _project.RenameScript("yst1", null);
        Assert.Equal(scriptsBefore, File.ReadAllText(_project.ScriptNamesPath));
    }

    [Fact]
    public void Voltar_a_ultima_geracao_troca_a_biblia_pela_de_antes_e_voltar_de_novo_desfaz()
    {
        _project.SaveBible(Bible("Antiga"));
        _project.KeepPreviousBible();
        _project.SaveBible(Bible("Nova"));

        Assert.True(_project.HasPreviousBible);
        Assert.True(_project.RestorePreviousBible());
        Assert.Equal("Antiga", _project.LoadBible().SceneName("yst1"));

        Assert.True(_project.RestorePreviousBible());
        Assert.Equal("Nova", _project.LoadBible().SceneName("yst1"));
    }

    [Fact]
    public void Sem_biblia_anterior_voltar_nao_faz_nada()
    {
        _project.SaveBible(Bible());

        Assert.False(_project.HasPreviousBible);
        Assert.False(_project.RestorePreviousBible());
        Assert.Equal("Atraso no portão", _project.LoadBible().SceneName("yst1"));
    }
}
