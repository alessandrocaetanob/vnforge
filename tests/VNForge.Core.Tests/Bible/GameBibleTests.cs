using VNForge.Core.Bible;

namespace VNForge.Core.Tests.Bible;

[Trait("Risco", "R2")]
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
}
