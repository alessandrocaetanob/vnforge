using System.Text.Json;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

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
    public void Lista_tem_uma_unidade_compacta_por_linha()
    {
        TextUnit[] unidades =
        [
            Unidade() with { Locator = JsonSerializer.SerializeToElement(new { command = 1, args = SingleZero }) },
            Unidade(translation: "Oi", status: TranslationStatus.Translated) with { Id = "yst00123:000043" },
        ];

        var json = TextUnitJson.SerializeList(unidades);
        var linhas = json.Split('\n');

        Assert.Equal(4, linhas.Length);
        Assert.Equal("[", linhas[0]);
        Assert.EndsWith("},", linhas[1]);
        Assert.StartsWith("{\"id\":\"yst00123:000042\"", linhas[1]);
        Assert.EndsWith("}", linhas[2]);
        Assert.Equal("]", linhas[3]);
        Assert.Equal("[]", TextUnitJson.SerializeList([]));
        Assert.Empty(TextUnitJson.DeserializeList("[]"));
    }

    [Theory]
    [InlineData("2026-10-17T18:00:00", false)]
    [InlineData("2026-10-17T15:00:00-03:00", true)]
    public void Origem_at_exige_Z_ou_deslocamento(string at, bool valido)
    {
        var json = $$"""{ "id": "a:000001", "kind": "name", "source": "x", "status": "machine", "origin": { "provider": "p", "at": "{{at}}" } }""";

        if (valido)
        {
            Assert.Equal(DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime, TextUnitJson.Deserialize(json).Origin!.At.UtcDateTime);
        }
        else
        {
            Assert.ThrowsAny<JsonException>(() => TextUnitJson.Deserialize(json));
        }
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

    [Theory]
    [InlineData(DoubtKind.Pun, "pun")]
    [InlineData(DoubtKind.HiddenSubject, "hidden-subject")]
    [InlineData(DoubtKind.UncertainGender, "uncertain-gender")]
    [InlineData(DoubtKind.CulturalReference, "cultural-reference")]
    [InlineData(DoubtKind.Tone, "tone")]
    public void Doubt_faz_ida_e_volta_no_json_com_o_nome_do_tipo(DoubtKind kind, string nome)
    {
        var unidade = Unidade(translation: "Oi", status: TranslationStatus.Machine) with { Doubt = new TranslationDoubt(kind, "Não sei quem fala.") };

        var json = TextUnitJson.Serialize(unidade);

        Assert.Contains($"\"doubt\":{{\"kind\":\"{nome}\",\"note\":\"Não sei quem fala.\"}}", json, StringComparison.Ordinal);
        Assert.Equal(unidade.Doubt, TextUnitJson.Deserialize(json).Doubt);
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
