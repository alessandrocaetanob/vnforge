using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;
using static VNForge.Core.Tests.Factsheet.SheetSamples;

namespace VNForge.Core.Tests.Factsheet;

/// <summary>A ficha na tela (d9, #554): spoiler por nível, rótulos, blocos da revisão, tradução de um bloco e a leitura dos arquivos.</summary>
public sealed class SheetPageAndBlocksTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public SheetPageAndBlocksTests() => Store = new SheetStore(_folder.Path);

    private SheetStore Store { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    private async Task Translated(VndbVisualNovel? game = null)
    {
        await SheetFetcher.FetchAsync(Catalog(game ?? Game() with { Rating = 79 }), Store, Game1, cancellationToken: Ct);
        await new SheetTranslationRunner(new StubSheetTranslator(), Store).RunAsync(Game1, cancellationToken: Ct);
    }

    private GameSheetCard Card(SheetVisibility visibility)
    {
        var game = Store.LoadGame(Game1)!;
        return SheetViewer.Card(game, Store.LoadCharacters(game.CharacterIds), Store.LoadTerms(), visibility);
    }

    private IReadOnlyList<SheetBlock> Blocks(SheetVisibility visibility) => SheetBlocks.Build(Store, Game1, visibility);

    [Fact]
    public void A_visibilidade_limita_o_nivel_de_spoiler_e_converte_de_e_para_a_privacidade_da_Biblioteca()
    {
        Assert.Equal(0, new SheetVisibility(-3).SpoilerLevel);
        Assert.Equal(2, new SheetVisibility(9).SpoilerLevel);
        Assert.Equal(new SheetVisibility(2, AdultMode.Show), SheetVisibility.From(new LibraryPrivacy(ShowSpoilers: true, Adult: AdultMode.Show)));
        Assert.Equal(new SheetVisibility(), SheetVisibility.From(new LibraryPrivacy()));
        Assert.False(new SheetVisibility(1).ShowsSpoilers);
        Assert.True(new SheetVisibility(2).ShowsSpoilers);
        Assert.Equal(new LibraryPrivacy(true, AdultMode.Show), new SheetVisibility(2, AdultMode.Show).ToPrivacy());
        Assert.Equal(["Escondido", "Escondido", "Leve", "Tudo"], new[] { -1, 0, 1, 2 }.Select(SheetVisibility.LevelName));
    }

    [Fact]
    public async Task No_nivel_leve_aparece_o_spoiler_pequeno_mas_nao_o_grande_nem_os_trechos_dos_textos()
    {
        await Translated();

        var card = Card(new SheetVisibility(1));

        Assert.Equal(["Kenta Aizawa", "Aoi Sakura"], card.Characters.Select(c => c.Name));
        Assert.Equal([new SheetHiddenCharacter("c3", 2)], card.HiddenCharacterCards);
        var kenta = card.Characters[0];
        Assert.Equal(["PT: Long Hair", "PT: Amnesia"], kenta.Traits.Select(t => t.Name));
        Assert.Equal([0, 1], kenta.Traits.Select(t => t.Spoiler));
        Assert.Equal(2, kenta.HiddenTraits);
        Assert.Equal("PT: The hero. [spoiler escondido]", kenta.Description!.Text);
        Assert.Null(kenta.RealGender);
        Assert.True(card.Synopsis!.HasHiddenSpoilers);
        Assert.Equal(["PT: Comedy"], card.Tags.Select(t => t.Name));
    }

    [Fact]
    public async Task No_nivel_maximo_aparecem_a_mentira_o_genero_real_e_o_personagem_de_spoiler_grande()
    {
        await Translated();

        var card = Card(new SheetVisibility(2));

        Assert.Equal(["Kenta Aizawa", "Aoi Sakura", "Ghost"], card.Characters.Select(c => c.Name));
        Assert.Empty(card.HiddenCharacterCards);
        Assert.Contains(card.Characters[0].Traits, t => t.Lie);
        Assert.Equal("masculino", card.Characters[1].RealGender);
        Assert.Equal(["PT: Comedy", "PT: Plot Twist"], card.Tags.Select(t => t.Name));
    }

    [Fact]
    public async Task As_tags_escondidas_se_separam_por_spoiler_e_por_conteudo_adulto()
    {
        await Translated();

        var hidden = Card(new SheetVisibility());
        var adult = Card(new SheetVisibility(2, AdultMode.Show));

        Assert.Equal((2, 1, 1), (hidden.HiddenTags, hidden.HiddenSpoilerTags, hidden.HiddenAdultTags));
        Assert.Equal((0, 0, 0), (adult.HiddenTags, adult.HiddenSpoilerTags, adult.HiddenAdultTags));
    }

    [Fact]
    public async Task O_cartao_traz_a_etapa_mais_atrasada_do_personagem_e_das_tags_e_os_dados_do_jogo()
    {
        await Translated();
        SheetEditor.Review(Store, Game1, [SheetField.OfCharacter("c1"), SheetField.OfTag("g10")]);
        SheetEditor.Approve(Store, Game1, [SheetField.OfCharacter("c1"), SheetField.OfTag("g10")]);

        var card = Card(new SheetVisibility());

        Assert.Equal(TranslationStatus.Machine, card.Characters[0].Status);
        Assert.Equal(TranslationStatus.Approved, card.TagsStatus);
        Assert.Equal("Média · 25 h (42 votos)", card.LengthDetail);
        Assert.Equal("2019, em japonês", card.Released);
        Assert.Equal("7,9 de 10", card.Rating);
    }

    [Fact]
    public async Task O_personagem_sem_descricao_e_sem_traco_visivel_fica_sem_etapa()
    {
        await SheetFetcher.FetchAsync(Catalog(), Store, Game1, cancellationToken: Ct);
        Store.SaveCharacter(Store.LoadCharacter("c3")! with { Description = null });

        var card = Card(new SheetVisibility(2));

        Assert.Null(card.Characters.Single(c => c.Id == "c3").Status);
        Assert.Null(Card(new SheetVisibility(2)).Characters.Single(c => c.Id == "c3").Description);
    }

    [Fact]
    public async Task Ver_o_original_mostra_o_texto_do_VNDB_com_a_mesma_regra_de_spoiler()
    {
        await Translated();
        var synopsis = Store.LoadGame(Game1)!.Synopsis!;

        var hidden = SheetViewer.ShownSource(synopsis, new SheetVisibility(1));
        var all = SheetViewer.ShownSource(synopsis, new SheetVisibility(2));

        Assert.Equal("A rainy town. [spoiler escondido] Kenta returns.", hidden.Text);
        Assert.True(hidden.HasHiddenSpoilers);
        Assert.False(hidden.Translated);
        Assert.Equal("A rainy town. He dies. Kenta returns.", all.Text);
        Assert.Equal(TranslationStatus.Machine, all.Status);
    }

    [Fact]
    public async Task Buscar_a_ficha_guarda_o_lancamento_a_lingua_e_a_nota()
    {
        await Translated();

        var game = Store.LoadGame(Game1)!;

        Assert.Equal(("2019-05-31", "ja", 79d), (game.Released, game.OriginalLanguage, game.Rating));
    }

    [Fact]
    public void Uma_ficha_gravada_antes_dos_novos_campos_abre_com_eles_vazios()
    {
        var path = Store.GamePath(Game1);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"version":1,"data":{"visualNovelId":"v900001","title":"Antiga","tags":[],"characterIds":[],"charactersTruncated":false,"fetchedAt":"2026-11-01T00:00:00+00:00","lengthVotes":0}}""");

        var game = Store.LoadGame(Game1)!;

        Assert.Equal(("Antiga", null, null, null), (game.Title, game.Released, game.OriginalLanguage, game.Rating));
    }

    [Theory]
    [InlineData(TranslationStatus.Machine, false, "Traduzido pela IA")]
    [InlineData(TranslationStatus.Machine, true, "Traduzida pela IA")]
    [InlineData(TranslationStatus.Translated, false, "Traduzido")]
    [InlineData(TranslationStatus.Reviewed, true, "Revisada")]
    [InlineData(TranslationStatus.Approved, false, "Aprovado")]
    [InlineData(TranslationStatus.Untranslated, false, "Sem tradução")]
    public void O_selo_da_etapa_concorda_com_o_genero_do_que_ele_marca(TranslationStatus status, bool feminine, string expected) =>
        Assert.Equal(expected, SheetLabels.StatusBadge(status, feminine));

    [Theory]
    [InlineData("main", "masculino", "Protagonista")]
    [InlineData("primary", "feminino", "Principal")]
    [InlineData("side", "feminino", "Secundária")]
    [InlineData("side", "masculino", "Secundário")]
    [InlineData("side", null, "Secundário")]
    [InlineData("appears", "feminino", "Aparece")]
    [InlineData("outro", null, "outro")]
    public void O_papel_do_selo_concorda_com_o_genero_do_personagem(string role, string? gender, string expected) =>
        Assert.Equal(expected, SheetLabels.RoleBadge(role, gender));

    [Theory]
    [InlineData(1500, 3, 47, "Média · 25 h (47 votos)")]
    [InlineData(1080, 3, 1, "Média · 18 h (1 voto)")]
    [InlineData(45, 1, 0, "Muito curta · 45 min")]
    [InlineData(null, 2, 0, "Curta · 2 a 10 h")]
    [InlineData(600, null, 3, "10 h (3 votos)")]
    [InlineData(null, null, 0, "Sem votos")]
    public void A_duracao_traz_a_categoria_as_horas_e_os_votos(int? minutes, int? category, int votes, string expected) =>
        Assert.Equal(expected, SheetLabels.LengthDetail(minutes, category, votes));

    [Theory]
    [InlineData("2019-05-31", "ja", "2019, em japonês")]
    [InlineData("2019", "en", "2019, em inglês")]
    [InlineData("TBA", "ja", "A anunciar, em japonês")]
    [InlineData(null, "ko", "em coreano")]
    [InlineData("2020-01", null, "2020")]
    [InlineData("2020-01", "xx", "2020")]
    [InlineData(null, null, null)]
    [InlineData("quando der", null, null)]
    public void O_lancamento_traz_o_ano_e_a_lingua_original(string? released, string? language, string? expected) =>
        Assert.Equal(expected, SheetLabels.Released(released, language));

    [Theory]
    [InlineData(79d, "7,9 de 10")]
    [InlineData(100d, "10 de 10")]
    [InlineData(0d, null)]
    [InlineData(null, null)]
    public void A_nota_sai_de_zero_a_dez_com_virgula(double? rating, string? expected) => Assert.Equal(expected, VndbRating.Score(rating));

    [Fact]
    public async Task Os_blocos_seguem_a_ordem_da_tela_e_o_nivel_de_spoiler()
    {
        await Translated();

        var hidden = Blocks(new SheetVisibility());
        var all = Blocks(new SheetVisibility(2));

        Assert.Equal(["Sinopse", "Gêneros e tags", "Kenta Aizawa", "Aoi Sakura"], hidden.Select(b => b.Title));
        Assert.Equal([SheetBlockKind.Synopsis, SheetBlockKind.Tags, SheetBlockKind.Character, SheetBlockKind.Character], hidden.Select(b => b.Kind));
        Assert.Equal(["Sinopse", "Gêneros e tags", "Kenta Aizawa", "Aoi Sakura", "Ghost"], all.Select(b => b.Title));
        Assert.Equal(["Gêneros e tags"], hidden[1].Pairs.Select(p => p.Title));
        Assert.Equal(["PT: Comedy"], hidden[1].Pairs[0].Target!.Split(", "));
        Assert.Equal(["Descrição de Kenta Aizawa", "Traços de Kenta Aizawa"], hidden[2].Pairs.Select(p => p.Title));
        Assert.Equal(["Comedy", "Plot Twist"], all[1].Pairs[0].Original.Split(", "));
    }

    [Fact]
    public async Task O_par_de_tracos_vira_uma_lista_com_um_campo_por_traco_visivel()
    {
        await Translated();

        var traits = Blocks(new SheetVisibility(0, AdultMode.Show))[2].Pairs[1];

        Assert.True(traits.IsList);
        Assert.Equal([SheetField.OfTrait("i1"), SheetField.OfTrait("i104")], traits.Fields);
        Assert.Equal("Long Hair, Virgin Sex", traits.Original);
        Assert.Equal("PT: Long Hair, PT: Virgin Sex", traits.Target);
        Assert.Equal(TranslationStatus.Machine, traits.Status);
    }

    [Fact]
    public async Task Traco_com_spoiler_ou_mentira_nao_entra_no_par_de_tracos_mesmo_no_nivel_maximo()
    {
        await Translated();

        var traits = Blocks(new SheetVisibility(2))[2].Pairs[1];

        Assert.Equal("Long Hair", traits.Original);
        Assert.Equal([SheetField.OfTrait("i1")], traits.Fields);
    }

    [Fact]
    public async Task O_bloco_leva_a_etapa_mais_atrasada_dos_pares_e_o_par_o_spoiler_do_original()
    {
        await Translated();
        SheetEditor.Review(Store, Game1, [SheetField.OfCharacter("c1")]);

        var kenta = Blocks(new SheetVisibility())[2];

        Assert.Equal([TranslationStatus.Reviewed, TranslationStatus.Machine], kenta.Pairs.Select(p => p.Status));
        Assert.Equal(TranslationStatus.Machine, kenta.Status);
        Assert.True(kenta.Pairs[0].HasSpoilers);
        Assert.False(kenta.Pairs[1].HasSpoilers);
        Assert.Equal([SheetField.OfCharacter("c1"), SheetField.OfTrait("i1")], kenta.Fields);
    }

    [Fact]
    public async Task Antes_de_traduzir_o_par_nao_tem_traducao_e_esta_sem_traducao()
    {
        await SheetFetcher.FetchAsync(Catalog(), Store, Game1, cancellationToken: Ct);

        var synopsis = Blocks(new SheetVisibility())[0];

        Assert.Equal(TranslationStatus.Untranslated, synopsis.Status);
        Assert.Null(synopsis.Pairs[0].Target);
        Assert.False(synopsis.Pairs[0].HasTarget);
    }

    [Fact]
    public async Task Sem_sinopse_nem_tags_nem_personagens_visiveis_nao_ha_bloco()
    {
        await SheetFetcher.FetchAsync(Catalog(Game(description: null) with { Tags = [] }), Store, Game1, cancellationToken: Ct);
        Store.SaveGame(Store.LoadGame(Game1)! with { CharacterIds = [] });

        Assert.Empty(Blocks(new SheetVisibility(2)));
        Assert.Throws<LibraryException>(() => SheetBlocks.Build(Store, "v1", new SheetVisibility()));
    }

    [Fact]
    public async Task Editar_um_texto_inteiro_grava_como_traduzido_e_exige_os_trechos_de_spoiler()
    {
        await Translated();
        var pair = Blocks(new SheetVisibility())[2].Pairs[0];

        SheetBlocks.EditPair(Store, Game1, pair, "O herói. [spoiler]Fantasma.[/spoiler]");

        var text = Store.LoadCharacter("c1")!.Description!;
        Assert.Equal(("O herói. [spoiler]Fantasma.[/spoiler]", TranslationStatus.Translated), (text.Target, text.Status));
        Assert.Throws<LibraryException>(() => SheetBlocks.EditPair(Store, Game1, pair, "O herói."));
    }

    [Fact]
    public async Task Editar_uma_lista_grava_um_nome_por_item_e_deixa_como_estava_o_que_nao_mudou()
    {
        await Translated();
        SheetEditor.Review(Store, Game1, [SheetField.OfTrait("i104")]);
        var pair = Blocks(new SheetVisibility(0, AdultMode.Show))[2].Pairs[1];

        SheetBlocks.EditPair(Store, Game1, pair, "Cabelo longo,  PT: Virgin Sex");

        var terms = Store.LoadTerms();
        Assert.Equal(("Cabelo longo", TranslationStatus.Translated), (terms.Find(SheetTermKind.Trait, "i1")!.Text.Target, terms.Find(SheetTermKind.Trait, "i1")!.Text.Status));
        Assert.Equal(("PT: Virgin Sex", TranslationStatus.Reviewed), (terms.Find(SheetTermKind.Trait, "i104")!.Text.Target, terms.Find(SheetTermKind.Trait, "i104")!.Text.Status));
    }

    [Fact]
    public async Task Editar_uma_lista_com_o_numero_errado_de_nomes_recusa_e_nao_grava_nada()
    {
        await Translated();
        var pair = Blocks(new SheetVisibility(0, AdultMode.Show))[2].Pairs[1];

        var ex = Assert.Throws<LibraryException>(() => SheetBlocks.EditPair(Store, Game1, pair, "Só um"));

        Assert.Contains("2 nomes", ex.Message, StringComparison.Ordinal);
        Assert.Equal("PT: Long Hair", Store.LoadTerms().Find(SheetTermKind.Trait, "i1")!.Text.Target);
        Assert.Throws<LibraryException>(() => SheetBlocks.EditPair(Store, Game1, pair, " , "));
    }

    [Fact]
    public async Task Uma_lista_de_um_nome_so_tambem_se_edita_como_lista()
    {
        await Translated();
        var tags = Blocks(new SheetVisibility())[1].Pairs[0];

        SheetBlocks.EditPair(Store, Game1, tags, "Comédia");

        Assert.Equal("Comédia", Store.LoadTerms().Find(SheetTermKind.Tag, "g10")!.Text.Target);
        Assert.Throws<LibraryException>(() => SheetBlocks.EditPair(Store, Game1, tags, "Comédia, Drama"));
    }

    [Fact]
    public async Task Traduzir_de_novo_um_bloco_refaz_o_que_a_pessoa_ja_revisou_e_deixa_o_resto()
    {
        await Translated();
        SheetEditor.Review(Store, Game1, [SheetField.OfCharacter("c1"), SheetField.OfCharacter("c2")]);
        var stub = new StubSheetTranslator(item => "Novo: " + item.Source);

        var result = await new SheetTranslationRunner(stub, Store).RetranslateAsync(Game1, [SheetField.OfCharacter("c1"), SheetField.OfTrait("i1")], cancellationToken: Ct);

        Assert.Equal(2, result.Translated);
        Assert.Empty(result.Failures);
        var kenta = Store.LoadCharacter("c1")!.Description!;
        Assert.Equal(("Novo: The hero. [spoiler]He is a ghost.[/spoiler]", TranslationStatus.Machine), (kenta.Target, kenta.Status));
        Assert.Equal(TranslationStatus.Reviewed, Store.LoadCharacter("c2")!.Description!.Status);
        Assert.Equal("Novo: Long Hair", Store.LoadTerms().Find(SheetTermKind.Trait, "i1")!.Text.Target);
        Assert.Equal(["character:c1", "trait:i1"], stub.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task O_pedido_de_um_bloco_leva_o_nome_do_personagem_e_o_grupo_do_traco_como_contexto()
    {
        await Translated();

        var plan = SheetTranslationRunner.PlanFields(Store, Game1, [SheetField.Synopsis, SheetField.OfCharacter("c2"), SheetField.OfTag("g10"), SheetField.OfTrait("i1"), SheetField.Synopsis]);

        Assert.Equal(["synopsis", "character:c2", "tag:g10", "trait:i1"], plan.Items.Select(i => i.Key));
        Assert.Equal([null, "Aoi Sakura", "cont", "Hair"], plan.Items.Select(i => i.Context));
        Assert.Equal("Amaoto no Hakoniwa", plan.GameTitle);
    }

    [Fact]
    public async Task Traduzir_de_novo_um_texto_que_nao_e_da_ficha_recusa()
    {
        await Translated();
        var runner = new SheetTranslationRunner(new StubSheetTranslator(), Store);

        await Assert.ThrowsAsync<LibraryException>(() => runner.RetranslateAsync(Game1, [SheetField.OfCharacter("c99")], cancellationToken: Ct));
        await Assert.ThrowsAsync<LibraryException>(() => runner.RetranslateAsync(Game1, [SheetField.OfTag("g99")], cancellationToken: Ct));
        await Assert.ThrowsAsync<LibraryException>(() => runner.RetranslateAsync("v1", [SheetField.Synopsis], cancellationToken: Ct));
    }

    [Fact]
    public async Task Traduzir_de_novo_nao_grava_se_o_original_mudou_no_meio()
    {
        await Translated();
        var changed = false;
        var stub = new StubSheetTranslator(item =>
        {
            if (!changed)
            {
                changed = true;
                Store.Update(s => s.SaveGame(s.LoadGame(Game1)! with { Synopsis = new SheetText("Outra sinopse.") }));
            }

            return "Novo: " + item.Source;
        });

        var result = await new SheetTranslationRunner(stub, Store).RetranslateAsync(Game1, [SheetField.Synopsis], cancellationToken: Ct);

        Assert.Equal(0, result.Translated);
        Assert.Equal(TranslationStatus.Untranslated, Store.LoadGame(Game1)!.Synopsis!.Status);
    }

    [Fact]
    public void A_pagina_sem_ficha_buscada_nao_existe()
    {
        Assert.Null(SheetPage.Load(_folder.Path, Game1));
        Assert.Throws<ArgumentException>(() => SheetPage.Load(" ", Game1));
        Assert.Throws<ArgumentException>(() => SheetPage.Load(_folder.Path, ""));
    }

    [Fact]
    public async Task A_pagina_traz_a_privacidade_e_as_tags_do_usuario_do_jogo_da_Biblioteca()
    {
        await Translated();
        var game = new LibraryGame("g1", "C:\\Jogos\\Amaoto", null, LibraryEngine.Unknown, "Amaoto", null, DateTimeOffset.UnixEpoch) { VndbId = Game1 };
        new LibraryFile(_folder.Path).Save(new GameLibrary([game], new LibraryPrivacy(ShowSpoilers: true)));
        new UserTagsFile(_folder.Path).Update(tags => tags.Add("g1", "favorito"));

        var page = SheetPage.Load(_folder.Path, Game1)!;

        Assert.Equal(Game1, page.Game.VisualNovelId);
        Assert.Equal(["c1", "c2", "c3"], page.Characters.Select(c => c.Id).Order(StringComparer.Ordinal));
        Assert.True(page.Privacy.ShowSpoilers);
        Assert.Equal(["favorito"], page.UserTags);
        Assert.NotNull(page.Terms.Find(SheetTermKind.Tag, "g10"));
    }

    [Fact]
    public async Task A_pagina_sem_o_jogo_na_Biblioteca_esconde_o_spoiler_e_nao_tem_tags_suas()
    {
        await Translated();

        var page = SheetPage.Load(_folder.Path, Game1)!;

        Assert.Equal(new LibraryPrivacy(), page.Privacy);
        Assert.Empty(page.UserTags);
    }

    [Fact]
    public async Task A_biblioteca_ilegivel_nao_impede_a_ficha_que_abre_com_a_privacidade_padrao()
    {
        await Translated();
        await File.WriteAllTextAsync(new LibraryFile(_folder.Path).Path, "{ não é json", Ct);

        var page = SheetPage.Load(_folder.Path, Game1)!;

        Assert.Equal(new LibraryPrivacy(), page.Privacy);
        Assert.Empty(page.UserTags);
    }

    [Fact]
    public async Task A_ficha_ilegivel_recusa_em_vez_de_abrir_vazia()
    {
        await Translated();
        await File.WriteAllTextAsync(Store.GamePath(Game1), "{ não é json", Ct);

        Assert.Throws<LibraryException>(() => SheetPage.Load(_folder.Path, Game1));
    }

    [Fact]
    public void A_contagem_por_etapa_aceita_as_etapas_ja_conhecidas()
    {
        var counts = SheetStatusCounts.OfStatuses([TranslationStatus.Machine, TranslationStatus.Approved, TranslationStatus.Machine, TranslationStatus.Untranslated]);

        Assert.Equal(new SheetStatusCounts(1, 2, 0, 0, 1), counts);
    }
}
