using System.Text.Json;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public class TextUnitTests
{
    private static TextUnit Unidade(
        string source = "今日は、いい天気だね。\\p",
        string? translation = null,
        TranslationStatus status = TranslationStatus.Untranslated) =>
        new("yst00123:0042", TextUnitKind.Dialogue, "美咲", source, translation, status);

    [Fact]
    public void Kind_cobre_os_valores_do_doc_02_e_serializa_como_string()
    {
        var esperado = new Dictionary<TextUnitKind, string>
        {
            [TextUnitKind.Dialogue] = "dialogue",
            [TextUnitKind.Narration] = "narration",
            [TextUnitKind.Name] = "name",
            [TextUnitKind.Choice] = "choice",
            [TextUnitKind.System] = "system",
            [TextUnitKind.Other] = "other",
        };

        Assert.Equal(esperado.Keys.Order(), Enum.GetValues<TextUnitKind>().Order());
        foreach (var (kind, texto) in esperado)
        {
            Assert.Equal($"\"{texto}\"", JsonSerializer.Serialize(kind, TextUnitJson.Options));
            Assert.Equal(kind, JsonSerializer.Deserialize<TextUnitKind>($"\"{texto}\"", TextUnitJson.Options));
        }
    }

    [Fact]
    public void Status_cobre_os_valores_do_doc_02_em_ordem_de_progresso()
    {
        var esperado = new Dictionary<TranslationStatus, string>
        {
            [TranslationStatus.Untranslated] = "untranslated",
            [TranslationStatus.Machine] = "machine",
            [TranslationStatus.Translated] = "translated",
            [TranslationStatus.Reviewed] = "reviewed",
            [TranslationStatus.Approved] = "approved",
        };

        Assert.Equal(esperado.Keys.Order(), Enum.GetValues<TranslationStatus>().Order());
        Assert.True(TranslationStatus.Untranslated < TranslationStatus.Machine);
        Assert.True(TranslationStatus.Machine < TranslationStatus.Translated);
        Assert.True(TranslationStatus.Translated < TranslationStatus.Reviewed);
        Assert.True(TranslationStatus.Reviewed < TranslationStatus.Approved);
        foreach (var (status, texto) in esperado)
        {
            Assert.Equal($"\"{texto}\"", JsonSerializer.Serialize(status, TextUnitJson.Options));
            Assert.Equal(status, JsonSerializer.Deserialize<TranslationStatus>($"\"{texto}\"", TextUnitJson.Options));
        }
    }

    [Fact]
    public void Valor_de_enum_desconhecido_no_json_falha()
    {
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<TextUnitKind>("\"grito\"", TextUnitJson.Options));
    }

    [Fact]
    public void Numero_no_lugar_do_enum_no_json_falha()
    {
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<TextUnitKind>("0", TextUnitJson.Options));
    }

    [Fact]
    public void Ida_e_volta_json_preserva_todos_os_campos()
    {
        var locator = JsonSerializer.SerializeToElement(new { command = 812, args = new[] { 0 } });
        var unidade = new TextUnit(
            "yst00123:0042",
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
    public void Origem_e_gravada_em_utc_com_Z()
    {
        var origem = new TranslationOrigin("anthropic", "claude-opus-5-5", new DateTimeOffset(2026, 10, 17, 15, 0, 0, TimeSpan.FromHours(-3)));
        var unidade = Unidade() with { Origin = origem };

        using var doc = JsonDocument.Parse(TextUnitJson.Serialize(unidade));

        Assert.Equal("2026-10-17T18:00:00Z", doc.RootElement.GetProperty("origin").GetProperty("at").GetString());
        Assert.Equal(origem.At, TextUnitJson.Deserialize(TextUnitJson.Serialize(unidade)).Origin!.At);
    }

    [Fact]
    public void Json_usa_nomes_camelCase_enums_em_minusculas_e_japones_sem_escape()
    {
        var unidade = Unidade(translation: "Oi", status: TranslationStatus.Machine) with
        {
            Locator = JsonSerializer.SerializeToElement(new { command = 812, args = new[] { 0 } }),
        };

        var json = TextUnitJson.Serialize(unidade);
        using var doc = JsonDocument.Parse(json);
        var nomes = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(
            ["id", "kind", "speaker", "source", "translation", "status", "origin", "notes", "flags", "locator"],
            nomes);
        Assert.Equal("dialogue", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal("machine", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(812, doc.RootElement.GetProperty("locator").GetProperty("command").GetInt32());
        Assert.Contains("今日は、いい天気だね。", json);
        Assert.Contains("\\\\p", json);
    }

    [Fact]
    public void Campos_opcionais_nulos_voltam_nulos_e_listas_ausentes_voltam_vazias()
    {
        var lida = TextUnitJson.Deserialize(
            """{ "id": "a:0001", "kind": "name", "source": "美咲", "status": "untranslated" }""");

        Assert.Null(lida.Speaker);
        Assert.Null(lida.Translation);
        Assert.Null(lida.Origin);
        Assert.Null(lida.Locator);
        Assert.Equal(string.Empty, lida.Notes);
        Assert.Empty(lida.Flags);

        var denovo = TextUnitJson.Deserialize(TextUnitJson.Serialize(lida));
        Assert.Null(denovo.Locator);
        Assert.Null(denovo.Origin);
    }

    [Fact]
    public void Lista_de_unidades_faz_ida_e_volta()
    {
        TextUnit[] unidades = [Unidade(), Unidade(translation: "Oi", status: TranslationStatus.Translated) with { Id = "yst00123:0043" }];

        var lidas = TextUnitJson.DeserializeList(TextUnitJson.SerializeList(unidades));

        Assert.Equal(["yst00123:0042", "yst00123:0043"], lidas.Select(u => u.Id));
        Assert.Equal(TranslationStatus.Translated, lidas[1].Status);
    }

    [Theory]
    [InlineData(null, TranslationStatus.Untranslated, false)]
    [InlineData("", TranslationStatus.Translated, false)]
    [InlineData("Oi", TranslationStatus.Untranslated, false)]
    [InlineData("Oi", TranslationStatus.Machine, true)]
    [InlineData("Oi", TranslationStatus.Approved, true)]
    public void HasTranslation_exige_texto_e_status_diferente_de_untranslated(string? translation, TranslationStatus status, bool esperado)
    {
        Assert.Equal(esperado, Unidade(translation: translation, status: status).HasTranslation);
    }

    [Fact]
    public void NeedsRewrite_e_falso_para_traducao_igual_ao_original_ou_ausente()
    {
        Assert.False(Unidade().NeedsRewrite);
        Assert.False(Unidade(translation: "今日は、いい天気だね。\\p", status: TranslationStatus.Translated).NeedsRewrite);
        Assert.True(Unidade(translation: "Hoje", status: TranslationStatus.Translated).NeedsRewrite);
    }

    [Fact]
    public void Propriedades_calculadas_nao_entram_no_json()
    {
        var json = TextUnitJson.Serialize(Unidade(translation: "Oi", status: TranslationStatus.Translated));

        Assert.DoesNotContain("hasTranslation", json);
        Assert.DoesNotContain("needsRewrite", json);
    }
}
