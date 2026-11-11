using VNForge.Core.Factsheet;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;
using static VNForge.Core.Tests.Factsheet.SheetSamples;

namespace VNForge.Core.Tests.Factsheet;

public sealed class SheetProjectBridgeTests : IDisposable
{
    private readonly TempFolder _folder = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    private SheetStore Store => new(Path.Combine(_folder.Path, "config"));

    private ProjectFolder NewProject()
    {
        var project = ProjectFolder.Create(Path.Combine(_folder.Path, "projeto"), new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        project.SaveText("yst00001", [Name(0, "相沢健太"), Name(1, "佐倉葵"), Name(2, "ゴースト")]);
        return project;
    }

    private static TextUnit Name(int n, string source) =>
        new($"yst00001:{n:D6}", TextUnitKind.Name, null, source, null, TranslationStatus.Untranslated);

    private async Task<(GameSheet Game, IReadOnlyList<CharacterSheet> Characters)> Prepared(bool review = true, VndbCharacter[]? characters = null)
    {
        characters ??= [Kenta(), Aoi(), Ghost() with { Name = "Gōsuto", Original = "ゴースト" }];
        await SheetFetcher.FetchAsync(new VNForge.Vndb.FakeVndbCatalog([Game()], characters), Store, Game1, cancellationToken: Ct);
        await new SheetTranslationRunner(new StubSheetTranslator(), Store).RunAsync(Game1, cancellationToken: Ct);
        if (review)
        {
            SheetEditor.Review(Store, Game1);
        }

        var game = Store.LoadGame(Game1)!;
        return (game, Store.LoadCharacters(game.CharacterIds));
    }

    [Fact]
    public async Task Texto_ainda_nao_revisado_fica_de_fora_a_menos_que_se_aceite_o_da_IA()
    {
        var project = NewProject();
        var (game, characters) = await Prepared(review: false);

        var strict = SheetProjectBridge.Apply(project, game, characters);

        Assert.Equal(3, strict.NotReady);
        Assert.False(strict.SynopsisApplied);
        Assert.All(project.LoadCharacters(), c => Assert.Null(c.Description));
        Assert.Equal(string.Empty, project.LoadBible().Synopsis);

        var loose = SheetProjectBridge.Apply(project, game, characters, TranslationStatus.Machine);

        Assert.Equal(0, loose.NotReady);
        Assert.True(loose.SynopsisApplied);
        Assert.Equal("PT: The hero.", project.LoadCharacters().Single(c => c.Source == "相沢健太").Description);
    }

    [Fact]
    public async Task Aplicar_de_novo_nao_muda_nada()
    {
        var project = NewProject();
        var (game, characters) = await Prepared();
        SheetProjectBridge.Apply(project, game, characters);
        var characterFile = File.ReadAllText(project.CharactersPath);
        var bibleFile = File.ReadAllText(project.BiblePath);

        var again = SheetProjectBridge.Apply(project, game, characters);

        Assert.Equal(new VndbApplyResult(3, 0, 0), again.Characters);
        Assert.False(again.SynopsisApplied);
        Assert.Equal(characterFile, File.ReadAllText(project.CharactersPath));
        Assert.Equal(bibleFile, File.ReadAllText(project.BiblePath));
    }
}
