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
            [TranslationStatus.Keep] = "keep",
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
            Locator = JsonSerializer.SerializeToElement(new { command = 812, args = SingleZero }),
        };

        var json = TextUnitJson.Serialize(unidade);
        using var doc = JsonDocument.Parse(json);
        var nomes = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(
            ["id", "kind", "speaker", "source", "translation", "status", "origin", "notes", "flags", "previousSource", "locator"],
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

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "id": "a:0001", "kind": "name", "status": "untranslated" }""")]
    [InlineData("""{ "id": null, "kind": "name", "source": "x", "status": "untranslated" }""")]
    public void Campo_obrigatorio_ausente_ou_nulo_falha(string json)
    {
        Assert.ThrowsAny<JsonException>(() => TextUnitJson.Deserialize(json));
    }

    [Fact]
    public void Locator_nao_depende_do_documento_de_origem()
    {
        JsonElement raiz;
        using (var doc = JsonDocument.Parse("""{ "command": 812 }"""))
        {
            raiz = doc.RootElement;
            var unidade = Unidade() with { Locator = raiz };
            Assert.NotNull(unidade.Locator);
        }

        var depois = Unidade() with { Locator = JsonSerializer.SerializeToElement(new { command = 1 }) };
        Assert.Contains("command", TextUnitJson.Serialize(depois));
    }

    [Fact]
    public void Campo_desconhecido_falha_em_vez_de_ser_descartado()
    {
        var ex = Assert.ThrowsAny<JsonException>(() => TextUnitJson.Deserialize(
            """{ "id": "a:000001", "kind": "name", "source": "x", "status": "untranslated", "traslation": "Oi" }"""));

        Assert.Contains("traslation", ex.Message);
    }

    [Fact]
    public void Leitura_aceita_comentarios_e_virgula_final()
    {
        var lidas = TextUnitJson.DeserializeList(
            """
            [
              // primeira linha
              { "id": "a:000001", "kind": "name", "source": "x", "status": "untranslated", },
            ]
            """);

        Assert.Equal("a:000001", Assert.Single(lidas).Id);
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

    [Fact]
    public void Origem_exige_provider_e_at()
    {
        const string Base = """{ "id": "a:000001", "kind": "name", "source": "x", "status": "machine", "origin": {0} }""";

        Assert.ThrowsAny<JsonException>(() => TextUnitJson.Deserialize(Base.Replace("{0}", """{ "at": "2026-10-17T18:00:00Z" }""")));
        Assert.ThrowsAny<JsonException>(() => TextUnitJson.Deserialize(Base.Replace("{0}", """{ "provider": "anthropic" }""")));
    }

    [Theory]
    [InlineData("2026-10-17T18:00:00", false)]
    [InlineData("2026-10-17", false)]
    [InlineData("2026-10-17T18:00:00Z", true)]
    [InlineData("2026-10-17T15:00:00-03:00", true)]
    [InlineData("2026-10-17T18:00:00.1234567Z", true)]
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

    [Theory]
    [InlineData(""" "notes": null """)]
    [InlineData(""" "flags": null """)]
    [InlineData(""" "flags": [null] """)]
    public void Notes_e_flags_nulos_sao_recusados(string campo)
    {
        var json = $$"""{ "id": "a:000001", "kind": "name", "source": "x", "status": "untranslated", {{campo}} }""";

        Assert.ThrowsAny<JsonException>(() => TextUnitJson.Deserialize(json));
    }

    [Fact]
    public void Elemento_nulo_na_lista_e_recusado()
    {
        Assert.ThrowsAny<JsonException>(() => TextUnitJson.DeserializeList("[null]"));
    }

    [Fact]
    public void Opcoes_sao_somente_leitura()
    {
        Assert.True(TextUnitJson.Options.IsReadOnly);
    }

    [Fact]
    public void Lista_de_unidades_faz_ida_e_volta()
    {
        TextUnit[] unidades = [Unidade(), Unidade(translation: "Oi", status: TranslationStatus.Translated) with { Id = "yst00123:000043" }];

        var lidas = TextUnitJson.DeserializeList(TextUnitJson.SerializeList(unidades));

        Assert.Equal(["yst00123:000042", "yst00123:000043"], lidas.Select(u => u.Id));
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
    public void ChosenSpeaker_tira_os_espacos_das_pontas_e_so_espacos_vira_narracao()
    {
        Assert.Equal("Inui", (Unidade() with { ChosenSpeaker = "  Inui " }).ChosenSpeaker);
        Assert.Equal(string.Empty, (Unidade() with { ChosenSpeaker = " 　 " }).ChosenSpeaker);
    }

    [Fact]
    public void EffectiveSpeaker_e_o_escolhido_quando_existe_e_o_extraido_quando_nao()
    {
        Assert.Equal("美咲", Unidade().EffectiveSpeaker);
        Assert.Equal("Inui", (Unidade() with { ChosenSpeaker = "Inui" }).EffectiveSpeaker);
        Assert.Equal(string.Empty, (Unidade() with { ChosenSpeaker = string.Empty }).EffectiveSpeaker);
        Assert.Null((Unidade() with { Speaker = null }).EffectiveSpeaker);
    }

    [Fact]
    public void Doubt_nula_nao_entra_no_json()
    {
        Assert.DoesNotContain("doubt", TextUnitJson.Serialize(Unidade()));
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
