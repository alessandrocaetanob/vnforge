using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;
using VNForge.Vndb;
using static VNForge.Core.Tests.Factsheet.SheetSamples;

namespace VNForge.Core.Tests.Factsheet;

public sealed class SheetFetchAndStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Buscar_traz_sinopse_com_spoiler_no_lugar_tags_duracao_e_personagens_por_papel()
    {
        using var folder = new TempFolder();
        var store = new SheetStore(folder.Path);

        var result = await SheetFetcher.FetchAsync(Catalog(), store, Game1, cancellationToken: Ct);

        var sheet = store.LoadGame(Game1)!;
        Assert.Equal(sheet.FetchedAt, result.Sheet.FetchedAt);
        Assert.Equal("A rainy town. [spoiler]He dies.[/spoiler] Kenta returns.", sheet.Synopsis!.Source);
        Assert.Equal(TranslationStatus.Untranslated, sheet.Synopsis.Status);
        Assert.Equal(["g10", "g12", "g11"], sheet.Tags.Select(t => t.Id));
        Assert.Equal(1500, sheet.LengthMinutes);
        Assert.Equal(["c1", "c2", "c3"], sheet.CharacterIds);
        Assert.Equal(3, result.Characters);
        Assert.False(sheet.CharactersTruncated);
    }

    [Fact]
    public async Task Os_nomes_de_tag_e_de_traco_entram_uma_vez_e_o_personagem_guarda_o_papel_e_o_spoiler()
    {
        using var folder = new TempFolder();
        var store = new SheetStore(folder.Path);

        var result = await SheetFetcher.FetchAsync(Catalog(), store, Game1, cancellationToken: Ct);

        Assert.Equal(7, result.NewTerms);
        var terms = store.LoadTerms();
        Assert.Equal(["g10", "g11", "g12"], terms.All.Where(t => t.Kind == SheetTermKind.Tag).Select(t => t.Id));
        Assert.Equal(["i1", "i104", "i2", "i3"], terms.All.Where(t => t.Kind == SheetTermKind.Trait).Select(t => t.Id));
        Assert.Equal("Hair", terms.Find(SheetTermKind.Trait, "i1")!.Context);
        var kenta = store.LoadCharacter("c1")!;
        Assert.Equal(("main", 0), (kenta.In(Game1)!.Role, kenta.In(Game1)!.Spoiler));
        Assert.Equal([new SheetAppearanceEdition(null, "main", 0)], kenta.In(Game1)!.Editions);
        Assert.Equal(("side", 2), (store.LoadCharacter("c3")!.In(Game1)!.Role, store.LoadCharacter("c3")!.In(Game1)!.Spoiler));
        Assert.Equal(("m", "m"), (kenta.Gender!.Visible, kenta.Gender.Spoiler));
    }

    [Fact]
    public async Task Buscar_de_novo_mantem_as_traducoes_e_o_original_que_mudou_fica_desatualizado()
    {
        using var folder = new TempFolder();
        var store = new SheetStore(folder.Path);
        await SheetFetcher.FetchAsync(Catalog(), store, Game1, cancellationToken: Ct);
        store.Update(s =>
        {
            var game = s.LoadGame(Game1)!;
            s.SaveGame(game with { Synopsis = game.Synopsis!.TranslatedByAi("A rainy town. [spoiler]He dies.[/spoiler] Kenta returns.").Reviewed() });
            var terms = s.LoadTerms();
            terms.Replace(SheetTermKind.Tag, "g10", terms.Find(SheetTermKind.Tag, "g10")!.Text.TranslatedByAi("Comédia"));
            s.SaveTerms(terms);
        });

        var changed = Catalog(Game(description: "A new synopsis."));
        var again = await SheetFetcher.FetchAsync(changed, store, Game1, cancellationToken: Ct);

        var synopsis = again.Sheet.Synopsis!;
        Assert.Equal(("A new synopsis.", TranslationStatus.Translated, true), (synopsis.Source, synopsis.Status, synopsis.Outdated));
        Assert.Equal("Comédia", store.LoadTerms().Find(SheetTermKind.Tag, "g10")!.Text.Target);
        Assert.Equal(0, again.NewTerms);
    }

    [Fact]
    public async Task O_mesmo_personagem_em_dois_jogos_e_um_registro_so_com_as_duas_aparicoes()
    {
        using var folder = new TempFolder();
        var store = new SheetStore(folder.Path);
        var kentaInBoth = Kenta() with
        {
            Appearances = [new(Game1, "main", 0, null), new(Game1, "side", 1, "r2"), new(Game2, "side", 1, null)],
        };
        var catalog = new FakeVndbCatalog([Game(), Game(Game2, "Sequel")], [kentaInBoth]);

        await SheetFetcher.FetchAsync(catalog, store, Game1, cancellationToken: Ct);
        await SheetFetcher.FetchAsync(catalog, store, Game2, cancellationToken: Ct);

        var kenta = store.LoadCharacter("c1")!;
        Assert.Equal([(Game1, "main", 1), (Game2, "side", 1)], kenta.Appearances.Select(a => (a.VisualNovelId, a.Role, a.Spoiler)));
        Assert.Equal([new SheetAppearanceEdition(null, "main", 0), new SheetAppearanceEdition("r2", "side", 1), new SheetAppearanceEdition(null, "side", 1)], kenta.Appearances.SelectMany(a => a.Editions!).Take(3));
        Assert.Equal(["c1"], store.LoadAllCharacters().Select(c => c.Id));
        Assert.Equal(["c1"], store.LoadGame(Game2)!.CharacterIds);
    }

    [Fact]
    public async Task Jogo_que_o_VNDB_nao_tem_recusa_em_portugues()
    {
        using var folder = new TempFolder();

        var ex = await Assert.ThrowsAsync<LibraryException>(() => SheetFetcher.FetchAsync(Catalog(), new SheetStore(folder.Path), "v1", cancellationToken: Ct));

        Assert.Contains("v1", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../v1")]
    [InlineData("c1")]
    [InlineData("v")]
    [InlineData("")]
    public void Id_que_nao_e_de_jogo_nao_vira_nome_de_arquivo(string id)
    {
        using var folder = new TempFolder();

        Assert.Throws<LibraryException>(() => new SheetStore(folder.Path).LoadGame(id));
    }

    [Fact]
    public void Arquivo_ilegivel_e_recusado_citando_o_caminho_e_nunca_sobrescrito()
    {
        using var folder = new TempFolder();
        var store = new SheetStore(folder.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(store.GamePath(Game1))!);
        File.WriteAllText(store.GamePath(Game1), "{ não é json");
        File.WriteAllText(store.TermsPath, "{\"version\":9,\"data\":{\"terms\":[]}}");

        Assert.Contains(Game1, Assert.Throws<LibraryException>(() => store.LoadGame(Game1)).Message, StringComparison.Ordinal);
        Assert.Contains("versão 9", Assert.Throws<LibraryException>(store.LoadTerms).Message, StringComparison.Ordinal);
        Assert.Equal("{ não é json", File.ReadAllText(store.GamePath(Game1)));
    }

    [Fact]
    public async Task Sem_arquivo_a_ficha_nao_existe_e_gravar_nao_deixa_temporario()
    {
        using var folder = new TempFolder();
        var store = new SheetStore(folder.Path);
        Assert.Null(store.LoadGame(Game1));
        Assert.Empty(store.LoadTerms().All);
        Assert.Empty(store.LoadAllCharacters());

        await SheetFetcher.FetchAsync(Catalog(), store, Game1, cancellationToken: Ct);

        Assert.DoesNotContain(Directory.EnumerateFiles(store.Root, "*.tmp", SearchOption.AllDirectories), _ => true);
        Assert.Equal(3, store.LoadAllCharacters().Count);
    }
}
