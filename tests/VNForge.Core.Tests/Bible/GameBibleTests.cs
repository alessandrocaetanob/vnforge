using VNForge.Core.Bible;

namespace VNForge.Core.Tests.Bible;

public sealed class GameBibleTests
{
    private static BibleScene Generated(string key, string summary = "Resumo da IA") => new(key)
    {
        Name = "Nome da IA",
        Summary = summary,
        Characters = ["Kenta", "Ren"],
        Tone = ["cômico leve"],
        Jokes = ["o guarda-chuva"],
        Relations = [new BibleRelation("Kenta e Ren", "Amigos de infância")],
    };

    [Fact]
    public void As_cenas_ficam_na_ordem_da_historia_e_nao_na_ordem_alfabetica()
    {
        var bible = GameBible.From([new BibleScene("yst10"), new BibleScene("yst2"), new BibleScene("yst1")]);

        Assert.Equal(["yst1", "yst2", "yst10"], bible.Scenes.Select(s => s.Key));
    }

    [Fact]
    public void Editar_um_campo_marca_o_campo_como_da_pessoa()
    {
        var scene = Generated("yst1").EditSummary("  Meu resumo  ");

        Assert.Equal("Meu resumo", scene.Summary);
        Assert.True(scene.IsEdited(BibleField.Summary));
        Assert.False(scene.IsEdited(BibleField.Tone));
    }

    [Fact]
    public void Gerar_de_novo_nao_sobrescreve_o_campo_que_a_pessoa_escreveu()
    {
        var bible = GameBible.Empty.WithGeneratedScene(Generated("yst1"))
            .WithScene(Generated("yst1").EditSummary("Meu resumo").EditTone(["seco"]));

        var again = bible.WithGeneratedScene(Generated("yst1", "Outro resumo da IA") with { Name = "Outro nome", Jokes = ["nova piada"] });

        var scene = again.Scene("yst1")!;
        Assert.Equal("Meu resumo", scene.Summary);
        Assert.Equal(["seco"], scene.Tone);
        Assert.Equal("Outro nome", scene.Name);
        Assert.Equal(["nova piada"], scene.Jokes);
        Assert.Equal([BibleField.Summary, BibleField.Tone], scene.Edited);
    }

    [Fact]
    public void Uma_cena_nova_entra_como_a_IA_escreveu()
    {
        var bible = GameBible.Empty.WithGeneratedScene(Generated("yst1"));

        Assert.Empty(bible.Scene("yst1")!.Edited);
        Assert.Equal("Nome da IA", bible.SceneName("yst1"));
    }

    [Fact]
    public void Reescrever_um_campo_com_a_IA_troca_so_aquele_campo_e_o_devolve_a_IA()
    {
        var scene = Generated("yst1").EditSummary("Meu resumo").EditTone(["seco"]);

        var rewritten = scene.WithGeneratedField(Generated("yst1", "Novo da IA"), BibleField.Summary);

        Assert.Equal("Novo da IA", rewritten.Summary);
        Assert.Equal(["seco"], rewritten.Tone);
        Assert.Equal([BibleField.Tone], rewritten.Edited);
    }

    [Fact]
    public void A_sinopse_da_pessoa_fica_e_a_linha_do_tempo_da_IA_e_trocada()
    {
        var bible = GameBible.Empty.WithGeneratedOverview("Sinopse da IA", [new BibleTimelineEntry("Prólogo", "A chuva")])
            .EditSynopsis("Minha sinopse");

        var again = bible.WithGeneratedOverview("Outra da IA", [new BibleTimelineEntry("Capítulo 1", "x")]);

        Assert.Equal("Minha sinopse", again.Synopsis);
        Assert.Equal(["Capítulo 1"], again.Timeline.Select(t => t.Title));
    }

    [Fact]
    public void Nome_em_branco_nao_conta_como_nome_de_cena()
    {
        var bible = GameBible.From([new BibleScene("yst1") { Name = "  " }, new BibleScene("yst2") { Name = "Chuva" }]);

        Assert.Null(bible.SceneName("yst1"));
        Assert.Equal(new Dictionary<string, string> { ["yst2"] = "Chuva" }, bible.SceneNames());
    }

    [Fact]
    public void Lista_de_campo_ignora_vazios_e_repetidos()
    {
        var scene = new BibleScene("yst1").EditTone(["seco", " ", "seco", " tenso "]);

        Assert.Equal(["seco", "tenso"], scene.Tone);
    }
}
