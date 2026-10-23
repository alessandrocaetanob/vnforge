using System.Text;
using VNForge.Core.Projects;
using VNForge.Core.Review;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Review;

public class ReviewRoundTripTests
{
    private static ProjectFolder NovoProjeto(string raiz) => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static void Semear(ProjectFolder projeto)
    {
        projeto.SaveText("yst00001",
        [
            new("yst00001:000001", TextUnitKind.Dialogue, "美咲", "おはよう\\p", "Bom dia\\p", TranslationStatus.Machine)
            {
                Origin = new TranslationOrigin("anthropic", "claude-opus-5-5", new DateTimeOffset(2026, 10, 17, 18, 0, 0, TimeSpan.Zero)),
                Flags = ["len"],
                Locator = System.Text.Json.JsonDocument.Parse("""{"command":812,"args":[0]}""").RootElement,
            },
            new("yst00001:000002", TextUnitKind.Narration, null, "雨が降っていた。", null, TranslationStatus.Untranslated),
            new("yst00001:000003", TextUnitKind.Choice, null, "はい, \"はい\"", "", TranslationStatus.Untranslated) { Notes = "ver com o\nrevisor" },
        ]);
        projeto.SaveText("yst00002", [new("yst00002:000001", TextUnitKind.System, null, "夜", null, TranslationStatus.Untranslated)]);
    }

    private static Dictionary<string, byte[]> Bytes(ProjectFolder projeto) =>
        Directory.GetFiles(projeto.TextDirectory).ToDictionary(p => Path.GetFileName(p)!, File.ReadAllBytes);

    private static void AssertIguais(Dictionary<string, byte[]> esperado, Dictionary<string, byte[]> atual)
    {
        Assert.Equal(esperado.Keys.Order(), atual.Keys.Order());
        foreach (var (nome, conteudo) in esperado)
        {
            Assert.True(conteudo.AsSpan().SequenceEqual(atual[nome]), $"{nome} mudou");
        }
    }

    [Fact]
    public void Exportar_e_importar_sem_edicao_nao_altera_os_text_json_nem_regrava_arquivos()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        Semear(projeto);
        var antes = Bytes(projeto);
        var csv = Path.Combine(temp.Path, "revisao.csv");

        var exportadas = ReviewExporter.Export(projeto, ReviewFormats.Csv, csv);
        var resultado = ReviewImporter.Import(projeto, csv);

        Assert.Equal(4, exportadas);
        Assert.Equal(4, resultado.Rows);
        Assert.Equal(0, resultado.ChangedUnits);
        Assert.Empty(resultado.ChangedScripts);
        AssertIguais(antes, Bytes(projeto));
    }

    [Fact]
    public void Importacao_nao_regrava_nem_o_arquivo_que_estava_fora_do_formato_canonico()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        Semear(projeto);
        var caminho = projeto.TextPath("yst00002");
        File.WriteAllText(caminho, File.ReadAllText(caminho).Replace("  ", "\t"));
        var antes = Bytes(projeto);
        var csv = Path.Combine(temp.Path, "revisao.csv");

        ReviewExporter.Export(projeto, ReviewFormats.Csv, csv);
        ReviewImporter.Import(projeto, csv);

        AssertIguais(antes, Bytes(projeto));
    }

    [Fact]
    public void Importacao_altera_so_traducao_status_e_notas_e_so_nos_arquivos_afetados()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        Semear(projeto);
        var segundo = File.ReadAllBytes(projeto.TextPath("yst00002"));
        var csv = Path.Combine(temp.Path, "revisao.csv");
        ReviewExporter.Export(projeto, ReviewFormats.Csv, csv);
        var texto = File.ReadAllText(csv)
            .Replace("Bom dia\\p,machine,", "Bom dia!\\p,approved,ok")
            .Replace("原文", "x")
            .Replace("美咲", "Outro");
        File.WriteAllText(csv, texto, new UTF8Encoding(true));

        var resultado = ReviewImporter.Import(projeto, csv);

        Assert.Equal(1, resultado.ChangedUnits);
        Assert.Equal(["yst00001"], resultado.ChangedScripts);
        var unidades = projeto.LoadText("yst00001");
        var primeira = unidades[0];
        Assert.Equal("Bom dia!\\p", primeira.Translation);
        Assert.Equal(TranslationStatus.Approved, primeira.Status);
        Assert.Equal("ok", primeira.Notes);
        Assert.Equal("美咲", primeira.Speaker);
        Assert.Equal("おはよう\\p", primeira.Source);
        Assert.Equal(["len"], primeira.Flags);
        Assert.Equal("anthropic", primeira.Origin?.Provider);
        Assert.Equal(812, primeira.Locator?.GetProperty("command").GetInt32());
        Assert.Equal(new[] { "yst00001:000001", "yst00001:000002", "yst00001:000003" }, unidades.Select(u => u.Id));
        Assert.Equal("ver com o\nrevisor", unidades[2].Notes);
        Assert.True(segundo.AsSpan().SequenceEqual(File.ReadAllBytes(projeto.TextPath("yst00002"))));
    }

    [Fact]
    public void Traducao_apagada_na_planilha_vira_nula()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        Semear(projeto);
        var csv = Path.Combine(temp.Path, "revisao.csv");
        ReviewExporter.Export(projeto, ReviewFormats.Csv, csv);
        File.WriteAllText(csv, File.ReadAllText(csv).Replace("Bom dia\\p,machine", ",untranslated"));

        ReviewImporter.Import(projeto, csv);

        var primeira = projeto.LoadText("yst00001")[0];
        Assert.Null(primeira.Translation);
        Assert.Equal(TranslationStatus.Untranslated, primeira.Status);
    }

    [Fact]
    public void Id_desconhecido_e_erro_e_nada_e_importado_pela_metade()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        Semear(projeto);
        var antes = Bytes(projeto);
        var csv = Path.Combine(temp.Path, "revisao.csv");
        ReviewExporter.Export(projeto, ReviewFormats.Csv, csv);
        var conteudo = File.ReadAllText(csv)
            .Replace("Bom dia\\p,machine,", "Mudou,approved,")
            .Replace("yst00002:000001,system,,夜,,untranslated,", "yst00002:999999,system,,夜,sim,translated,");
        File.WriteAllText(csv, conteudo);

        var ex = Assert.Throws<ReviewImportException>(() => ReviewImporter.Import(projeto, csv));

        Assert.Contains("yst00002:999999", ex.Message);
        Assert.Contains("desconhecido", ex.Message);
        Assert.Single(ex.Errors);
        AssertIguais(antes, Bytes(projeto));
    }

    [Fact]
    public void Todos_os_erros_sao_reunidos_numa_so_excecao()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        Semear(projeto);
        var antes = Bytes(projeto);
        var csv = Path.Combine(temp.Path, "revisao.csv");
        File.WriteAllText(csv, string.Join("\r\n",
            "id,translation,status,notes",
            "yst00001:000001,a,inventado,",
            "yst00001:000001,b,translated,",
            "nada:1,c,translated,",
            string.Empty));

        var ex = Assert.Throws<ReviewImportException>(() => ReviewImporter.Import(projeto, csv));

        Assert.Equal(3, ex.Errors.Count);
        Assert.Contains(ex.Errors, e => e.Line == 2 && e.Message.Contains("inventado"));
        Assert.Contains(ex.Errors, e => e.Line == 3 && e.Message.Contains("repetido"));
        Assert.Contains(ex.Errors, e => e.Line == 4 && e.Message.Contains("nada:1"));
        AssertIguais(antes, Bytes(projeto));
    }
}
