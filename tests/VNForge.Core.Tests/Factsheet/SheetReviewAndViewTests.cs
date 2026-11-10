using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;
using static VNForge.Core.Tests.Factsheet.SheetSamples;

namespace VNForge.Core.Tests.Factsheet;

public sealed class SheetReviewAndViewTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public SheetReviewAndViewTests() => Store = new SheetStore(_folder.Path);

    private SheetStore Store { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    private async Task Translated()
    {
        await SheetFetcher.FetchAsync(Catalog(), Store, Game1, cancellationToken: Ct);
        await new SheetTranslationRunner(new StubSheetTranslator(), Store).RunAsync(Game1, cancellationToken: Ct);
    }

    private GameSheetCard Card(LibraryPrivacy? privacy = null)
    {
        var game = Store.LoadGame(Game1)!;
        return SheetViewer.Card(game, Store.LoadCharacters(game.CharacterIds), Store.LoadTerms(), privacy ?? new LibraryPrivacy());
    }

    [Fact]
    public async Task Revisar_tudo_e_depois_aprovar_tudo_leva_cada_texto_de_uma_etapa_a_outra()
    {
        await Translated();

        Assert.Equal(new SheetStatusCounts(0, 11, 0, 0, 0), Card().Counts);
        Assert.Equal(new SheetMarkResult(11, 0), SheetEditor.Review(Store, Game1));
        Assert.Equal(new SheetStatusCounts(0, 0, 0, 11, 0), Card().Counts);
        Assert.Equal(new SheetMarkResult(11, 0), SheetEditor.Approve(Store, Game1));
        Assert.Equal(new SheetStatusCounts(0, 0, 0, 0, 11), Card().Counts);
        Assert.Equal(new SheetMarkResult(0, 11), SheetEditor.Approve(Store, Game1));
    }

    [Fact]
    public async Task Revisar_tudo_deixa_de_fora_o_texto_desatualizado_e_so_o_pedido_pelo_nome_o_revisa()
    {
        await Translated();
        SheetEditor.Edit(Store, Game1, SheetField.Synopsis, "Minha sinopse. [spoiler]Ele morre.[/spoiler] Kenta volta.");
        await SheetFetcher.FetchAsync(Catalog(Game(description: "A new synopsis. [spoiler]He dies.[/spoiler]")), Store, Game1, cancellationToken: Ct);
        Assert.True(Store.LoadGame(Game1)!.Synopsis!.Outdated);

        var all = SheetEditor.Review(Store, Game1);

        var kept = Store.LoadGame(Game1)!.Synopsis!;
        Assert.Equal((TranslationStatus.Translated, true), (kept.Status, kept.Outdated));
        Assert.Equal(new SheetMarkResult(10, 0, 1), all);
        Assert.Equal(new SheetMarkResult(0, 10, 1), SheetEditor.Review(Store, Game1));

        SheetEditor.Review(Store, Game1, [SheetField.Synopsis]);

        Assert.Equal((TranslationStatus.Reviewed, false), (Store.LoadGame(Game1)!.Synopsis!.Status, Store.LoadGame(Game1)!.Synopsis!.Outdated));
    }

    [Fact]
    public async Task Revisar_um_nome_de_tag_vale_para_todos_os_jogos_porque_o_termo_e_um_so()
    {
        await Translated();

        SheetEditor.Review(Store, Game1, [SheetField.OfTag("g10")]);

        Assert.Equal(TranslationStatus.Reviewed, Store.LoadTerms().Find(SheetTermKind.Tag, "g10")!.Text.Status);
        Assert.Equal(TranslationStatus.Machine, Store.LoadTerms().Find(SheetTermKind.Tag, "g11")!.Text.Status);
    }

    [Fact]
    public async Task Aprovar_o_que_so_foi_traduzido_pela_IA_recusa_e_nao_grava_nada()
    {
        await Translated();

        var ex = Assert.Throws<LibraryException>(() => SheetEditor.Approve(Store, Game1, [SheetField.Synopsis]));

        Assert.Contains("Revise", ex.Message, StringComparison.Ordinal);
        Assert.Equal(TranslationStatus.Machine, Store.LoadGame(Game1)!.Synopsis!.Status);
    }

    [Fact]
    public async Task Corrigir_a_mao_grava_como_traduzido_e_exige_os_mesmos_trechos_de_spoiler()
    {
        await Translated();

        SheetEditor.Edit(Store, Game1, SheetField.OfCharacter("c1"), "O herói. [spoiler]Ele é um fantasma.[/spoiler]");

        var description = Store.LoadCharacter("c1")!.Description!;
        Assert.Equal(("O herói. [spoiler]Ele é um fantasma.[/spoiler]", TranslationStatus.Translated), (description.Target, description.Status));
        Assert.Throws<LibraryException>(() => SheetEditor.Edit(Store, Game1, SheetField.OfCharacter("c1"), "O herói."));
    }

    [Fact]
    public async Task Texto_de_outro_jogo_ou_que_nao_existe_na_ficha_recusa()
    {
        await Translated();

        Assert.Contains("c99", Assert.Throws<LibraryException>(() => SheetEditor.Review(Store, Game1, [SheetField.OfCharacter("c99")])).Message, StringComparison.Ordinal);
        Assert.Throws<LibraryException>(() => SheetEditor.Review(Store, Game1, [SheetField.OfTag("g77")]));
        Assert.Throws<LibraryException>(() => SheetEditor.Review(Store, "v1"));
    }

    [Fact]
    public async Task Personagem_com_spoiler_numa_edicao_ou_de_ficha_antiga_sem_as_edicoes_fica_escondido()
    {
        var inEdition = Aoi() with { Appearances = [new VndbCharacterAppearance(Game1, "primary", 0, null), new VndbCharacterAppearance(Game1, "primary", 2, "r2")] };
        await SheetFetcher.FetchAsync(new VNForge.Vndb.FakeVndbCatalog([Game()], [Kenta(), inEdition]), Store, Game1, cancellationToken: Ct);
        var game = Store.LoadGame(Game1)!;
        var characters = Store.LoadCharacters(game.CharacterIds);
        var old = characters.Select(c => c.Id == "c1" ? c with { Appearances = [.. c.Appearances.Select(a => a with { Spoiler = 0, Editions = null })] } : c).ToList();

        var fresh = SheetViewer.Card(game, characters, Store.LoadTerms(), new LibraryPrivacy());
        var stale = SheetViewer.Card(game, old, Store.LoadTerms(), new LibraryPrivacy());
        var open = SheetViewer.Card(game, old, Store.LoadTerms(), new LibraryPrivacy(ShowSpoilers: true));

        Assert.Equal(["Kenta Aizawa"], fresh.Characters.Select(c => c.Name));
        Assert.Equal(1, fresh.HiddenCharacters);
        Assert.Empty(stale.Characters);
        Assert.Equal(2, stale.HiddenCharacters);
        Assert.Equal(2, open.Characters.Count);
    }

    [Fact]
    public async Task O_spoiler_fica_escondido_por_padrao_pelo_nivel_do_VNDB()
    {
        await Translated();

        var card = Card();

        Assert.Equal("PT: A rainy town. [spoiler escondido] Kenta returns.", card.Synopsis!.Text);
        Assert.True(card.Synopsis.HasHiddenSpoilers);
        Assert.Equal(["Kenta Aizawa", "Aoi Sakura"], card.Characters.Select(c => c.Name));
        Assert.Equal(1, card.HiddenCharacters);
        Assert.Equal(["PT: Comedy"], card.Tags.Select(t => t.Name));
        Assert.Equal(2, card.HiddenTags);
        var kenta = card.Characters[0];
        Assert.Equal("PT: The hero. [spoiler escondido]", kenta.Description!.Text);
        Assert.Equal(["PT: Long Hair"], kenta.Traits.Select(t => t.Name));
        Assert.Equal(3, kenta.HiddenTraits);
    }

    [Fact]
    public async Task Pedindo_o_spoiler_aparece_tudo_o_que_tem_spoiler_e_o_genero_real()
    {
        await Translated();

        var card = Card(new LibraryPrivacy(ShowSpoilers: true));

        Assert.Equal("PT: A rainy town. He dies. Kenta returns.", card.Synopsis!.Text);
        Assert.False(card.Synopsis.HasHiddenSpoilers);
        Assert.Equal(["Kenta Aizawa", "Aoi Sakura", "Ghost"], card.Characters.Select(c => c.Name));
        Assert.Equal(0, card.HiddenCharacters);
        Assert.Equal(["PT: Comedy", "PT: Plot Twist"], card.Tags.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["PT: Long Hair", "PT: Amnesia", "PT: Tsundere"], card.Characters[0].Traits.Select(t => t.Name));
        var aoi = card.Characters[1];
        Assert.Equal(("feminino", "masculino"), (aoi.Gender, aoi.RealGender));
        Assert.Null(card.Characters[0].RealGender);
    }

    [Fact]
    public async Task O_genero_vem_em_dois_valores_e_o_real_so_com_o_spoiler_liberado()
    {
        await Translated();

        var hidden = Card().Characters[1];
        var shown = Card(new LibraryPrivacy(ShowSpoilers: true)).Characters[1];

        Assert.Equal(("feminino", null), (hidden.Gender, hidden.RealGender));
        Assert.Equal(("feminino", "masculino"), (shown.Gender, shown.RealGender));
    }

    [Fact]
    public async Task Conteudo_adulto_some_por_padrao_retrato_tag_erotica_e_traco_sexual()
    {
        await Translated();

        var hidden = Card();
        var shown = Card(new LibraryPrivacy(ShowAdult: true, ShowSpoilers: true));

        var aoi = hidden.Characters[1];
        Assert.Equal((null, true), (aoi.Image, aoi.ImageHidden));
        Assert.NotNull(hidden.Characters[0].Image);
        Assert.DoesNotContain(hidden.Tags, t => t.Category == "ero");
        Assert.DoesNotContain(hidden.Characters[0].Traits, t => t.Id == Adult.Id);
        Assert.NotNull(shown.Characters[1].Image);
        Assert.Contains(shown.Tags, t => t.Category == "ero");
        Assert.Contains(shown.Characters[0].Traits, t => t.Id == Adult.Id);
    }

    [Fact]
    public void O_traco_adulto_e_o_que_o_VNDB_marca_como_sexual_e_nao_o_nome_do_grupo()
    {
        var sexualInOtherGroup = new VndbTrait("i528", "Sitting Sex", "i1625", "Subject of (Sexual)", 0, false, Sexual: true);
        var namedLikeAdultButNot = new VndbTrait("i9", "Anything", "i1", "Sexual Content", 0, false, Sexual: false);
        var hiding = new LibraryPrivacy(ShowAdult: false);

        Assert.True(SheetViewer.IsTraitHidden(sexualInOtherGroup, hiding));
        Assert.True(SheetViewer.IsTraitHidden(Adult, hiding));
        Assert.False(SheetViewer.IsTraitHidden(namedLikeAdultButNot, hiding));
        Assert.False(SheetViewer.IsTraitHidden(sexualInOtherGroup, new LibraryPrivacy(ShowAdult: true)));
    }

    [Fact]
    public async Task Ficha_gravada_antes_do_campo_sexual_esconde_o_traco_ate_a_proxima_busca()
    {
        await Translated();
        var kenta = Store.LoadCharacter("c1")!;
        Store.SaveCharacter(kenta with { Traits = [.. kenta.Traits.Select(t => t with { Sexual = null })] });

        var hidden = Card();
        var shown = Card(new LibraryPrivacy(ShowAdult: true, ShowSpoilers: true));

        Assert.Empty(hidden.Characters[0].Traits);
        Assert.Equal(4, shown.Characters[0].Traits.Count);

        await SheetFetcher.FetchAsync(Catalog(), Store, Game1, cancellationToken: Ct);

        Assert.Equal(["i1"], Card().Characters[0].Traits.Select(t => t.Id));
    }

    [Fact]
    public async Task Aprovar_tudo_nao_pega_texto_desatualizado_porque_o_revisado_que_o_VNDB_mudou_volta_a_traduzido()
    {
        await Translated();
        SheetEditor.Review(Store, Game1);
        await SheetFetcher.FetchAsync(Catalog(Game(description: "A new synopsis. [spoiler]He dies.[/spoiler]")), Store, Game1, cancellationToken: Ct);
        var synopsis = Store.LoadGame(Game1)!.Synopsis!;
        Assert.Equal((TranslationStatus.Translated, true), (synopsis.Status, synopsis.Outdated));

        var result = SheetEditor.Approve(Store, Game1);

        Assert.Equal(10, result.Changed);
        Assert.Equal(TranslationStatus.Translated, Store.LoadGame(Game1)!.Synopsis!.Status);
        Assert.Throws<LibraryException>(() => SheetEditor.Approve(Store, Game1, [SheetField.Synopsis]));
    }

    [Fact]
    public async Task Sem_traducao_a_ficha_mostra_o_original_marcado_como_nao_traduzido()
    {
        await SheetFetcher.FetchAsync(Catalog(), Store, Game1, cancellationToken: Ct);

        var card = Card();

        Assert.Equal("A rainy town. [spoiler escondido] Kenta returns.", card.Synopsis!.Text);
        Assert.False(card.Synopsis.Translated);
        Assert.Equal("Comedy", card.Tags[0].Name);
        Assert.Equal(new SheetStatusCounts(11, 0, 0, 0, 0), card.Counts);
    }

    [Fact]
    public async Task A_duracao_sai_em_portugues()
    {
        await SheetFetcher.FetchAsync(Catalog(), Store, Game1, cancellationToken: Ct);

        Assert.Equal("cerca de 25 h", Card().Length);
        Assert.Equal("curta (2 a 10 h)", SheetLabels.Length(null, 2));
        Assert.Equal("45 min", SheetLabels.Length(45, null));
    }

    [Theory]
    [InlineData("synopsis", SheetFieldKind.Synopsis, null)]
    [InlineData("sinopse", SheetFieldKind.Synopsis, null)]
    [InlineData("c12", SheetFieldKind.Character, "c12")]
    [InlineData("character:c12", SheetFieldKind.Character, "c12")]
    [InlineData("TAG:G7", SheetFieldKind.Tag, "g7")]
    [InlineData("i34", SheetFieldKind.Trait, "i34")]
    [InlineData("trait:i34", SheetFieldKind.Trait, "i34")]
    public void O_texto_da_ficha_se_le_pelo_nome(string text, SheetFieldKind kind, string? id) =>
        Assert.Equal(new SheetField(kind, id), SheetField.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("v17")]
    [InlineData("tag:c1")]
    [InlineData("personagem")]
    public void Nome_que_nao_e_de_um_texto_da_ficha_recusa_em_portugues(string text) =>
        Assert.Throws<LibraryException>(() => SheetField.Parse(text));
}
