using VNForge.Core.Library;

namespace VNForge.Core.Tests.Library;

public sealed class UserTagsTests
{
    [Theory]
    [InlineData("favorito", "favorito")]
    [InlineData("  Pra   TRADUZIR ", "pra traduzir")]
    [InlineData("Rejogar em\tdezembro", "rejogar em dezembro")]
    public void Nome_e_normalizado_em_caixa_e_espacos(string input, string expected) =>
        Assert.Equal(expected, UserTagName.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("vndb:drama")]
    [InlineData("a\u0001b")]
    public void Nome_invalido_e_recusado(string? input) =>
        Assert.Throws<LibraryException>(() => UserTagName.Normalize(input));

    [Theory]
    [InlineData("ｆａｖｏｒｉｔｏ", "favorito")]
    [InlineData("Ｐｒａ　ｔｒａｄｕｚｉｒ", "pra traduzir")]
    [InlineData("café", "café")]
    [InlineData("CAFÉ", "café")]
    public void Nome_ignora_largura_e_forma_de_composicao_do_unicode(string input, string expected) =>
        Assert.Equal(expected, UserTagName.Normalize(input));

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
    public void Dois_pontos_de_largura_total_tambem_sao_recusados() =>
        Assert.Throws<LibraryException>(() => UserTagName.Normalize("vndb：drama"));

    [Fact]
    public void Nome_longo_demais_e_recusado() =>
        Assert.Throws<LibraryException>(() => UserTagName.Normalize(new string('a', UserTagName.MaxLength + 1)));

    [Theory]
    [InlineData("FF8800", "#ff8800")]
    [InlineData("#Ff8800", "#ff8800")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void Cor_e_normalizada(string? input, string? expected) =>
        Assert.Equal(expected, UserTagName.NormalizeColor(input));

    [Theory]
    [InlineData("vermelho")]
    [InlineData("#fff")]
    [InlineData("#gg0000")]
    public void Cor_invalida_e_recusada(string input) =>
        Assert.Throws<LibraryException>(() => UserTagName.NormalizeColor(input));

    [Fact]
    public void Add_cria_a_tag_e_nao_repete_a_mesma_com_outra_caixa()
    {
        var tags = new UserTags();

        tags.Add("g1", "Favorito", "#FF8800");
        tags.Add("g1", " FAVORITO ");

        var tag = Assert.Single(tags.Tags);
        Assert.Equal(new UserTag("favorito", "#ff8800"), tag);
        Assert.Equal(["favorito"], tags.TagsOf("g1"));
    }

    [Fact]
    public void Add_com_cor_muda_a_cor_e_sem_cor_mantem()
    {
        var tags = new UserTags();
        tags.Add("g1", "favorito", "#111111");

        tags.Add("g2", "favorito");
        Assert.Equal("#111111", tags.Get("favorito").Color);

        tags.Add("g2", "favorito", "#222222");
        Assert.Equal("#222222", tags.Get("favorito").Color);
    }

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
    public void Renomear_atualiza_todos_os_jogos_que_usam_a_tag()
    {
        var tags = new UserTags();
        tags.Add("g1", "rejogar", "#00ff00");
        tags.Add("g2", "rejogar");
        tags.Add("g2", "favorito");
        tags.Add("g3", "favorito");

        var changed = tags.Rename("rejogar", "Rejogar Depois");

        Assert.Equal(2, changed.Games);
        Assert.False(tags.TryGet("rejogar", out _));
        Assert.Equal(new UserTag("rejogar depois", "#00ff00"), tags.Get("rejogar depois"));
        Assert.Equal(["rejogar depois"], tags.TagsOf("g1"));
        Assert.Equal(["favorito", "rejogar depois"], tags.TagsOf("g2"));
        Assert.Equal(["favorito"], tags.TagsOf("g3"));
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
    public void Mesclar_junta_os_jogos_sem_repetir_e_mantem_a_cor_do_destino()
    {
        var tags = new UserTags();
        tags.Add("g1", "a", "#aaaaaa");
        tags.Add("g2", "a");
        tags.Add("g2", "b", "#bbbbbb");
        tags.Add("g3", "b");

        var changed = tags.Rename("a", "b", merge: true);

        Assert.Equal(2, changed.Games);
        Assert.Equal(new UserTag("b", "#bbbbbb"), Assert.Single(tags.Tags));
        Assert.Equal(["b"], tags.TagsOf("g1"));
        Assert.Equal(["b"], tags.TagsOf("g2"));
        Assert.Equal(["b"], tags.TagsOf("g3"));
    }

    [Fact]
    public void Mesclar_herda_a_cor_da_origem_quando_o_destino_nao_tem()
    {
        var tags = new UserTags();
        tags.Add("g1", "a", "#aaaaaa");
        tags.Add("g2", "b");

        tags.Rename("a", "b", merge: true);

        Assert.Equal("#aaaaaa", tags.Get("b").Color);
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
    public void Apagar_tira_a_tag_de_todos_os_jogos()
    {
        var tags = new UserTags();
        tags.Add("g1", "velha");
        tags.Add("g2", "velha");
        tags.Add("g2", "nova");

        var changed = tags.Delete("Velha");

        Assert.Equal(2, changed.Games);
        Assert.False(tags.TryGet("velha", out _));
        Assert.Empty(tags.TagsOf("g1"));
        Assert.Equal(["nova"], tags.TagsOf("g2"));
        Assert.Equal(["g2"], tags.GameIds);
        Assert.Throws<LibraryException>(() => tags.Delete("velha"));
    }

    [Fact]
    public void Apagar_o_unico_termo_do_filtro_salvo_apaga_o_filtro_e_avisa()
    {
        var tags = new UserTags();
        tags.Add("g1", "a");
        tags.SavedFilter = LibraryTagFilter.Create(["minha:a"], any: false, null);

        var edit = tags.Delete("a");

        Assert.True(tags.SavedFilter.IsEmpty);
        var notice = Assert.Single(edit.Notices);
        Assert.Contains("filtro salvo foi apagado", notice);
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
    public void Apagar_um_de_varios_termos_tira_so_ele_e_avisa()
    {
        var tags = new UserTags();
        tags.Add("g1", "a");
        tags.SavedFilter = LibraryTagFilter.Create(["a", "vndb:g1"], any: false, null);

        var edit = tags.Delete("a");

        Assert.Equal(["vndb:g1"], tags.SavedFilter.Include);
        Assert.Contains("perdeu", Assert.Single(edit.Notices));
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
