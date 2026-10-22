using System.Text;
using System.Text.Json;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public class TextFileSerializerTests
{
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

        var lidas = TextFileSerializer.Parse(TextFileSerializer.Serialize(original), "text/yst00123.json");

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
        var ex = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse("{\n  \"version\": 1,\n  \"units\": [ ]]\n}", "text/yst00123.json"));

        Assert.Contains("text/yst00123.json", ex.Message);
        Assert.Contains("linha 3", ex.Message);
        Assert.Contains("coluna", ex.Message);
        Assert.Equal("text/yst00123.json", ex.FilePath);
    }

    [Theory]
    [InlineData("{ \"units\": [] }", "version")]
    [InlineData("{ \"version\": 1 }", "units")]
    [InlineData("{ \"version\": 1, \"units\": [], \"extra\": 1 }", "extra")]
    [InlineData("[]", "objeto")]
    [InlineData("null", "objeto")]
    public void Parse_de_envelope_invalido_falha_em_portugues_citando_o_arquivo(string json, string trecho)
    {
        var ex = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(json, "text/a.json"));

        Assert.Contains("text/a.json", ex.Message);
        Assert.Contains(trecho, ex.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2")]
    [InlineData("\"1\"")]
    [InlineData("1.5")]
    public void Parse_de_version_nao_suportada_falha(string version)
    {
        var ex = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse($"{{ \"version\": {version}, \"units\": [] }}", "text/a.json"));

        Assert.Contains("version", ex.Message);
    }

    [Fact]
    public void Parse_de_unidade_invalida_cita_o_campo_e_a_posicao()
    {
        const string Json = "{\n  \"version\": 1,\n  \"units\": [\n    {\"id\":\"a:000001\",\"kind\":\"grito\",\"source\":\"x\",\"status\":\"untranslated\"}\n  ]\n}\n";

        var ex = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(Json, "text/a.json"));

        Assert.Contains("text/a.json", ex.Message);
        Assert.Contains("$.units[0].kind", ex.Message);
        Assert.Contains("linha 4", ex.Message);
        Assert.Contains("valor inválido", ex.Message);
        Assert.DoesNotContain("could not", ex.Message);
    }

    [Fact]
    public void Parse_recusa_campo_repetido_em_vez_de_descartar_unidades()
    {
        var raiz = "{ \"version\": 1, \"units\": [ {\"id\":\"a:000001\",\"kind\":\"dialogue\",\"source\":\"x\",\"status\":\"untranslated\"} ], \"units\": [] }";
        var unidade = "{ \"version\": 1, \"units\": [ {\"id\":\"a:000001\",\"kind\":\"dialogue\",\"source\":\"x\",\"source\":\"y\",\"status\":\"untranslated\"} ] }";

        Assert.Contains("campo repetido", Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(raiz, "a.json")).Message);
        Assert.Contains("campo repetido", Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(unidade, "a.json")).Message);
    }

    [Fact]
    public void Ida_e_volta_preserva_emoji_e_caracteres_fora_do_plano_basico()
    {
        var original = new[] { Unidade(1, "😀 𠮷野家") };

        var lidas = TextFileSerializer.Parse(TextFileSerializer.Serialize(original), "a.json");

        Assert.Equal("😀 𠮷野家", lidas[0].Source);
    }

    [Fact]
    public void Parse_de_unidade_sem_campo_obrigatorio_ou_com_campo_desconhecido_falha()
    {
        var faltando = "{ \"version\": 1, \"units\": [ {\"id\":\"a:000001\",\"kind\":\"dialogue\",\"status\":\"untranslated\"} ] }";
        var desconhecido = "{ \"version\": 1, \"units\": [ {\"id\":\"a:000001\",\"kind\":\"dialogue\",\"source\":\"x\",\"status\":\"untranslated\",\"traslation\":\"y\"} ] }";

        var msgFaltando = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(faltando, "a.json")).Message;
        var msgDesconhecido = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(desconhecido, "a.json")).Message;

        Assert.Contains("campo obrigatório ausente: 'source'", msgFaltando);
        Assert.Contains("campo desconhecido 'traslation'", msgDesconhecido);
        Assert.Contains("a.json", msgDesconhecido);
    }

    [Fact]
    public void Parse_de_unidade_nula_ou_flags_com_elemento_nulo_falha()
    {
        Assert.Throws<TextFileException>(() => TextFileSerializer.Parse("{ \"version\": 1, \"units\": [ null ] }", "a.json"));
        var flagsNulo = "{ \"version\": 1, \"units\": [ {\"id\":\"a:000001\",\"kind\":\"dialogue\",\"source\":\"x\",\"status\":\"untranslated\",\"flags\":[null]} ] }";
        Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(flagsNulo, "a.json"));
    }

    [Fact]
    public void Parse_confere_que_os_ids_pertencem_ao_script_e_nao_se_repetem()
    {
        var outro = TextFileSerializer.Serialize([new TextUnit("outro:000001", TextUnitKind.Other, null, "x", null, TranslationStatus.Untranslated)]);
        var repetido = TextFileSerializer.Serialize([Unidade(1), Unidade(1)]);

        var ex1 = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(outro, "text/yst00123.json", "yst00123"));
        var ex2 = Assert.Throws<TextFileException>(() => TextFileSerializer.Parse(repetido, "text/yst00123.json", "yst00123"));

        Assert.Contains("outro:000001", ex1.Message);
        Assert.Contains("yst00123:", ex1.Message);
        Assert.Contains("yst00123:000001", ex2.Message);
        Assert.Contains("mais de uma vez", ex2.Message);
    }

    [Fact]
    public void Nao_usa_bom_na_saida()
    {
        var bytes = new UTF8Encoding(false).GetBytes(TextFileSerializer.Serialize([Unidade(1)]));

        Assert.NotEqual(0xEF, bytes[0]);
    }
}
