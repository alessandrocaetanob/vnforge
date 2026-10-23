using System.Text;
using System.Text.Json;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public class TextFileSerializerTests
{
    private const string TextAJson = "text/a.json";
    private const string AJson = "a.json";
    private const string TextId1 = "yst00123:000001";
    private const string TextYst123Json = "text/yst00123.json";

    private static TextUnit Unidade(int posicao, string source = "今日は、いい天気だね。\\p", string? translation = null, TranslationStatus status = TranslationStatus.Untranslated) =>
        new($"yst00123:{posicao:D6}", TextUnitKind.Dialogue, "美咲", source, translation, status);

    private static IReadOnlyList<TextUnit> Exemplo() =>
    [
        Unidade(1, "「おはよう」\\n{VAR:name}\\c3\\p"),
        Unidade(2, "第二章", "Capítulo 2\\p", TranslationStatus.Translated) with
        {
            Origin = new TranslationOrigin("anthropic", "claude-opus-5-5", new DateTimeOffset(2026, 10, 17, 18, 0, 0, TimeSpan.Zero)),
            Notes = "revisar",
            Flags = ["tamanho"],
            Locator = JsonDocument.Parse("""{ "command": 812, "args": [0] }""").RootElement,
        },
        Unidade(3, "aspas \" e barra \\ e tab\t"),
    ];

    private static string Json(IReadOnlyList<TextUnit> units) => string.Join("|", units.Select(TextUnitJson.Serialize));

    [Fact]
    public void Serialize_tem_envelope_com_version_e_uma_unidade_por_linha()
    {
        var json = TextFileSerializer.Serialize([Unidade(1), Unidade(2)]);

        var linhas = json.Split('\n');
        Assert.Equal("{", linhas[0]);
        Assert.Equal("  \"version\": 1,", linhas[1]);
        Assert.Equal("  \"units\": [", linhas[2]);
        Assert.StartsWith("    {\"id\":\"yst00123:000001\"", linhas[3]);
        Assert.EndsWith("},", linhas[3]);
        Assert.StartsWith("    {\"id\":\"yst00123:000002\"", linhas[4]);
        Assert.EndsWith("}", linhas[4]);
        Assert.Equal("  ]", linhas[5]);
        Assert.Equal("}", linhas[6]);
        Assert.Equal("", linhas[7]);
        Assert.Equal(8, linhas.Length);
    }

    [Fact]
    public void Serialize_de_lista_vazia_gera_units_vazio()
    {
        Assert.Equal("{\n  \"version\": 1,\n  \"units\": []\n}\n", TextFileSerializer.Serialize([]));
    }

    [Fact]
    public void Serialize_duas_vezes_gera_texto_identico_com_fim_de_linha_fixo()
    {
        var a = TextFileSerializer.Serialize(Exemplo());
        var b = TextFileSerializer.Serialize(Exemplo());

        Assert.Equal(a, b);
        Assert.DoesNotContain('\r', a);
        Assert.EndsWith("}\n", a);
    }

    [Fact]
    public void Serialize_mantem_japones_legivel_e_marcadores_como_texto()
    {
        var json = TextFileSerializer.Serialize(Exemplo());

        Assert.Contains("「おはよう」\\\\n{VAR:name}\\\\c3\\\\p", json);
        Assert.DoesNotContain("\\u", json);
    }

    [Fact]
    public void Ida_e_volta_de_unidades_nao_perde_nada()
    {
        var original = Exemplo();

        var lidas = TextFileSerializer.Parse(TextFileSerializer.Serialize(original), TextYst123Json);

        Assert.Equal(Json(original), Json(lidas));
        Assert.Equal("「おはよう」\\n{VAR:name}\\c3\\p", lidas[0].Source);
        Assert.Equal("aspas \" e barra \\ e tab\t", lidas[2].Source);
    }

    [Fact]
    public void Ida_e_volta_de_json_para_json_gera_texto_identico()
    {
        var primeiro = TextFileSerializer.Serialize(Exemplo());

        var segundo = TextFileSerializer.Serialize(TextFileSerializer.Parse(primeiro, "x.json"));

        Assert.Equal(primeiro, segundo);
    }

    [Fact]
    public void Parse_aceita_comentarios_virgula_final_e_envelope_sem_unidades()
    {
        var lidas = TextFileSerializer.Parse("// nota\n{ \"version\": 1, \"units\": [], }", "x.json");

        Assert.Empty(lidas);
    }

    [Fact]
    public void Parse_de_json_malformado_cita_arquivo_e_posicao()
    {
        var ex = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse("{\n  \"version\": 1,\n  \"units\": [ ]]\n}", TextYst123Json));

        Assert.Contains(TextYst123Json, ex.Message);
        Assert.Contains("linha 3", ex.Message);
        Assert.Contains("coluna", ex.Message);
        Assert.Equal(TextYst123Json, ex.FilePath);
    }

    [Theory]
    [InlineData("{ \"units\": [] }", "version")]
    [InlineData("{ \"version\": 1 }", "units")]
    [InlineData("{ \"version\": 1, \"units\": [], \"extra\": 1 }", "extra")]
    [InlineData("[]", "objeto")]
    [InlineData("null", "objeto")]
    public void Parse_de_envelope_invalido_falha_em_portugues_citando_o_arquivo(string json, string trecho)
    {
        var ex = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(json, TextAJson));

        Assert.Contains(TextAJson, ex.Message);
        Assert.Contains(trecho, ex.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2")]
    [InlineData("\"1\"")]
    [InlineData("1.5")]
    public void Parse_de_version_nao_suportada_falha(string version)
    {
        var ex = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse($"{{ \"version\": {version}, \"units\": [] }}", TextAJson));

        Assert.Contains("version", ex.Message);
    }

    [Fact]
    public void Parse_de_unidade_invalida_cita_o_campo_e_a_posicao()
    {
        const string Json = "{\n  \"version\": 1,\n  \"units\": [\n    {\"id\":\"a:000001\",\"kind\":\"grito\",\"source\":\"x\",\"status\":\"untranslated\"}\n  ]\n}\n";

        var ex = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(Json, TextAJson));

        Assert.Contains(TextAJson, ex.Message);
        Assert.Contains("$.units[0].kind", ex.Message);
        Assert.Contains("linha 4", ex.Message);
        Assert.Contains("valor inválido", ex.Message);
        Assert.DoesNotContain("could not", ex.Message);
    }

    [Fact]
    public void Parse_recusa_campo_repetido_em_vez_de_descartar_unidades()
    {
        var raiz = "{\n  \"version\": 1,\n  \"units\": [ {\"id\":\"a:000001\",\"kind\":\"dialogue\",\"source\":\"x\",\"status\":\"untranslated\"} ],\n  \"units\": []\n}\n";
        var unidade = "{\n  \"version\": 1,\n  \"units\": [\n    {\"id\":\"a:000001\",\"kind\":\"dialogue\",\"source\":\"x\",\"source\":\"y\",\"status\":\"untranslated\"}\n  ]\n}\n";

        var msgRaiz = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(raiz, AJson)).Message;
        var msgUnidade = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(unidade, AJson)).Message;

        Assert.Contains("campo repetido 'units'", msgRaiz);
        Assert.Contains("linha 4", msgRaiz);
        Assert.Contains("campo repetido 'source'", msgUnidade);
        Assert.Contains("$.units[0]", msgUnidade);
        Assert.Contains("linha 4", msgUnidade);
        Assert.DoesNotContain("Duplicate", msgRaiz + msgUnidade);
    }

    [Theory]
    [InlineData("\"id\":null,\"kind\":\"dialogue\",\"source\":\"x\",\"status\":\"untranslated\"", "$.units[0]")]
    [InlineData("\"id\":\"a:000001\",\"kind\":\"dialogue\",\"source\":null,\"status\":\"untranslated\"", "$.units[0]")]
    [InlineData("\"id\":\"a:000001\",\"kind\":\"dialogue\",\"source\":\"x\",\"status\":\"untranslated\",\"notes\":null", "$.units[0]")]
    [InlineData("\"id\":\"a:000001\",\"kind\":\"dialogue\",\"source\":\"x\",\"status\":\"untranslated\",\"flags\":null", "$.units[0]")]
    [InlineData("\"id\":\"a:000001\",\"kind\":\"dialogue\",\"source\":\"x\",\"status\":\"untranslated\",\"origin\":{\"provider\":null,\"at\":\"2026-10-17T18:00:00Z\"}", "$.units[0]")]
    public void Parse_de_campo_nulo_onde_nao_cabe_falha_em_portugues(string campos, string caminho)
    {
        var json = $"{{ \"version\": 1, \"units\": [ {{{campos}}} ] }}";

        var msg = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(json, AJson)).Message;

        Assert.Contains("não aceita null", msg);
        Assert.Contains(caminho, msg);
        Assert.DoesNotContain("allow", msg);
    }

    [Fact]
    public void Parse_de_status_invalido_e_tipo_errado_falha_em_portugues()
    {
        var status = "{ \"version\": 1, \"units\": [ {\"id\":\"a:000001\",\"kind\":\"dialogue\",\"source\":\"x\",\"status\":\"quase\"} ] }";
        var tipo = "{ \"version\": 1, \"units\": [ {\"id\":\"a:000001\",\"kind\":\"dialogue\",\"source\":5,\"status\":\"untranslated\"} ] }";

        foreach (var json in new[] { status, tipo })
        {
            var msg = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(json, AJson)).Message;
            Assert.Contains("valor inválido", msg);
            Assert.DoesNotContain("could not", msg);
        }
    }

    [Fact]
    public void Serialize_escapa_so_o_que_o_json_exige_e_deixa_o_resto_literal()
    {
        var ideografico = ((char)0x3000).ToString();
        var literal = $"{ideografico}「こんにちは」{(char)0x00A0}😀{(char)0x2028}野家";
        var texto = $"{literal} \"aspas\" \\ \t{(char)1}";

        var json = TextFileSerializer.Serialize([Unidade(1, texto)]);
        var outra = TextFileSerializer.Serialize(TextFileSerializer.Parse(json, AJson));

        Assert.Contains(literal, json);
        Assert.Contains("\\\"aspas\\\" \\\\ \\t\\u0001", json);
        Assert.DoesNotContain("\\u3000", json);
        Assert.Equal(json, outra);
        Assert.Equal(texto, TextFileSerializer.Parse(json, AJson)[0].Source);
        Assert.Equal(json, TextFileSerializer.Serialize([Unidade(1, texto)]));
    }

    private static TextUnit UnidadeComTextoEmTodosOsCampos(string texto) =>
        new(TextId1, TextUnitKind.Dialogue, texto, texto, texto, TranslationStatus.Translated)
        {
            Origin = new TranslationOrigin(texto, texto, new DateTimeOffset(2026, 10, 17, 18, 0, 0, TimeSpan.Zero)),
            Notes = texto,
            Flags = [texto, texto],
        };

    [Fact]
    public void Texto_especial_em_todos_os_campos_string_sai_literal_e_volta_igual()
    {
        var texto = $"{(char)0x3000}「野家」😀{(char)0x00A0}{(char)0x2028}\"\\\n";
        var unidade = UnidadeComTextoEmTodosOsCampos(texto);

        var json = TextFileSerializer.Serialize([unidade]);
        var lida = TextFileSerializer.Parse(json, AJson)[0];

        Assert.DoesNotContain("\\u3000", json);
        Assert.Equal(texto, lida.Speaker);
        Assert.Equal(texto, lida.Source);
        Assert.Equal(texto, lida.Translation);
        Assert.Equal(texto, lida.Notes);
        Assert.Equal([texto, texto], lida.Flags);
        Assert.Equal(texto, lida.Origin!.Provider);
        Assert.Equal(texto, lida.Origin.Model);
        Assert.Equal(json, TextFileSerializer.Serialize([lida]));
    }

    [Fact]
    public void Strings_nulas_sao_gravadas_como_null()
    {
        var json = TextFileSerializer.Serialize([Unidade(1) with { Speaker = null }]);

        Assert.Contains("\"speaker\":null", json);
        Assert.Contains("\"translation\":null", json);
    }

    [Fact]
    public void Surrogate_solitario_em_qualquer_campo_vira_erro_em_portugues_com_o_id()
    {
        var invalido = "ok" + (char)0xD800 + "fim";
        TextUnit[] unidades =
        [
            UnidadeComTextoEmTodosOsCampos("a") with { Id = TextId1, Source = invalido },
            UnidadeComTextoEmTodosOsCampos("a") with { Translation = invalido },
            UnidadeComTextoEmTodosOsCampos("a") with { Speaker = invalido },
            UnidadeComTextoEmTodosOsCampos("a") with { Notes = invalido },
            UnidadeComTextoEmTodosOsCampos("a") with { Flags = ["a", invalido] },
            UnidadeComTextoEmTodosOsCampos("a") with { Origin = new TranslationOrigin("manual", invalido, DateTimeOffset.UnixEpoch) },
        ];

        foreach (var unidade in unidades)
        {
            var ex = Assert.Throws<TextFileException>(() => TextFileSerializer.Serialize([unidade], TextAJson));

            Assert.Contains(TextId1, ex.Message);
            Assert.Contains("surrogate solitário", ex.Message);
            Assert.Contains(TextAJson, ex.Message);
        }
    }

    [Fact]
    public void Ida_e_volta_preserva_emoji_e_caracteres_fora_do_plano_basico()
    {
        var original = new[] { Unidade(1, "😀 𠮷野家") };

        var lidas = TextFileSerializer.Parse(TextFileSerializer.Serialize(original), AJson);

        Assert.Equal("😀 𠮷野家", lidas[0].Source);
    }

    [Fact]
    public void Parse_de_unidade_sem_campo_obrigatorio_ou_com_campo_desconhecido_falha()
    {
        var faltando = "{ \"version\": 1, \"units\": [ {\"id\":\"a:000001\",\"kind\":\"dialogue\",\"status\":\"untranslated\"} ] }";
        var desconhecido = "{ \"version\": 1, \"units\": [ {\"id\":\"a:000001\",\"kind\":\"dialogue\",\"source\":\"x\",\"status\":\"untranslated\",\"traslation\":\"y\"} ] }";

        var msgFaltando = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(faltando, AJson)).Message;
        var msgDesconhecido = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(desconhecido, AJson)).Message;

        Assert.Contains("campo obrigatório ausente: 'source'", msgFaltando);
        Assert.Contains("campo desconhecido 'traslation'", msgDesconhecido);
        Assert.Contains(AJson, msgDesconhecido);
    }

    [Fact]
    public void Parse_de_unidade_nula_ou_flags_com_elemento_nulo_falha()
    {
        Assert.Throws<TextFileException>(() => TextFileSerializer.Parse("{ \"version\": 1, \"units\": [ null ] }", AJson));
        var flagsNulo = "{ \"version\": 1, \"units\": [ {\"id\":\"a:000001\",\"kind\":\"dialogue\",\"source\":\"x\",\"status\":\"untranslated\",\"flags\":[null]} ] }";
        Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(flagsNulo, AJson));
    }

    [Fact]
    public void Parse_confere_que_os_ids_pertencem_ao_script_e_nao_se_repetem()
    {
        var outro = TextFileSerializer.Serialize([new TextUnit("outro:000001", TextUnitKind.Other, null, "x", null, TranslationStatus.Untranslated)]);
        var repetido = TextFileSerializer.Serialize([Unidade(1), Unidade(1)]);

        var ex1 = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(outro, TextYst123Json, "yst00123"));
        var ex2 = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(repetido, TextYst123Json, "yst00123"));

        Assert.Contains("outro:000001", ex1.Message);
        Assert.Contains("yst00123:", ex1.Message);
        Assert.Contains(TextId1, ex2.Message);
        Assert.Contains("mais de uma vez", ex2.Message);
    }

    [Fact]
    public void Nao_usa_bom_na_saida()
    {
        var bytes = new UTF8Encoding(false).GetBytes(TextFileSerializer.Serialize([Unidade(1)]));

        Assert.NotEqual(0xEF, bytes[0]);
    }
}
