using VNForge.Core.Library;

namespace VNForge.Core.Tests.Library;

public sealed class UserTagsTests
{

    [Theory]
    [InlineData(null)]
    [InlineData("a\u0001b")]
    public void Nome_invalido_e_recusado(string? input) =>
        Assert.Throws<LibraryException>(() => UserTagName.Normalize(input));

    [Fact]
    public void Nome_em_nfd_e_nfc_e_a_mesma_tag()
    {
        var tags = new UserTags();
        tags.Add("g1", "café");
        tags.Add("g1", "café");
        tags.Add("g1", "ｃａｆé");

        Assert.Equal(["café"], tags.TagsOf("g1"));
        Assert.True(tags.Has("g1", "ＣＡＦÉ"));
        Assert.Single(tags.Tags);
    }

    [Fact]
    public void Nome_longo_demais_e_recusado() =>
        Assert.Throws<LibraryException>(() => UserTagName.Normalize(new string('a', UserTagName.MaxLength + 1)));

    [Theory]
    [InlineData("#gg0000")]
    public void Cor_invalida_e_recusada(string input) =>
        Assert.Throws<LibraryException>(() => UserTagName.NormalizeColor(input));

    [Fact]
    public void Remove_tira_do_jogo_e_a_tag_continua_no_registro()
    {
        var tags = new UserTags();
        tags.Add("g1", "favorito");
        tags.Add("g2", "favorito");

        Assert.True(tags.Remove("g1", "Favorito"));
        Assert.False(tags.Remove("g1", "favorito"));

        Assert.Empty(tags.TagsOf("g1"));
        Assert.Equal(["g2"], tags.GamesWith("favorito"));
        Assert.Throws<LibraryException>(() => tags.Remove("g1", "inexistente"));
    }

    [Fact]
    public void Renomear_para_um_nome_que_existe_exige_mesclar()
    {
        var tags = new UserTags();
        tags.Add("g1", "a");
        tags.Add("g2", "b");

        var error = Assert.Throws<LibraryException>(() => tags.Rename("a", "b"));

        Assert.Contains("--merge", error.Message);
        Assert.Equal(["a"], tags.TagsOf("g1"));
    }

    [Fact]
    public void Renomear_para_o_mesmo_nome_com_outra_caixa_nao_muda_nada()
    {
        var tags = new UserTags();
        tags.Add("g1", "a");

        Assert.Equal(1, tags.Rename("a", "A").Games);

        Assert.Equal(["a"], tags.TagsOf("g1"));
    }

    [Fact]
    public void Apagar_a_unica_tag_exigida_nao_deixa_so_a_exclusao_valendo()
    {
        var tags = new UserTags();
        tags.Add("g1", "a");
        tags.Add("g1", "b");
        tags.SavedFilter = LibraryTagFilter.Create(["a"], any: false, ["b"]);

        var edit = tags.Delete("a");

        Assert.True(tags.SavedFilter.IsEmpty);
        Assert.Contains("filtro salvo foi apagado", Assert.Single(edit.Notices));
    }

    [Fact]
    public void Apagar_nao_tira_do_filtro_o_termo_sem_prefixo_que_ainda_bate_com_tag_do_vndb()
    {
        var game = TagTestGames.WithVndb("Jogo", false, new LibraryTag("g1", "Drama", "cont", 2.9, 0));
        var tags = new UserTags();
        tags.Add(game.Id, "drama");
        tags.Add(game.Id, "outra");
        tags.SavedFilter = LibraryTagFilter.Create(["drama", "outra"], any: false, ["drama"]);

        var edit = tags.Delete("drama", [game]);

        Assert.Equal(["vndb:drama", "outra"], tags.SavedFilter.Include);
        Assert.Equal(["vndb:drama"], tags.SavedFilter.Exclude);
        Assert.Contains("só para a tag do VNDB", string.Join(' ', edit.Notices));
    }

    [Fact]
    public void Mesclar_com_o_alvo_em_exclusao_tira_o_termo_de_inclusao_e_avisa()
    {
        var tags = new UserTags();
        tags.Add("g1", "a");
        tags.Add("g2", "b");
        tags.SavedFilter = LibraryTagFilter.Create(["a", "x"], any: false, ["b"]);

        var edit = tags.Rename("a", "b", merge: true);

        Assert.Equal(["x"], tags.SavedFilter.Include);
        Assert.Equal(["b"], tags.SavedFilter.Exclude);
        Assert.Contains("--not", Assert.Single(edit.Notices));
    }

    [Fact]
    public void Renomear_e_apagar_acompanham_o_filtro_salvo()
    {
        var tags = new UserTags();
        tags.Add("g1", "a");
        tags.Add("g1", "b");
        tags.Add("g1", "c");
        tags.SavedFilter = LibraryTagFilter.Create(["minha:a", "vndb:g1", "b"], any: false, ["c"]);

        tags.Rename("a", "z");
        Assert.Equal(["minha:z", "vndb:g1", "b"], tags.SavedFilter.Include);

        tags.Delete("c");
        tags.Delete("b");
        Assert.Equal(["minha:z", "vndb:g1"], tags.SavedFilter.Include);
        Assert.Empty(tags.SavedFilter.Exclude);
    }
}
