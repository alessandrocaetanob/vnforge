using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

/// <summary>O índice por original e o "Usar em todos": a mesma fala em outras linhas, e só ela.</summary>
[Trait("Risco", "R2")]
public class SameSourceTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static TextUnit Linha(string script, int n, string original, string? traducao = null, TranslationStatus status = TranslationStatus.Untranslated) =>
        new($"{script}:{n:D6}", TextUnitKind.Dialogue, "美咲", original, traducao, status);

    private static Dictionary<string, IReadOnlyList<TextUnit>> Textos() => new Dictionary<string, IReadOnlyList<TextUnit>>
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
    public void Original_vazio_nao_agrupa()
    {
        var textos = new Dictionary<string, IReadOnlyList<TextUnit>> { ["yst00001"] = [Linha("yst00001", 0, ""), Linha("yst00001", 1, "")] };
        var linhas = ProjectLines.Create(textos);

        Assert.Empty(linhas.SameSourceAs(linhas.LinesOf("yst00001")[0]));
    }

    [Fact]
    public void Usar_em_todos_com_linha_que_nao_existe_avisa_sem_gravar_o_script()
    {
        using var pasta = new TempFolder();
        var projeto = NovoProjeto(pasta.Path);
        projeto.SaveText("yst00001", Textos()["yst00001"]);
        var editor = new LineEditor(projeto, new FakeTimeProvider(Agora));

        var resultado = editor.SetTranslationFor([("yst00001", "yst00001:000000"), ("yst00001", "yst00001:000099")], "Tá.\\p");

        Assert.IsType<KeyNotFoundException>(resultado.Failure);
        Assert.Empty(resultado.Changes);
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
