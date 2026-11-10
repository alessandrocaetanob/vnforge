using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Vndb;
using static VNForge.Core.Tests.Library.LibraryCharactersSamples;

namespace VNForge.Core.Tests.Library;

/// <summary>#558: o registro de personagens da Biblioteca, com o catálogo falso do VNDB.</summary>
public sealed class LibraryCharactersTests : IDisposable
{
    private static readonly LibraryPrivacy Hidden = new();
    private static readonly LibraryPrivacy All = new(ShowSpoilers: true, ShowAdult: true);

    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    private async Task<(GameLibrary Library, SheetStore Store)> PrepareAsync() => (OwnedLibrary(), await FilledAsync(_dir.Path));

    private static LibraryCharacterList List(GameLibrary library, SheetStore store, LibraryPrivacy privacy, GameEditions? editions = null, LibraryCharacterFilter? filter = null) =>
        LibraryCharacters.List(library, store.LoadAllCharacters(), store.LoadTerms(), editions ?? new GameEditions(), privacy, filter);

    [Fact]
    public async Task O_mesmo_personagem_em_dois_jogos_e_um_registro_so_com_o_papel_de_cada_jogo()
    {
        var (library, store) = await PrepareAsync();

        var hana = Assert.Single(List(library, store, All).Characters, c => c.Id == "c101");

        Assert.Equal(["g1", "g2"], hana.AppearsIn.Select(a => a.GameId));
        Assert.Equal([("protagonista", 0), ("secundário", 1)], hana.AppearsIn.Select(a => (a.Role, a.Spoiler)));
        Assert.Equal(["Moon Flower", "Star Garden"], hana.AppearsIn.Select(a => a.GameTitle));
    }

    [Fact]
    public async Task Jogo_fora_da_biblioteca_nao_entra_no_aparece_em_e_personagem_so_de_la_nao_entra_na_lista()
    {
        var (library, store) = await PrepareAsync();

        var list = List(library, store, All);

        Assert.DoesNotContain(list.Characters.SelectMany(c => c.AppearsIn), a => a.VisualNovelId == Outside);
        Assert.DoesNotContain(list.Characters, c => c.Id == "c103");
        Assert.Equal(["c101", "c102", "c104"], list.Characters.Select(c => c.Id).Order());
    }

    [Fact]
    public async Task Duas_pastas_do_mesmo_jogo_sao_dois_itens_cada_um_com_a_sua_edicao()
    {
        var (_, store) = await PrepareAsync();
        var library = new GameLibrary([Owned("g1", "Moon Flower", Moon, "Moon Flower"), Owned("g9", "Moon Flower Deluxe", Moon, "Moon Flower Deluxe")]);
        var editions = new GameEditions();
        editions.Set("g1", Moon, ["r1"], Now);
        editions.Set("g9", Moon, ["r2"], Now);

        var mika = Assert.Single(List(library, store, All, editions).Characters, c => c.Id == "c102");

        Assert.Equal([("g1", "main", 2), ("g9", "side", 2)], mika.AppearsIn.Select(a => (a.GameId, a.RoleCode, a.Spoiler)));
        Assert.Equal([["r1"], ["r2"]], mika.AppearsIn.Select(a => a.Editions));
    }

    [Fact]
    public void Edicao_casada_decide_o_papel_mas_o_spoiler_e_sempre_o_maior()
    {
        var appearance = Mika().Appearances;
        var sheet = new SheetAppearance(Moon, "main", 0, [.. appearance.Select(a => new SheetAppearanceEdition(a.ReleaseId, a.Role, a.Spoiler))]);

        var first = LibraryCharacters.Resolve(sheet, ["r1"]);
        var second = LibraryCharacters.Resolve(sheet, ["r2"]);

        Assert.Equal(("main", 2), (first.Role, first.Spoiler));
        Assert.Equal(["r1"], first.Editions);
        Assert.Equal(("side", 2), (second.Role, second.Spoiler));
        Assert.Equal(["r2"], second.Editions);
    }

    [Fact]
    public void Sem_edicao_casada_vale_o_papel_mais_importante_e_o_maior_spoiler()
    {
        var sheet = new SheetAppearance(
            Moon, "main", 0, [new SheetAppearanceEdition("r1", "side", 0), new SheetAppearanceEdition("r2", "primary", 2), new SheetAppearanceEdition(null, "appears", 1)]);

        var (role, spoiler, used) = LibraryCharacters.Resolve(sheet, []);

        Assert.Equal(("primary", 2), (role, spoiler));
        Assert.Empty(used);
    }

    [Fact]
    public void Edicao_casada_que_o_VNDB_nao_cita_usa_a_entrada_de_toda_edicao_e_sem_ela_o_pior_caso()
    {
        var withDefault = new SheetAppearance(Moon, "main", 0, [new SheetAppearanceEdition("r1", "main", 0), new SheetAppearanceEdition(null, "side", 1)]);
        var withoutDefault = new SheetAppearance(Moon, "main", 0, [new SheetAppearanceEdition("r1", "main", 0), new SheetAppearanceEdition("r2", "side", 2)]);

        var fallback = LibraryCharacters.Resolve(withDefault, ["r9"]);
        var worst = LibraryCharacters.Resolve(withoutDefault, ["r9"]);

        Assert.Equal(("side", 1), (fallback.Role, fallback.Spoiler));
        Assert.Equal(("main", 2), (worst.Role, worst.Spoiler));
    }

    [Fact]
    public void Ficha_antiga_sem_as_edicoes_tem_papel_do_resumo_e_spoiler_no_pior_caso()
    {
        var (role, spoiler, used) = LibraryCharacters.Resolve(new SheetAppearance(Moon, "side", 0), ["r1"]);

        Assert.Equal(("side", 2), (role, spoiler));
        Assert.Empty(used);
    }

    [Fact]
    public async Task Personagem_de_ficha_antiga_sem_as_edicoes_fica_escondido_ate_a_ficha_ser_buscada_de_novo()
    {
        var (library, store) = await PrepareAsync();
        store.Update(s =>
        {
            var hana = s.LoadCharacter("c101")!;
            s.SaveCharacter(hana with { Appearances = [.. hana.Appearances.Select(a => a with { Spoiler = 0, Editions = null })] });
        });

        var hidden = List(library, store, Hidden);
        var shown = List(library, store, All);

        Assert.DoesNotContain(hidden.Characters, c => c.Id == "c101");
        Assert.Contains(shown.Characters, c => c.Id == "c101");
    }

    [Fact]
    public void Edicoes_casadas_empatadas_valem_o_melhor_papel_e_o_maior_spoiler_entre_elas()
    {
        var sheet = new SheetAppearance(Moon, "main", 0, [new SheetAppearanceEdition("r1", "side", 0), new SheetAppearanceEdition("r2", "primary", 2), new SheetAppearanceEdition("r3", "main", 0)]);

        var (role, spoiler, used) = LibraryCharacters.Resolve(sheet, ["r1", "r2"]);

        Assert.Equal(("primary", 2), (role, spoiler));
        Assert.Equal(["r1", "r2"], used);
    }

    [Fact]
    public async Task Spoiler_esconde_o_jogo_do_aparece_em_e_o_personagem_quando_todos_os_jogos_passam_do_nivel()
    {
        var (library, store) = await PrepareAsync();

        var list = List(library, store, Hidden);

        var hana = Assert.Single(list.Characters, c => c.Id == "c101");
        Assert.Equal(["g1"], hana.AppearsIn.Select(a => a.GameId));
        Assert.Equal(1, hana.HiddenAppearances);
        Assert.Equal(["c101"], list.Characters.Select(c => c.Id));
        Assert.Equal(2, list.Hidden);
    }

    [Fact]
    public async Task Edicao_casada_de_spoiler_baixo_nao_baixa_o_spoiler_que_vale_para_esconder()
    {
        var (_, store) = await PrepareAsync();
        var library = new GameLibrary([Owned("g1", "Moon Flower", Moon, "Moon Flower")]);
        var risky = new GameEditions();
        risky.Set("g1", Moon, ["r2"], Now);
        var safe = new GameEditions();
        safe.Set("g1", Moon, ["r1"], Now);

        Assert.DoesNotContain(List(library, store, Hidden, risky).Characters, c => c.Id == "c102");
        Assert.DoesNotContain(List(library, store, Hidden, safe).Characters, c => c.Id == "c102");
        Assert.Contains(List(library, store, All, risky).Characters, c => c.Id == "c102");
        Assert.Contains(List(library, store, All, safe).Characters, c => c.Id == "c102");
    }

    [Fact]
    public async Task Descricao_e_tracos_em_portugues_vem_da_ficha_e_traco_de_spoiler_some()
    {
        var (library, store) = await PrepareAsync();
        store.Update(s =>
        {
            var hana = s.LoadCharacter("c101")!;
            s.SaveCharacter(hana with { Description = hana.Description!.TranslatedByAi("Uma florista. [spoiler]Ela é um fantasma.[/spoiler]") });
            var terms = s.LoadTerms();
            terms.Replace(SheetTermKind.Trait, "i1", terms.Find(SheetTermKind.Trait, "i1")!.Text.TranslatedByAi("Cabelo longo"));
            s.SaveTerms(terms);
        });

        var card = Assert.Single(List(library, store, Hidden).Characters, c => c.Id == "c101");
        var open = Assert.Single(List(library, store, All).Characters, c => c.Id == "c101");

        Assert.Equal("Uma florista. " + LibraryVisibility.SpoilerPlaceholder, card.Description!.Text);
        Assert.True(card.Description.Translated);
        Assert.Equal(["Cabelo longo"], card.Traits.Select(t => t.Name));
        Assert.Equal(1, card.HiddenTraits);
        Assert.Equal(["Cabelo longo", "Ghost"], open.Traits.Select(t => t.Name));
        Assert.Contains("Ela é um fantasma", open.Description!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lista_vem_em_ordem_de_nome()
    {
        var (library, store) = await PrepareAsync();

        Assert.Equal(["Hana Tsukino", "Mika Aoba", "Secret Person"], List(library, store, All).Characters.Select(c => c.Name));
    }

    [Theory]
    [InlineData("hana", new[] { "c101" })]
    [InlineData("月野", new[] { "c101" })]
    [InlineData("hanako", new[] { "c101" })]
    [InlineData("star garden", new[] { "c101", "c104" })]
    [InlineData("zzz", new string[0])]
    public async Task Busca_por_nome_original_apelido_e_titulo_do_jogo(string text, string[] expected)
    {
        var (library, store) = await PrepareAsync();

        var found = List(library, store, All, filter: new LibraryCharacterFilter(Text: text));

        Assert.Equal(expected, found.Characters.Select(c => c.Id));
    }

    [Fact]
    public async Task Filtro_por_traco_aceita_id_ou_pedaco_do_nome_em_portugues_ou_no_original_e_combina_com_E_ou_OU()
    {
        var (library, store) = await PrepareAsync();
        store.Update(s =>
        {
            var terms = s.LoadTerms();
            terms.Replace(SheetTermKind.Trait, "i1", terms.Find(SheetTermKind.Trait, "i1")!.Text.TranslatedByAi("Cabelo longo"));
            s.SaveTerms(terms);
        });

        IReadOnlyList<string> Ids(params string[] traits) => [.. List(library, store, All, filter: new LibraryCharacterFilter(Traits: traits)).Characters.Select(c => c.Id)];
        IReadOnlyList<string> AnyIds(params string[] traits) => [.. List(library, store, All, filter: new LibraryCharacterFilter(Traits: traits, AnyTrait: true)).Characters.Select(c => c.Id)];

        Assert.Equal(["c101", "c102"], Ids("i1"));
        Assert.Equal(["c101", "c102"], Ids("cabelo"));
        Assert.Equal(["c101", "c102"], Ids("long hair"));
        Assert.Equal(["c101"], Ids("cabelo", "ghost"));
        Assert.Equal(["c101", "c102"], AnyIds("ghost", "i1"));
        Assert.Empty(Ids("nao-existe"));
    }

    [Fact]
    public async Task Filtro_por_traco_nao_revela_traco_escondido_por_spoiler()
    {
        var (library, store) = await PrepareAsync();

        var found = List(library, store, Hidden, filter: new LibraryCharacterFilter(Traits: ["ghost"]));

        Assert.Empty(found.Characters);
    }

    [Fact]
    public async Task O_contador_de_escondidos_e_o_do_filtro_e_nao_o_da_biblioteca()
    {
        var (library, store) = await PrepareAsync();

        Assert.Equal(2, List(library, store, Hidden).Hidden);
        Assert.Equal(1, List(library, store, Hidden, filter: new LibraryCharacterFilter(Text: "secret")).Hidden);
        Assert.Equal(0, List(library, store, Hidden, filter: new LibraryCharacterFilter(Text: "hana")).Hidden);
        Assert.Equal(0, List(library, store, Hidden, filter: new LibraryCharacterFilter(Text: "zzz")).Hidden);
        Assert.Equal(1, List(library, store, Hidden, filter: new LibraryCharacterFilter(GameId: "g2")).Hidden);
    }

    [Fact]
    public async Task Filtro_por_jogo_limita_aos_personagens_dele()
    {
        var (library, store) = await PrepareAsync();

        var found = List(library, store, All, filter: new LibraryCharacterFilter(GameId: "g2"));

        Assert.Equal(["c101", "c104"], found.Characters.Select(c => c.Id));
    }

    [Fact]
    public async Task Busca_ou_traco_sem_letras_nem_numeros_e_recusado()
    {
        const string text = "!!!";
        var (library, store) = await PrepareAsync();

        var error = Assert.Throws<LibraryException>(() => List(library, store, All, filter: new LibraryCharacterFilter(Text: text)));

        Assert.Contains("não tem letras nem números", error.Message, StringComparison.Ordinal);
        Assert.Throws<LibraryException>(() => List(library, store, All, filter: new LibraryCharacterFilter(Traits: ["--"])));
    }

    [Fact]
    public async Task Find_devolve_o_cartao_ou_diz_que_o_personagem_esta_escondido_ou_fora()
    {
        var (library, store) = await PrepareAsync();
        var characters = store.LoadAllCharacters();
        var terms = store.LoadTerms();
        var editions = new GameEditions();

        var shown = LibraryCharacters.Find(library, characters, terms, editions, Hidden, "c101");
        var secret = LibraryCharacters.Find(library, characters, terms, editions, Hidden, "C104");
        var secretOpen = LibraryCharacters.Find(library, characters, terms, editions, All, "c104");
        var missing = LibraryCharacters.Find(library, characters, terms, editions, All, "c999");

        Assert.Equal("c101", shown.Card!.Id);
        Assert.False(shown.HiddenBySpoiler);
        Assert.Null(secret.Card);
        Assert.True(secret.HiddenBySpoiler);
        Assert.Equal("c104", secretOpen.Card!.Id);
        Assert.Null(missing.Card);
        Assert.False(missing.HiddenBySpoiler);
    }

    [Fact]
    public async Task Retrato_adulto_fica_escondido_sem_a_privacidade_liberar()
    {
        var (library, store) = await PrepareAsync();
        store.Update(s => s.SaveCharacter(s.LoadCharacter("c101")! with { Image = new VndbImageRef("https://t.vndb.org/ch/1.jpg", Sexual: 1.5) }));

        var hidden = Assert.Single(List(library, store, Hidden).Characters, c => c.Id == "c101");
        var shown = Assert.Single(List(library, store, new LibraryPrivacy(ShowAdult: true)).Characters, c => c.Id == "c101");

        Assert.True(hidden.ImageHidden);
        Assert.Null(hidden.Image);
        Assert.NotNull(shown.Image);
    }

    [Fact]
    public async Task Genero_real_so_sai_com_os_spoilers_liberados()
    {
        var (library, store) = await PrepareAsync();
        store.Update(s => s.SaveCharacter(s.LoadCharacter("c101")! with { Gender = new VndbGender("f", "m") }));

        var hidden = Assert.Single(List(library, store, Hidden).Characters, c => c.Id == "c101");
        var shown = Assert.Single(List(library, store, new LibraryPrivacy(ShowSpoilers: true)).Characters, c => c.Id == "c101");

        Assert.Equal(("feminino", (string?)null), (hidden.Gender, hidden.RealGender));
        Assert.Equal(("feminino", "masculino"), (shown.Gender, shown.RealGender));
    }

    [Fact]
    public async Task Jogo_sem_o_VNDB_confirmado_nao_conta()
    {
        var (_, store) = await PrepareAsync();
        var library = new GameLibrary([new LibraryGame("g5", "C:\\Jogos\\X", null, LibraryEngine.Unknown, "X", null, Now)]);

        Assert.Empty(List(library, store, All).Characters);
    }
}
