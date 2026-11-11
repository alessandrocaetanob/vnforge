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

    [Fact]
    public void O_build_com_o_arquivo_de_palavras_ilegivel_segue_e_avisa()
    {
        FalaComErro("Bom dia");
        File.WriteAllText(_project.IgnoredWordsPath, Quebrado);

        var result = ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([new SpellingRule()]));

        Assert.Contains(result.Warnings, w => w.Contains("ignored-words.json", StringComparison.Ordinal) && w.Contains("ignorado", StringComparison.Ordinal));
        Assert.Equal(Quebrado, File.ReadAllText(_project.IgnoredWordsPath));
    }
}
