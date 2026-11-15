using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R6")]
public class ProjectLinesTests
{
    private static TextUnit Linha(string key, int n, string? falante, TranslationStatus status) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, falante, $"原文{n}", status == TranslationStatus.Untranslated ? null : $"tradução {n}", status);

    private static Dictionary<string, IReadOnlyList<TextUnit>> Textos(params (string Key, TextUnit[] Units)[] scripts) =>
        scripts.ToDictionary(s => s.Key, s => (IReadOnlyList<TextUnit>)s.Units, StringComparer.Ordinal);

    private static QaIssue Problema(string key, int n, QaSeverity gravidade, string regra = "regra") =>
        new(regra, gravidade, $"{key}:{n:D6}", key, "problema");

    private static LineEntry Entrada(TextUnit unit, params QaIssue[] problemas) => new("s", 1, unit, problemas);

    [Fact]
    public void Chave_desconhecida_devolve_lista_vazia()
    {
        var linhas = ProjectLines.Create(Textos(("a", [Linha("a", 1, null, TranslationStatus.Untranslated)])));

        Assert.Empty(linhas.LinesOf("outro"));
    }

    private static ProjectFolder ProjetoCp1252(string raiz, params TextUnit[] linhas)
    {
        var projeto = ProjectFolder.Create(raiz, new ProjectConfig(
            "VN",
            new GameConfig("D:/Jogos/VN", "VN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default));
        projeto.SaveText("story04", linhas);
        return projeto;
    }

    private static TextUnit RascunhoComTil() =>
        new("story04:000064", TextUnitKind.Dialogue, null, "～朝～", "～Manhã～", TranslationStatus.Machine);
}
