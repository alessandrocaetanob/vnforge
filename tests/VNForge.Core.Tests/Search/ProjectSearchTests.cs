using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Projects;
using VNForge.Core.Search;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Search;

[Trait("Risco", "R6")]
public class ProjectSearchTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static TextUnit Linha(string script, int n, string? traducao, TranslationStatus status = TranslationStatus.Translated, string? original = null) =>
        new($"{script}:{n:D6}", TextUnitKind.Dialogue, null, original ?? $"原文{n}", traducao, status)
        {
            Origin = traducao is null ? null : new TranslationOrigin("anthropic", "claude-opus-5-5", Agora.AddDays(-1)),
        };

    private static Dictionary<string, IReadOnlyList<TextUnit>> Textos(params (string Script, TextUnit[] Linhas)[] scripts) =>
        scripts.ToDictionary(s => s.Script, s => (IReadOnlyList<TextUnit>)s.Linhas);

    private static ProjectFolder NovoProjeto(string raiz) => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static SearchResult Achar(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> textos, SearchOptions opcoes) =>
        ProjectSearch.Find(textos, opcoes, TestContext.Current.CancellationToken);

    private static ReplacePreview Previa(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> textos, SearchOptions opcoes, string troca) =>
        ProjectSearch.PreviewReplace(textos, opcoes, troca, TestContext.Current.CancellationToken);

    private static ReplacePreview Previa(ProjectFolder projeto, SearchOptions opcoes, string troca) =>
        ProjectSearch.PreviewReplace(projeto, opcoes, troca, TestContext.Current.CancellationToken);

    [Fact]
    public void Padrao_que_so_casa_vazio_e_contado_na_busca_e_na_previa()
    {
        var textos = Textos(("a", [Linha("a", 1, "Um"), Linha("a", 2, "Dois"), Linha("a", 3, "Três 3")]));
        var opcoes = new SearchOptions("^", UseRegex: true);

        var busca = Achar(textos, opcoes);
        var previa = Previa(textos, opcoes, ">");

        Assert.Empty(busca.Hits);
        Assert.Equal(3, busca.EmptyMatchLines);
        Assert.True(previa.IsEmpty);
        Assert.Equal(3, previa.EmptyMatchLines);
        Assert.Equal(2, Achar(textos, new SearchOptions(@"\d*", UseRegex: true)).EmptyMatchLines);
    }

    [Fact]
    public void Busca_no_original_acha_mesmo_sem_traducao()
    {
        var textos = Textos(("a", [Linha("a", 1, null, TranslationStatus.Untranslated, original: "美咲が来た"), Linha("a", 2, "x", original: "別")]));

        var achados = Achar(textos, new SearchOptions("美咲", Field: SearchField.Source)).Hits;

        Assert.Equal(["a:000001"], achados.Select(h => h.UnitId));
    }

    [Fact]
    public void Texto_vazio_e_regex_invalida_viram_SearchException_em_portugues()
    {
        var textos = Textos(("a", [Linha("a", 1, "x")]));

        Assert.Contains("Digite", Assert.Throws<SearchException>(() => Achar(textos, new SearchOptions(""))).Message);
        Assert.Contains("inválida", Assert.Throws<SearchException>(() => Achar(textos, new SearchOptions("(", UseRegex: true))).Message);
    }

    [Fact]
    public void Regex_catastrofica_estoura_o_tempo_limite_em_vez_de_travar()
    {
        var textos = Textos(("a", [Linha("a", 1, new string('a', 64) + "!")]));
        var opcoes = new SearchOptions("(a+)+$", UseRegex: true) { MatchTimeout = TimeSpan.FromMilliseconds(50) };

        var erro = Assert.Throws<SearchException>(() => Achar(textos, opcoes));

        Assert.Contains("demorou demais", erro.Message);
        Assert.Throws<SearchException>(() => Previa(textos, opcoes, "x"));
    }

    [Fact]
    public void Previa_do_original_e_recusada()
    {
        var textos = Textos(("a", [Linha("a", 1, "x")]));

        Assert.Contains("original", Assert.Throws<SearchException>(() => Previa(textos, new SearchOptions("x", Field: SearchField.Source), "y")).Message);
    }
}
