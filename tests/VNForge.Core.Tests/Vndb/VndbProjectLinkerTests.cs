using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Formatting;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Core.Tests.Vndb;

/// <summary>#536: o jogo do VNDB confirmado vira <c>vndb.json</c> e os personagens que casam com o <c>ES.CHAR.NAME</c> chegam ao <c>characters.json</c> com o gênero.</summary>
public sealed class VndbProjectLinkerTests
{
    private static FakeVndbCatalog Catalog(params VndbVisualNovel[] games) =>
        new(games.Length == 0 ? [VndbSamples.Game(description: "Numa cidade onde chove, [spoiler]ele morre[/spoiler]Kenta [b]volta[/b].")] : games, VndbSamples.Cast);

    private static ProjectFolder NewProject(string root) => ProjectFolder.Create(root, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static TextUnit Name(int n, string source) =>
        new($"yst00001:{n:D6}", TextUnitKind.Name, null, source, null, TranslationStatus.Untranslated);

    private static TextUnit Line(int n, string speaker) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, speaker, "こんにちは", "Olá", TranslationStatus.Translated);

    [Fact]
    public async Task FindCandidates_poe_na_frente_o_jogo_que_combina_com_a_pasta_ou_o_exe()
    {
        var other = VndbSamples.Game("v900002", "Amaoto no Hakoniwa: Fan Disc", "雨音の箱庭 ファンディスク");
        var main = VndbSamples.Game();
        var catalog = new FakeVndbCatalog([other, main]);

        var candidates = await VndbProjectLinker.FindCandidatesAsync(catalog, "雨音の箱庭", "Hakoniwa", "AmaotoNoHakoniwa.exe", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["v900002", "v900001"], candidates.Select(c => c.VisualNovel.Id));
        Assert.All(candidates, c => Assert.Equal(VndbEvidence.FolderAndExe, c.Evidence));
    }

    [Fact]
    public async Task FindCandidates_marca_so_o_titulo_quando_a_pasta_nao_aparece_nos_titulos()
    {
        var candidates = await VndbProjectLinker.FindCandidatesAsync(Catalog(), "雨音の箱庭", "jogo123", "game.exe", cancellationToken: TestContext.Current.CancellationToken);

        var candidate = Assert.Single(candidates);
        Assert.Equal(VndbEvidence.TitleOnly, candidate.Evidence);
    }

    [Fact]
    public async Task FindCandidates_ignora_pasta_curta_demais_para_provar_alguma_coisa()
    {
        var candidates = await VndbProjectLinker.FindCandidatesAsync(Catalog(), "雨音の箱庭", "ab", "x.exe", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(VndbEvidence.TitleOnly, Assert.Single(candidates).Evidence);
    }

    [Theory]
    [InlineData("a", "a", CharacterGender.Other)]
    [InlineData(null, null, CharacterGender.Unknown)]
    public void GenderOf_prefere_o_real_ao_que_aparece_sem_spoiler(string? visible, string? real, CharacterGender expected) =>
        Assert.Equal(expected, VndbProjectLinker.GenderOf(new VndbGender(visible, real)));

    [Fact]
    public async Task Match_casa_pelo_nome_inteiro_por_uma_parte_ou_por_apelido_ignorando_espaco_e_largura()
    {
        var link = await VndbProjectLinker.BuildAsync(Catalog(), VndbSamples.VisualNovelId, TestContext.Current.CancellationToken);

        var matches = VndbProjectLinker.Match(["相沢健太", "葵", "ＲＥＮ", "早瀬　蓮", "Todo-sensei", "ナレーター"], link.Characters);

        Assert.Equal(
            [("相沢健太", "c1"), ("葵", "c2"), ("早瀬　蓮", "c3"), ("Todo-sensei", "c4")],
            matches.Select(m => (m.GameName, m.Character.Id)));
    }

    [Fact]
    public void ApplyCharacters_sem_vndb_json_ou_sem_texto_extraido_nao_faz_nada()
    {
        using var folder = new TempFolder();
        var project = NewProject(folder.Path);

        Assert.Equal(VndbApplyResult.None, VndbProjectLinker.ApplyCharacters(project));
        Assert.False(File.Exists(project.CharactersPath));
    }

    [Fact]
    public async Task Extrair_depois_de_ligar_traz_os_personagens_sem_outro_comando()
    {
        using var folder = new TempFolder();
        var project = NewProject(folder.Path);
        await VndbProjectLinker.LinkAsync(project, Catalog(), VndbSamples.VisualNovelId, TestContext.Current.CancellationToken);

        Assert.Empty(project.LoadCharacters());
        project.SaveText("yst00001", [Name(0, "相沢健太")]);
        VndbProjectLinker.ApplyCharacters(project);

        Assert.Equal(CharacterGender.Male, Assert.Single(project.LoadCharacters()).Gender);
    }

    private static ProjectFolder ProjectWithGame(string root, string package)
    {
        var game = Path.Combine(root, "jogo");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "pacote.fake"), package);
        return ProjectFolder.Create(
            Path.Combine(root, "projeto"),
            new ProjectConfig("Teste", new GameConfig(game.Replace(Path.DirectorySeparatorChar, '/'), "Jogo.exe"), new EngineConfig("fake"), new LanguagesConfig("ja", "pt-BR"), new EncodingConfig("cp1252"), AiConfig.Default));
    }

    [Fact]
    public void Extract_com_vndb_json_invalido_termina_a_extracao_e_avisa()
    {
        using var folder = new TempFolder();
        var project = ProjectWithGame(folder.Path, "name|相沢健太");
        File.WriteAllText(project.VndbPath, "{ nao e json");

        var result = ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result.VndbCharacters);
        Assert.Contains(result.Warnings, w => w.StartsWith("Os personagens do VNDB não entraram em characters.json:", StringComparison.Ordinal) && w.Contains("vndb.json", StringComparison.Ordinal));
        Assert.Equal(1, result.TotalLines);
        Assert.False(File.Exists(project.CharactersPath));
    }

    private static QaContext Qa(ProjectFolder project, IReadOnlyList<CharacterEntry> characters) =>
        new(project.LoadAllText(), [], characters, FormattingProfile.Default);
}
