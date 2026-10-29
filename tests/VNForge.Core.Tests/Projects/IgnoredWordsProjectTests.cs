using VNForge.Core.Engines;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Spelling;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>As palavras ignoradas pelo corretor (#511) de ponta a ponta: o contexto do QA, a grade do Editor e o build com o arquivo ilegível.</summary>
public sealed class IgnoredWordsProjectTests : IDisposable
{
    private const string Quebrado = "<<<<<<< HEAD\n{\"version\":1,\"entries\":[]}\n=======\n>>>>>>> outro\n";

    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly ProjectFolder _project;
    private readonly FakeEngine _engine = new();

    public IgnoredWordsProjectTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "a001.fake"), "dialogue|Olá\nname|Ana");
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig(_game.Replace(Path.DirectorySeparatorChar, '/'), "Jogo.exe"),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("sjis"),
                AiConfig.Default));
        _engine.Unpack(_game, _project.GameCacheDirectory, TestContext.Current.CancellationToken);
    }

    public void Dispose() => _temp.Dispose();

    private void FalaComErro(string traducao)
    {
        var units = new List<TextUnit>(_engine.Scripts[0].Extract(
            new ScriptFile("a001.fake", File.ReadAllBytes(Path.Combine(_project.GameCacheDirectory, "pacote.fake", "a001.fake"))), new FakeContext()).Units);
        units[0] = units[0] with { Translation = traducao, Status = TranslationStatus.Translated };
        _project.SaveText("a001", units);
    }

    [Theory]
    [InlineData(Quebrado)]
    [InlineData("{\"version\":1,\"entries\":[{\"word\":\"\"}]}")]
    public void O_contexto_do_projeto_tolera_o_arquivo_de_palavras_ilegivel(string conteudo)
    {
        File.WriteAllText(_project.IgnoredWordsPath, conteudo);

        var context = QaContext.FromProject(_project);

        Assert.Empty(context.IgnoredWords);
        Assert.Equal(conteudo, File.ReadAllText(_project.IgnoredWordsPath));
    }

    [Fact]
    public void A_grade_do_editor_abre_com_o_arquivo_de_palavras_ilegivel()
    {
        _ = SpellChecker.Portuguese;
        FalaComErro("Eu esquesi a chave.");
        File.WriteAllText(_project.IgnoredWordsPath, Quebrado);

        var lines = ProjectLines.Read(_project, new QaEngine([new SpellingRule()]));

        var line = Assert.Single(lines.LinesOf("a001"), l => l.Unit.HasTranslation);
        Assert.Contains(line.Issues, i => i.RuleId == SpellingRule.RuleId);
    }

    [Fact]
    public void O_build_com_o_arquivo_de_palavras_ilegivel_segue_e_avisa()
    {
        FalaComErro("Bom dia");
        File.WriteAllText(_project.IgnoredWordsPath, Quebrado);

        var result = ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([new SpellingRule()]));

        Assert.Contains(result.Warnings, w => w.Contains("ignored-words.json", StringComparison.Ordinal) && w.Contains("ignorado", StringComparison.Ordinal));
        Assert.Equal(Quebrado, File.ReadAllText(_project.IgnoredWordsPath));
    }

    [Fact]
    public void O_build_com_o_arquivo_de_palavras_valido_nao_avisa()
    {
        FalaComErro("Bom dia");
        _project.AddIgnoredWord("Zorgblat");

        var result = ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([new SpellingRule()]));

        Assert.DoesNotContain(result.Warnings, w => w.Contains("ignored-words.json", StringComparison.Ordinal));
    }

    [Fact]
    public void O_apostrofo_curvo_e_o_reto_sao_a_mesma_palavra_ignorada()
    {
        Assert.True(_project.AddIgnoredWord("D'arc"));
        Assert.False(_project.AddIgnoredWord("D’arc"));

        Assert.Single(_project.LoadIgnoredWords());
        var ex = Assert.Throws<VNForge.Core.Glossary.GlossaryFileException>(() => IgnoredWordsSerializer.Serialize([new IgnoredWord("D'arc"), new IgnoredWord("d’arc")]));
        Assert.Contains("mais de uma vez", ex.Reason);
    }
}
