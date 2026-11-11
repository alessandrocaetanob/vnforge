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
    private static readonly LibraryPrivacy All = new(ShowSpoilers: true, Adult: AdultMode.Show);

    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    private async Task<(GameLibrary Library, SheetStore Store)> PrepareAsync() => (OwnedLibrary(), await FilledAsync(_dir.Path));

    private static LibraryCharacterList List(GameLibrary library, SheetStore store, LibraryPrivacy privacy, GameEditions? editions = null, LibraryCharacterFilter? filter = null) =>
        LibraryCharacters.List(library, store.LoadAllCharacters(), store.LoadTerms(), editions ?? new GameEditions(), privacy, filter);

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
    public async Task O_contador_de_escondidos_e_o_do_filtro_e_nao_o_da_biblioteca()
    {
        var (library, store) = await PrepareAsync();

        Assert.Equal(2, List(library, store, Hidden).Hidden);
        Assert.Equal(1, List(library, store, Hidden, filter: new LibraryCharacterFilter(Traits: ["i1"])).Hidden);
        Assert.Equal(0, List(library, store, Hidden, filter: new LibraryCharacterFilter(Text: "hana")).Hidden);
        Assert.Equal(0, List(library, store, Hidden, filter: new LibraryCharacterFilter(Text: "zzz")).Hidden);
        Assert.Equal(1, List(library, store, Hidden, filter: new LibraryCharacterFilter(GameId: "g2")).Hidden);
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

        var blurred = Assert.Single(List(library, store, Hidden).Characters, c => c.Id == "c101");
        var hidden = Assert.Single(List(library, store, new LibraryPrivacy(Adult: AdultMode.Hide)).Characters, c => c.Id == "c101");
        var shown = Assert.Single(List(library, store, new LibraryPrivacy(Adult: AdultMode.Show)).Characters, c => c.Id == "c101");

        Assert.True(blurred.ImageHidden);
        Assert.NotNull(blurred.Image);
        Assert.True(hidden.ImageHidden);
        Assert.Null(hidden.Image);
        Assert.False(shown.ImageHidden);
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
}
