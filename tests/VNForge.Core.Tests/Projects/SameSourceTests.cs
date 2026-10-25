using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>O índice por original e o "Usar em todos": a mesma fala em outras linhas, e só ela.</summary>
public class SameSourceTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static TextUnit Linha(string script, int n, string original, string? traducao = null, TranslationStatus status = TranslationStatus.Untranslated) =>
        new($"{script}:{n:D6}", TextUnitKind.Dialogue, "美咲", original, traducao, status);

    private static IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> Textos() => new Dictionary<string, IReadOnlyList<TextUnit>>
    {
        ["yst00001"] =
        [
            Linha("yst00001", 0, "「うん」\\p"),
            Linha("yst00001", 1, "「ええ」\\p", "Sim.\\p", TranslationStatus.Translated),
            Linha("yst00001", 2, "「うん」\\p", "Aham.\\p", TranslationStatus.Approved),
        ],
        ["yst00002"] =
        [
            Linha("yst00002", 0, "「うん」\\p"),
            Linha("yst00002", 1, "「うん」", "Sem pausa."),
            Linha("yst00002", 2, "「うん」\\p", "Hm.\\p", TranslationStatus.Machine),
        ],
    };

    [Fact]
    public void Linhas_com_o_mesmo_original_em_todos_os_scripts_sao_encontradas()
    {
        var linhas = ProjectLines.Create(Textos());
        var primeira = linhas.LinesOf("yst00001")[0];

        var iguais = linhas.SameSourceAs(primeira);

        Assert.Equal(["yst00001:000002", "yst00002:000000", "yst00002:000002"], iguais.Select(l => l.Unit.Id));
    }

    [Fact]
    public void A_propria_linha_e_texto_diferente_nao_entram()
    {
        var linhas = ProjectLines.Create(Textos());

        Assert.DoesNotContain(linhas.LinesOf("yst00001")[0].Unit.Id, linhas.SameSourceAs(linhas.LinesOf("yst00001")[0]).Select(l => l.Unit.Id));
        Assert.Empty(linhas.SameSourceAs(linhas.LinesOf("yst00001")[1]));
        Assert.DoesNotContain(linhas.LinesOf("yst00002")[1].Unit.Id, linhas.SameSourceAs(linhas.LinesOf("yst00001")[0]).Select(l => l.Unit.Id));
    }

    [Fact]
    public void Original_vazio_nao_agrupa()
    {
        var textos = new Dictionary<string, IReadOnlyList<TextUnit>> { ["yst00001"] = [Linha("yst00001", 0, ""), Linha("yst00001", 1, "")] };
        var linhas = ProjectLines.Create(textos);

        Assert.Empty(linhas.SameSourceAs(linhas.LinesOf("yst00001")[0]));
    }

    [Fact]
    public void O_indice_acompanha_as_linhas_trocadas_com_Replace()
    {
        var linhas = ProjectLines.Create(Textos());
        var primeira = linhas.LinesOf("yst00001")[0];
        var terceira = linhas.LinesOf("yst00001")[2];
        linhas.Replace(terceira.WithUnit(terceira.Unit with { Translation = "Outra." }));

        var iguais = linhas.SameSourceAs(primeira);

        Assert.Equal("Outra.", iguais.First(l => l.Unit.Id == "yst00001:000002").Unit.Translation);
    }

    [Fact]
    public void O_hash_do_original_e_estavel_e_diferencia_textos()
    {
        Assert.Equal(SourceHash.Of("「うん」\\p"), SourceHash.Of("「うん」\\p"));
        Assert.NotEqual(SourceHash.Of("「うん」\\p"), SourceHash.Of("「うん」"));
        Assert.Equal(64, SourceHash.Of("x").Length);
    }

    [Fact]
    public void Usar_em_todos_aplica_a_traducao_so_as_linhas_do_mesmo_original()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        foreach (var (chave, unidades) in Textos())
        {
            projeto.SaveText(chave, unidades);
        }

        var editor = new LineEditor(projeto, new FakeTimeProvider(Agora));
        var linhas = ProjectLines.Create(Textos());
        var alvos = linhas.SameSourceAs(linhas.LinesOf("yst00001")[0]).Select(l => (l.ScriptKey, l.Unit.Id));

        var gravadas = editor.SetTranslationFor(alvos, "Tá.\\p");

        Assert.Equal(3, gravadas.Count);
        Assert.All(gravadas, u => Assert.Equal(("Tá.\\p", TranslationStatus.Translated), (u.Translation, u.Status)));
        Assert.Equal("Tá.\\p", projeto.LoadText("yst00001")[2].Translation);
        Assert.Equal("Tá.\\p", projeto.LoadText("yst00002")[0].Translation);
        Assert.Equal("Tá.\\p", projeto.LoadText("yst00002")[2].Translation);
        Assert.Equal("Sim.\\p", projeto.LoadText("yst00001")[1].Translation);
        Assert.Equal("Sem pausa.", projeto.LoadText("yst00002")[1].Translation);
        Assert.Null(projeto.LoadText("yst00001")[0].Translation);
    }

    [Fact]
    public void Usar_em_todos_pula_as_linhas_que_ja_tem_a_mesma_traducao()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        projeto.SaveText("yst00001", Textos()["yst00001"]);
        var editor = new LineEditor(projeto, new FakeTimeProvider(Agora));

        var gravadas = editor.SetTranslationFor([("yst00001", "yst00001:000002")], "Aham.\\p");

        Assert.Empty(gravadas);
        Assert.Equal(TranslationStatus.Approved, projeto.LoadText("yst00001")[2].Status);
    }

    [Fact]
    public void Usar_em_todos_com_linha_que_nao_existe_avisa_sem_gravar_nada()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        projeto.SaveText("yst00001", Textos()["yst00001"]);
        var editor = new LineEditor(projeto, new FakeTimeProvider(Agora));

        Assert.Throws<KeyNotFoundException>(() => editor.SetTranslationFor([("yst00001", "yst00001:000000"), ("yst00001", "yst00001:000099")], "Tá.\\p"));

        Assert.Null(projeto.LoadText("yst00001")[0].Translation);
    }

    private static ProjectFolder NovoProjeto(string raiz) => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));
}
