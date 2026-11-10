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
    public async Task A_descricao_em_portugues_vai_para_o_Glossario_sem_o_spoiler_e_a_sinopse_para_a_biblia()
    {
        var project = NewProject();
        var (game, characters) = await Prepared();

        var result = SheetProjectBridge.Apply(project, game, characters);

        Assert.Equal(new VndbApplyResult(3, 3, 0), result.Characters);
        Assert.True(result.SynopsisApplied);
        var saved = project.LoadCharacters().ToDictionary(c => c.Source);
        Assert.Equal("PT: The hero.", saved["相沢健太"].Description);
        Assert.Equal(CharacterGender.Male, saved["相沢健太"].Gender);
        Assert.Equal("c1", saved["相沢健太"].VndbId);
        Assert.Equal("PT: A rainy town. Kenta returns.", project.LoadBible().Synopsis);
        Assert.DoesNotContain("spoiler", File.ReadAllText(project.CharactersPath), StringComparison.Ordinal);
        Assert.DoesNotContain("dies", File.ReadAllText(project.BiblePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Personagem_que_e_spoiler_no_jogo_entra_so_com_o_nome_e_o_genero_sem_descricao()
    {
        var project = NewProject();
        var (game, characters) = await Prepared();

        SheetProjectBridge.Apply(project, game, characters);

        var ghost = project.LoadCharacters().Single(c => c.Source == "ゴースト");
        Assert.Null(ghost.Description);
    }

    [Fact]
    public async Task Personagem_com_spoiler_so_em_uma_edicao_ou_de_ficha_antiga_nao_leva_a_descricao()
    {
        var project = NewProject();
        var inEdition = Aoi() with { Appearances = [new VndbCharacterAppearance(Game1, "primary", 0, null), new VndbCharacterAppearance(Game1, "primary", 2, "r2")] };
        var (game, characters) = await Prepared(characters: [Kenta(), inEdition, Ghost() with { Name = "Gōsuto", Original = "ゴースト" }]);
        var old = characters.Select(c => c.Id == "c1" ? c with { Appearances = [.. c.Appearances.Select(a => a with { Spoiler = 0, Editions = null })] } : c).ToList();

        SheetProjectBridge.Apply(project, game, old);

        var linked = project.LoadCharacters();
        Assert.Null(linked.Single(c => c.Source == "佐倉葵").Description);
        Assert.Null(linked.Single(c => c.Source == "相沢健太").Description);
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
    public async Task O_que_a_pessoa_escreveu_fica_mas_o_original_em_ingles_do_VNDB_da_lugar_a_traducao()
    {
        var project = NewProject();
        var (game, characters) = await Prepared();
        project.SaveCharacters(
        [
            new CharacterEntry("相沢健太", "Kenta", Description: "The hero."),
            new CharacterEntry("佐倉葵", "Aoi", Description: "Minha descrição."),
        ]);
        project.UpdateBible(b => b.EditSynopsis("Minha sinopse."));

        var result = SheetProjectBridge.Apply(project, game, characters);

        var saved = project.LoadCharacters().ToDictionary(c => c.Source);
        Assert.Equal("PT: The hero.", saved["相沢健太"].Description);
        Assert.Equal("Minha descrição.", saved["佐倉葵"].Description);
        Assert.Equal("Kenta", saved["相沢健太"].Target);
        Assert.False(result.SynopsisApplied);
        Assert.Equal("Minha sinopse.", project.LoadBible().Synopsis);
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
