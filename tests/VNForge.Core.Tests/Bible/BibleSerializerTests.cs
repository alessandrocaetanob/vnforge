using VNForge.Core.Bible;
using VNForge.Core.Glossary;

namespace VNForge.Core.Tests.Bible;

public sealed class BibleSerializerTests
{
    private static GameBible Sample() => GameBible.From(
    [
        new BibleScene("yst2") { Name = "Intervalo", Summary = "Pátio.", Characters = ["Aoi"] },
        new BibleScene("yst1")
        {
            Name = "Atraso no portão",
            Summary = "Kenta e Ren chegam atrasados à escola \"Hoshizora\".",
            Characters = ["Kenta", "Ren"],
            Tone = ["cômico leve"],
            Jokes = ["o guarda-chuva de Ren"],
            Relations = [new BibleRelation("Kenta e Ren", "Amigos de infância")],
            Edited = [BibleField.Summary],
        },
    ]) with
    {
        Synopsis = "Uma cidade onde sempre chove.",
        Timeline = [new BibleTimelineEntry("Prólogo", "A chuva")],
        TimelineEdited = true,
        Generation = new BibleGeneration(new DateTimeOffset(2026, 11, 6, 21, 40, 0, TimeSpan.Zero), "claude-opus-5-5", 2, 1, 1.25m),
    };

    [Fact]
    public void Ida_e_volta_preserva_tudo()
    {
        var json = BibleSerializer.Serialize(Sample());

        var back = BibleSerializer.Parse(json, "bible.json");

        Assert.Equal(["yst1", "yst2"], back.Scenes.Select(s => s.Key));
        Assert.Equal(Sample().Scene("yst1")!.Summary, back.Scene("yst1")!.Summary);
        Assert.Equal([BibleField.Summary], back.Scene("yst1")!.Edited);
        Assert.Equal(["Kenta", "Ren"], back.Scene("yst1")!.Characters);
        Assert.Equal(new BibleRelation("Kenta e Ren", "Amigos de infância"), Assert.Single(back.Scene("yst1")!.Relations));
        Assert.Equal("Uma cidade onde sempre chove.", back.Synopsis);
        Assert.True(back.TimelineEdited);
        Assert.False(back.SynopsisEdited);
        Assert.Equal(Sample().Generation, back.Generation);
        Assert.Equal(json, BibleSerializer.Serialize(back));
    }

    [Theory]
    [InlineData("""{ "version": 1, "scenes": [ { "key": "yst1", "cor": "azul" } ] }""", "desconhecido")]
    [InlineData("""{ "version": 2, "scenes": [] }""", "mais nova")]
    [InlineData("""{ "version": 0, "scenes": [] }""", "não existe")]
    [InlineData("""{ "version": 1, "scenes": [ { "key": "yst1" }, { "key": "yst1" } ] }""", "mais de uma vez")]
    [InlineData("""{ "version": 1, "scenes": [ { "key": "YST1" } ] }""", "inválida")]
    [InlineData("""{ "version": 1, "scenes": [ { "key": "yst1", "edited": [ "resumo" ] } ] }""", "scenes")]
    [InlineData("não é json", "JSON")]
    public void Arquivo_invalido_diz_o_que_esta_errado(string json, string expected)
    {
        var ex = Assert.Throws<GlossaryFileException>(() => BibleSerializer.Parse(json, "bible.json"));

        Assert.Contains("bible.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains(expected, ex.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gravar_recusa_cena_repetida()
    {
        var bible = new GameBible { Scenes = [new BibleScene("yst1"), new BibleScene("yst1")] };

        Assert.Throws<GlossaryFileException>(() => BibleSerializer.Serialize(bible));
    }
}
