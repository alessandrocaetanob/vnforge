using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Spelling;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Spelling;

public sealed class IgnoredWordsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-ignored-" + Guid.NewGuid().ToString("N"));

    public IgnoredWordsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private ProjectFolder NovoProjeto() => ProjectFolder.Create(_root, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static string[] Wrong(string text, ProjectDictionary project) => [.. SpellChecker.Portuguese.Find(text, project).Select(span => span.Text)];

    [Fact]
    public void Lista_ida_e_volta_byte_a_byte_com_uma_palavra_por_linha()
    {
        var json = IgnoredWordsSerializer.Serialize([new IgnoredWord("Zorgblat"), new IgnoredWord("kyuun")]);

        Assert.Equal("{\n  \"version\": 1,\n  \"entries\": [\n    {\"word\":\"Zorgblat\"},\n    {\"word\":\"kyuun\"}\n  ]\n}\n", json);
        var lido = IgnoredWordsSerializer.Parse(json, "ignored-words.json");
        Assert.Equal([new IgnoredWord("Zorgblat"), new IgnoredWord("kyuun")], lido);
        Assert.Equal(json, IgnoredWordsSerializer.Serialize(lido));
    }

    [Fact]
    public void Palavra_vazia_ou_repetida_ignorando_maiusculas_e_recusada()
    {
        Assert.Throws<GlossaryFileException>(() => IgnoredWordsSerializer.Serialize([new IgnoredWord("  ")]));

        var ex = Assert.Throws<GlossaryFileException>(() => IgnoredWordsSerializer.Serialize([new IgnoredWord("Zorgblat"), new IgnoredWord(" zorgblat ")]));

        Assert.Contains("mais de uma vez", ex.Reason);
    }

    [Fact]
    public void Projeto_antigo_sem_o_arquivo_abre_sem_palavras_e_sem_criar_o_arquivo()
    {
        var projeto = NovoProjeto();
        projeto.SaveGlossary([new GlossaryTerm("先輩", "veterano")]);
        var glossario = File.ReadAllText(projeto.GlossaryPath);

        Assert.Empty(projeto.LoadIgnoredWords());
        Assert.Equal(glossario, File.ReadAllText(projeto.GlossaryPath));
        Assert.False(File.Exists(projeto.IgnoredWordsPath));
    }

    [Fact]
    public void Projeto_grava_a_palavra_e_le_de_volta_sem_repetir_a_mesma_palavra()
    {
        var projeto = NovoProjeto();

        Assert.True(projeto.AddIgnoredWord("Zorgblat"));
        Assert.False(projeto.AddIgnoredWord("zorgblat"));
        Assert.True(projeto.AddIgnoredWord("kyuun"));

        Assert.Equal([new IgnoredWord("Zorgblat"), new IgnoredWord("kyuun")], projeto.LoadIgnoredWords());
        var gravado = File.ReadAllBytes(projeto.IgnoredWordsPath);
        projeto.SaveIgnoredWords(projeto.LoadIgnoredWords());
        Assert.Equal(gravado, File.ReadAllBytes(projeto.IgnoredWordsPath));
    }

    [Fact]
    public void Palavra_em_branco_nao_e_gravada()
    {
        var projeto = NovoProjeto();

        Assert.False(projeto.AddIgnoredWord("  "));

        Assert.False(File.Exists(projeto.IgnoredWordsPath));
    }

    [Fact]
    public void Arquivo_ilegivel_na_leitura_tolerante_vira_lista_vazia_com_o_motivo()
    {
        var projeto = NovoProjeto();
        File.WriteAllText(projeto.IgnoredWordsPath, "isto não é json");

        var palavras = projeto.LoadIgnoredWordsOrEmpty(out var problema);

        Assert.Empty(palavras);
        Assert.Contains("ignored-words.json", problema);
    }

    [Fact]
    public void Palavra_ignorada_deixa_de_ser_marcada_em_todas_as_falas_sem_liberar_as_outras()
    {
        var sem = ProjectDictionary.Create([], []);
        var com = ProjectDictionary.Create([], [], ignoredWords: ["Zorgblat", "kyuun"]);

        Assert.Equal(["Zorgblat"], Wrong("Zorgblat chegou.", sem));
        Assert.Equal(["kyuun", "esquesi"], Wrong("Foi kyuun, esquesi.", sem));
        Assert.Equal(["esquesi"], Wrong("Zorgblat, esquesi e Kyuun.", com));
        Assert.Empty(Wrong("Zorgblat chegou. Depois, zorgblat saiu.", com));
    }

    [Fact]
    public void Palavra_ignorada_com_hifen_vale_inteira_e_nao_libera_a_parte()
    {
        var sem = ProjectDictionary.Create([], []);
        var com = ProjectDictionary.Create([], [], ignoredWords: ["xorgbla-zzq"]);

        Assert.Equal(["xorgbla-zzq"], Wrong("Foi xorgbla-zzq mesmo.", sem));
        Assert.Empty(Wrong("Foi xorgbla-zzq mesmo.", com));
        Assert.Equal(["xorgbla"], Wrong("Foi xorgbla mesmo.", com));
    }

    [Fact]
    public void Dicionario_com_a_palavra_acrescentada_nao_muda_o_original()
    {
        var original = ProjectDictionary.Create([], []);

        var novo = original.WithIgnoredWord("Zorgblat");

        Assert.True(novo.Contains("zorgblat"));
        Assert.False(original.Contains("Zorgblat"));
    }

    [Fact]
    public void Regra_de_ortografia_respeita_as_palavras_ignoradas_do_contexto()
    {
        var unit = new TextUnit("a:1", TextUnitKind.Dialogue, null, "原文", "Zorgblat esquesi a chave.", TranslationStatus.Translated);
        var texts = new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = [unit] };
        var context = new QaContext(texts, [], [], FormattingProfile.Default);
        var rule = new SpellingRule();

        Assert.Equal(2, rule.Check(context).Count());

        var ignorando = context with { IgnoredWords = [new IgnoredWord("Zorgblat")] };
        var issue = Assert.Single(rule.Check(ignorando));
        Assert.Contains("esquesi", issue.Message);

        Assert.Equal(2, rule.Check(context).Count());
    }

    [Fact]
    public void Contexto_do_projeto_traz_as_palavras_ignoradas()
    {
        var projeto = NovoProjeto();
        projeto.AddIgnoredWord("Zorgblat");

        var context = QaContext.FromProject(projeto);

        Assert.Equal([new IgnoredWord("Zorgblat")], context.IgnoredWords);
    }
}
