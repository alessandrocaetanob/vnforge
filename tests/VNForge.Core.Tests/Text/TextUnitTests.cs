using System.Text.Json;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

[Trait("Risco", "R3")]
public class TextUnitTests
{
    private static readonly int[] SingleZero = [0];

    private static TextUnit Unidade(
        string source = "今日は、いい天気だね。\\p",
        string? translation = null,
        TranslationStatus status = TranslationStatus.Untranslated) =>
        new("yst00123:000042", TextUnitKind.Dialogue, "美咲", source, translation, status);

    [Fact]
    public void Ida_e_volta_json_preserva_todos_os_campos()
    {
        var locator = JsonSerializer.SerializeToElement(new { command = 812, args = SingleZero });
        var unidade = new TextUnit(
            "yst00123:000042",
            TextUnitKind.Dialogue,
            "美咲",
            "今日は、いい天気だね。\\p",
            "Hoje está um dia bonito, né?\\p",
            TranslationStatus.Reviewed)
        {
            Origin = new TranslationOrigin("anthropic", "claude-opus-5-5", new DateTimeOffset(2026, 10, 17, 18, 0, 0, TimeSpan.Zero)),
            Notes = "tom casual",
            Flags = ["double-space"],
            Locator = locator,
        };

        var json = TextUnitJson.Serialize(unidade);
        var lida = TextUnitJson.Deserialize(json);

        Assert.Equal(unidade.Id, lida.Id);
        Assert.Equal(unidade.Kind, lida.Kind);
        Assert.Equal(unidade.Speaker, lida.Speaker);
        Assert.Equal(unidade.Source, lida.Source);
        Assert.Equal(unidade.Translation, lida.Translation);
        Assert.Equal(unidade.Status, lida.Status);
        Assert.Equal(unidade.Origin, lida.Origin);
        Assert.Equal(unidade.Notes, lida.Notes);
        Assert.Equal(unidade.Flags, lida.Flags);
        Assert.True(JsonElement.DeepEquals(locator, lida.Locator!.Value));
        Assert.Equal(json, TextUnitJson.Serialize(lida));
    }

    [Fact]
    public void Lista_de_unidades_faz_ida_e_volta()
    {
        TextUnit[] unidades = [Unidade(), Unidade(translation: "Oi", status: TranslationStatus.Translated) with { Id = "yst00123:000043" }];

        var lidas = TextUnitJson.DeserializeList(TextUnitJson.SerializeList(unidades));

        Assert.Equal(["yst00123:000042", "yst00123:000043"], lidas.Select(u => u.Id));
        Assert.Equal(TranslationStatus.Translated, lidas[1].Status);
    }

    [Fact]
    public void ChosenSpeaker_nulo_nao_entra_no_json_e_o_valor_faz_ida_e_volta()
    {
        Assert.DoesNotContain("chosenSpeaker", TextUnitJson.Serialize(Unidade()));

        var json = TextUnitJson.Serialize(Unidade() with { ChosenSpeaker = "Inui" });
        Assert.Contains("\"chosenSpeaker\":\"Inui\"", json, StringComparison.Ordinal);
        Assert.Equal("Inui", TextUnitJson.Deserialize(json).ChosenSpeaker);
        Assert.Equal(string.Empty, TextUnitJson.Deserialize(TextUnitJson.Serialize(Unidade() with { ChosenSpeaker = string.Empty })).ChosenSpeaker);
    }

    [Fact]
    public void Doubt_com_tipo_desconhecido_ou_sem_frase_e_recusada()
    {
        var json = TextUnitJson.Serialize(Unidade());
        var tipoNovo = json[..^1] + ",\"doubt\":{\"kind\":\"vibe\",\"note\":\"x\"}}";
        var semFrase = json[..^1] + ",\"doubt\":{\"kind\":\"tone\"}}";

        Assert.Throws<System.Text.Json.JsonException>(() => TextUnitJson.Deserialize(tipoNovo));
        Assert.Throws<System.Text.Json.JsonException>(() => TextUnitJson.Deserialize(semFrase));
    }
}
