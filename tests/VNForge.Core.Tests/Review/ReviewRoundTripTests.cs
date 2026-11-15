using System.Text;
using VNForge.Core.Projects;
using VNForge.Core.Review;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Review;

[Trait("Risco", "R3")]
public class ReviewRoundTripTests
{
    private static readonly string[] ExpectedIds = ["yst00001:000001", "yst00001:000002", "yst00001:000003"];

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

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public void Exportar_e_importar_sem_edicao_em_cada_formato_nao_altera_os_text_json_nem_regrava_arquivos(string formato)
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        Semear(projeto);
        var antes = Bytes(projeto);
        var csv = Path.Combine(temp.Path, "revisao." + formato);

        var exportadas = ReviewExporter.Export(projeto, ReviewFormats.Find(formato)!, csv);
        var resultado = ReviewImporter.Import(projeto, csv);

        Assert.Equal(4, exportadas);
        Assert.Equal(4, resultado.Rows);
        Assert.Equal(0, resultado.ChangedUnits);
        Assert.Empty(resultado.ChangedScripts);
        AssertIguais(antes, Bytes(projeto));
    }

    [Fact]
    public void Importacao_sem_mudanca_mantem_a_duvida_de_linha_com_traducao_nula_ou_vazia()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        Semear(projeto);
        var duvida = new TranslationDoubt(DoubtKind.Tone, "Ironia?");
        var unidades = projeto.LoadText("yst00001").ToList();
        unidades[1] = unidades[1] with { Doubt = duvida };
        unidades[2] = unidades[2] with { Doubt = duvida };
        projeto.SaveText("yst00001", unidades);
        Assert.Null(unidades[1].Translation);
        Assert.Equal(string.Empty, unidades[2].Translation);
        var csv = Path.Combine(temp.Path, "revisao.csv");
        ReviewExporter.Export(projeto, ReviewFormats.Csv, csv);

        ReviewImporter.Import(projeto, csv);

        var depois = projeto.LoadText("yst00001");
        Assert.Equal(duvida, depois[1].Doubt);
        Assert.Equal(duvida, depois[2].Doubt);
    }
}
