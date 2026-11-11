using VNForge.Core.Library;

namespace VNForge.Core.Tests.Library;

public sealed class LibraryTagFilterTests
{
    private static readonly LibraryPrivacy Hidden = new();

    private static readonly LibraryTag Drama = new("g1", "Drama", "cont", 2.9, 0);
    private static readonly LibraryTag Twist = new("g2", "Plot Twist", "cont", 2.0, 2);
    private static readonly LibraryTag Explicit = new("g3", "Cena Explícita", "ero", 2.8, 0);

    private readonly LibraryGame _clannad = TagTestGames.WithVndb("CLANNAD", false, Drama, Twist);
    private readonly LibraryGame _eroge = TagTestGames.WithVndb("Eroge", true, Drama, Explicit);
    private readonly LibraryGame _loose = TagTestGames.Plain("Sem VNDB");

    private (UserTags Tags, LibraryGame[] Games) Setup()
    {
        var tags = new UserTags();
        tags.Add(_clannad.Id, "favorito");
        tags.Add(_clannad.Id, "rejogar");
        tags.Add(_eroge.Id, "favorito");
        tags.Add(_loose.Id, "pra traduzir");
        return (tags, [_clannad, _eroge, _loose]);
    }

    private static string[] Titles(IEnumerable<LibraryGame> games) => [.. games.Select(g => g.Title).Order(StringComparer.Ordinal)];

    private static string[] Run(LibraryTagFilter filter, UserTags tags, LibraryGame[] games, LibraryPrivacy? privacy = null) =>
        Titles(filter.Apply(games, tags, privacy ?? Hidden));

    [Fact]
    public void Filtro_vazio_deixa_todos_passarem()
    {
        var (tags, games) = Setup();

        Assert.Equal(["CLANNAD", "Eroge", "Sem VNDB"], Run(LibraryTagFilter.Empty, tags, games));
    }

    [Fact]
    public void E_exige_todas_as_tags_e_junta_as_proprias_com_as_do_vndb()
    {
        var (tags, games) = Setup();

        Assert.Equal(["CLANNAD", "Eroge"], Run(LibraryTagFilter.Create(["favorito"], any: false, null), tags, games));
        Assert.Equal(["CLANNAD", "Eroge"], Run(LibraryTagFilter.Create(["favorito", "drama"], any: false, null), tags, games));
        Assert.Equal(["CLANNAD"], Run(LibraryTagFilter.Create(["favorito", "drama", "rejogar"], any: false, null), tags, games));
    }

    [Fact]
    public void Ou_aceita_qualquer_uma_das_tags()
    {
        var (tags, games) = Setup();

        var filter = LibraryTagFilter.Create(["rejogar", "pra traduzir"], any: true, null);

        Assert.Equal(["CLANNAD", "Sem VNDB"], Run(filter, tags, games));
    }

    [Fact]
    public void Exclusao_tira_o_jogo_que_tem_a_tag_e_funciona_sozinha()
    {
        var (tags, games) = Setup();

        var withoutExplicit = LibraryTagFilter.Create(["favorito"], any: false, ["Cena Explícita", "x"]);
        Assert.Equal(["CLANNAD"], Run(withoutExplicit, tags, games, new LibraryPrivacy(Adult: AdultMode.Show)));
        Assert.Equal(["CLANNAD", "Eroge"], Run(withoutExplicit, tags, games));
        Assert.Equal(["Sem VNDB"], Run(LibraryTagFilter.Create(null, any: false, ["favorito"]), tags, games));
        Assert.Equal(["Eroge", "Sem VNDB"], Run(LibraryTagFilter.Create(null, any: false, ["rejogar"]), tags, games));
    }

    [Fact]
    public void Prefixo_limita_o_termo_a_tags_proprias_ou_do_vndb()
    {
        var (tags, games) = Setup();
        tags.Add(_loose.Id, "drama");

        Assert.Equal(["CLANNAD", "Eroge", "Sem VNDB"], Run(LibraryTagFilter.Create(["drama"], any: false, null), tags, games));
        Assert.Equal(["Sem VNDB"], Run(LibraryTagFilter.Create(["minha:drama"], any: false, null), tags, games));
        Assert.Equal(["CLANNAD", "Eroge"], Run(LibraryTagFilter.Create(["vndb:drama"], any: false, null), tags, games));
        Assert.Equal(["CLANNAD", "Eroge"], Run(LibraryTagFilter.Create(["vndb:g1"], any: false, null), tags, games));
    }

    [Fact]
    public void Termo_ignora_caixa_e_espacos_repetidos()
    {
        var (tags, games) = Setup();

        var filter = LibraryTagFilter.Create(["  PRA   Traduzir"], any: false, null);

        Assert.Equal(["pra traduzir"], filter.Include);
        Assert.Equal(["Sem VNDB"], Run(filter, tags, games));
    }

    [Fact]
    public void Tag_de_spoiler_escondida_nao_aparece_no_filtro()
    {
        var (tags, games) = Setup();
        var filter = LibraryTagFilter.Create(["vndb:plot twist"], any: false, null);

        Assert.Empty(Run(filter, tags, games));
        Assert.Equal(["CLANNAD"], Run(filter, tags, games, new LibraryPrivacy(ShowSpoilers: true)));
        Assert.Equal(["CLANNAD", "Eroge", "Sem VNDB"], Run(LibraryTagFilter.Create(null, any: false, ["vndb:plot twist"]), tags, games));
    }

    [Fact]
    public void Tag_adulta_so_entra_com_a_privacidade_liberando()
    {
        var (tags, games) = Setup();
        var filter = LibraryTagFilter.Create(["vndb:cena explícita"], any: false, null);

        Assert.Empty(Run(filter, tags, games));
        Assert.Equal(["Eroge"], Run(filter, tags, games, new LibraryPrivacy(Adult: AdultMode.Show)));
    }

    [Fact]
    public void Tag_propria_nao_depende_da_privacidade()
    {
        var (tags, games) = Setup();
        tags.Add(_eroge.Id, "segredo");

        Assert.Equal(["Eroge"], Run(LibraryTagFilter.Create(["segredo"], any: false, null), tags, games));
    }

    [Fact]
    public void Termo_vazio_e_recusado()
    {
        Assert.Throws<LibraryException>(() => LibraryTagFilter.Create(["  "], any: false, null));
        Assert.Throws<LibraryException>(() => LibraryTagFilter.Create(null, any: false, ["minha:"]));
    }

    [Fact]
    public void Tag_erotica_de_jogo_nao_marcado_como_adulto_tambem_some_sem_show_adult()
    {
        var plain = TagTestGames.WithVndb("Comum", false, Drama, Explicit);
        var tags = new UserTags();
        var filter = LibraryTagFilter.Create(["vndb:cena explícita"], any: false, null);

        Assert.Equal([Drama], LibraryVisibility.VisibleTags(plain, Hidden));
        Assert.Empty(filter.Apply([plain], tags, Hidden));
        Assert.Single(filter.Apply([plain], tags, new LibraryPrivacy(Adult: AdultMode.Show)));
        Assert.Equal(LibraryVisibility.Card(plain, Hidden)!.Tags, LibraryVisibility.VisibleTags(plain, Hidden));
    }

    [Fact]
    public void Termo_do_vndb_ignora_largura_e_forma_de_composicao_do_unicode()
    {
        var cafe = new LibraryTag("g7", "Café", "cont", 2.0, 0);
        var game = TagTestGames.WithVndb("Jogo", false, cafe);
        var tags = new UserTags();
        tags.Add(game.Id, "Ｆａｖｏｒｉｔｏ");

        Assert.Single(LibraryTagFilter.Create(["vndb:café"], any: false, null).Apply([game], tags, Hidden));
        Assert.Single(LibraryTagFilter.Create(["ｖｎｄｂ：Ｃａｆé"], any: false, null).Apply([game], tags, Hidden));
        Assert.Single(LibraryTagFilter.Create(["favorito"], any: false, null).Apply([game], tags, Hidden));
        Assert.Equal(["vndb:café"], LibraryTagFilter.Create(["vndb:café"], any: false, null).Include);
    }

    [Fact]
    public void Mascarar_troca_so_os_termos_do_vndb_que_a_privacidade_esconde()
    {
        var (tags, games) = Setup();
        var filter = LibraryTagFilter.Create(["vndb:plot twist", "drama", "favorito", "cena explícita"], any: false, ["vndb:g3", "vndb:xyz"]);

        var masked = filter.Masked(games, tags, Hidden, out var count);
        var revealed = filter.Masked(games, tags, new LibraryPrivacy(ShowSpoilers: true, Adult: AdultMode.Show), out var none);

        Assert.Equal(3, count);
        Assert.Equal([LibraryTagFilter.HiddenTerm, "drama", "favorito", LibraryTagFilter.HiddenTerm], masked.Include);
        Assert.Equal([LibraryTagFilter.HiddenTerm, "vndb:xyz"], masked.Exclude);
        Assert.Equal(0, none);
        Assert.Equal(filter.Include, revealed.Include);
        Assert.Equal(filter.Exclude, revealed.Exclude);
    }

    [Fact]
    public void Termos_repetidos_viram_um()
    {
        var filter = LibraryTagFilter.Create(["Drama", "drama ", "minha:drama"], any: false, null);

        Assert.Equal(["drama", "minha:drama"], filter.Include);
    }
}
