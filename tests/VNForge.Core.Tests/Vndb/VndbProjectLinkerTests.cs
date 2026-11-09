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

    [Fact]
    public async Task Build_traz_sinopse_limpa_e_personagens_com_genero_real_papel_e_apelidos()
    {
        var link = await VndbProjectLinker.BuildAsync(Catalog(), VndbSamples.VisualNovelId, TestContext.Current.CancellationToken);

        Assert.Equal("Amaoto no Hakoniwa", link.Title);
        Assert.Equal("雨音の箱庭", link.OriginalTitle);
        Assert.Equal("Numa cidade onde chove, Kenta volta.", link.Synopsis);
        Assert.Equal(5, link.Characters.Count);
        var kenta = link.Characters[0];
        Assert.Equal(("c1", "Kenta Aizawa", "相沢 健太", CharacterGender.Male, "main", "O protagonista."), (kenta.Id, kenta.Name, kenta.Original, kenta.Gender, kenta.Role, kenta.Description));
        Assert.Equal(["Ken", "Kenta-kun"], kenta.Aliases);
        Assert.Equal(CharacterGender.Female, link.Characters[1].Gender);
    }

    [Fact]
    public void From_guarda_a_miniatura_da_capa_para_o_cabecalho_do_jogo()
    {
        var vn = VndbSamples.Game() with { Image = new VndbImage("https://t.vndb.org/cv/12/345.jpg", "https://t.vndb.org/cv.t/12/345.jpg", 600, 800, 0.1, 0) };

        var link = VndbProjectLinker.From(vn, new VndbResults<VndbCharacter>([], false));

        Assert.Equal(new VndbImageRef("https://t.vndb.org/cv.t/12/345.jpg", 0.1, 0), link.Cover);
        Assert.Null(VndbProjectLinker.From(VndbSamples.Game(), new VndbResults<VndbCharacter>([], false)).Cover);
    }

    [Fact]
    public async Task Build_de_jogo_que_o_vndb_nao_tem_recusa_em_portugues()
    {
        var ex = await Assert.ThrowsAsync<VndbLinkException>(() => VndbProjectLinker.BuildAsync(Catalog(), "v1", TestContext.Current.CancellationToken));

        Assert.Contains("v1", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("m", "m", CharacterGender.Male)]
    [InlineData("f", "f", CharacterGender.Female)]
    [InlineData("o", "o", CharacterGender.Other)]
    [InlineData("a", "a", CharacterGender.Other)]
    [InlineData(null, "f", CharacterGender.Female)]
    [InlineData("f", "m", CharacterGender.Male)]
    [InlineData(null, null, CharacterGender.Unknown)]
    public void GenderOf_prefere_o_real_ao_que_aparece_sem_spoiler(string? visible, string? real, CharacterGender expected) =>
        Assert.Equal(expected, VndbProjectLinker.GenderOf(new VndbGender(visible, real)));

    [Fact]
    public void GenderOf_sem_dado_do_vndb_e_desconhecido() =>
        Assert.Equal(CharacterGender.Unknown, VndbProjectLinker.GenderOf(null));

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
    public void Match_deixa_de_fora_o_nome_que_empata_entre_dois_personagens()
    {
        VndbLinkedCharacter[] characters =
        [
            new("c1", "Aoi Sakura", "佐倉 葵", [], CharacterGender.Female, null, "primary"),
            new("c2", "Aoi Mori", "森 葵", [], CharacterGender.Female, null, "side"),
        ];

        Assert.Empty(VndbProjectLinker.Match(["葵"], characters));
        Assert.Equal("c2", VndbProjectLinker.Match(["森葵"], characters).Single().Character.Id);
    }

    [Fact]
    public async Task ApplyCharacters_poe_os_personagens_do_jogo_em_characters_json_com_o_genero_e_o_aviso_some()
    {
        using var folder = new TempFolder();
        var project = NewProject(folder.Path);
        project.SaveText("yst00001", [Name(0, "相沢健太"), Name(1, "佐倉葵"), Name(2, "ナレーター"), Line(3, "相沢健太"), Line(4, "佐倉葵")]);
        var characters = new List<CharacterEntry>(GameCharacters.Names(project.LoadText("yst00001")));
        var before = new GenderRule().Check(Qa(project, characters)).Select(i => i.Message).ToList();
        Assert.Equal(2, before.Count);

        await VndbProjectLinker.LinkAsync(project, Catalog(), VndbSamples.VisualNovelId, TestContext.Current.CancellationToken);

        var saved = project.LoadCharacters();
        Assert.Equal(["相沢健太", "佐倉葵"], saved.Select(c => c.Source));
        Assert.Equal([CharacterGender.Male, CharacterGender.Female], saved.Select(c => c.Gender));
        Assert.Equal(("Kenta Aizawa", "O protagonista."), (saved[0].Romaji, saved[0].Description));
        Assert.Equal(["Ken", "Kenta-kun"], saved[0].Aliases);
        Assert.Empty(new GenderRule().Check(Qa(project, saved)));
    }

    [Fact]
    public async Task ApplyCharacters_nao_sobrescreve_o_que_o_usuario_escreveu_e_so_preenche_o_que_falta()
    {
        using var folder = new TempFolder();
        var project = NewProject(folder.Path);
        project.SaveText("yst00001", [Name(0, "相沢健太")]);
        project.SaveCharacters([new CharacterEntry("相沢健太", "Kenta", CharacterGender.Other, "tímido", "blue", Romaji: "Meu romaji")]);
        project.SaveVndbLink(await VndbProjectLinker.BuildAsync(Catalog(), VndbSamples.VisualNovelId, TestContext.Current.CancellationToken));

        var result = VndbProjectLinker.ApplyCharacters(project);

        var entry = Assert.Single(project.LoadCharacters());
        Assert.Equal(new VndbApplyResult(1, 0, 1), result);
        Assert.Equal(("Kenta", CharacterGender.Other, "tímido", "blue", "Meu romaji"), (entry.Target, entry.Gender, entry.Tone, entry.Color, entry.Romaji));
        Assert.Equal(["Ken", "Kenta-kun"], entry.Aliases);
        Assert.Equal("O protagonista.", entry.Description);
    }

    [Fact]
    public async Task ApplyCharacters_repetida_nao_muda_nada()
    {
        using var folder = new TempFolder();
        var project = NewProject(folder.Path);
        project.SaveText("yst00001", [Name(0, "相沢健太")]);
        await VndbProjectLinker.LinkAsync(project, Catalog(), VndbSamples.VisualNovelId, TestContext.Current.CancellationToken);
        var written = File.ReadAllText(project.CharactersPath);

        var result = VndbProjectLinker.ApplyCharacters(project);

        Assert.Equal(new VndbApplyResult(1, 0, 0), result);
        Assert.Equal(written, File.ReadAllText(project.CharactersPath));
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
    public async Task Extract_de_projeto_ligado_ao_vndb_leva_os_personagens_para_characters_json()
    {
        using var folder = new TempFolder();
        var project = ProjectWithGame(folder.Path, "name|相沢健太\ndialogue|こんにちは");
        project.SaveVndbLink(await VndbProjectLinker.BuildAsync(Catalog(), VndbSamples.VisualNovelId, TestContext.Current.CancellationToken));

        var result = ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new VndbApplyResult(1, 1, 0), result.VndbCharacters);
        Assert.Equal(CharacterGender.Male, Assert.Single(project.LoadCharacters()).Gender);
    }

    [Fact]
    public void Extract_de_projeto_sem_vndb_nao_cria_characters_json()
    {
        using var folder = new TempFolder();
        var project = ProjectWithGame(folder.Path, "name|相沢健太");

        var result = ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result.VndbCharacters);
        Assert.False(File.Exists(project.CharactersPath));
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

    [Fact]
    public async Task Extract_com_characters_json_ilegivel_termina_a_extracao_avisa_e_nao_mexe_no_arquivo()
    {
        using var folder = new TempFolder();
        var project = ProjectWithGame(folder.Path, "name|相沢健太");
        project.SaveVndbLink(await VndbProjectLinker.BuildAsync(Catalog(), VndbSamples.VisualNovelId, TestContext.Current.CancellationToken));
        File.WriteAllText(project.CharactersPath, "{ nao e json");

        var result = ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result.VndbCharacters);
        Assert.Contains(result.Warnings, w => w.Contains("characters.json", StringComparison.Ordinal));
        Assert.Equal("{ nao e json", File.ReadAllText(project.CharactersPath));
    }

    [Fact]
    public async Task ApplyCharacters_com_characters_json_ilegivel_recusa_e_deixa_o_arquivo_intacto()
    {
        using var folder = new TempFolder();
        var project = NewProject(folder.Path);
        project.SaveText("yst00001", [Name(0, "相沢健太")]);
        project.SaveVndbLink(await VndbProjectLinker.BuildAsync(Catalog(), VndbSamples.VisualNovelId, TestContext.Current.CancellationToken));
        File.WriteAllText(project.CharactersPath, "{ nao e json");

        Assert.Throws<GlossaryFileException>(() => VndbProjectLinker.ApplyCharacters(project));

        Assert.Equal("{ nao e json", File.ReadAllText(project.CharactersPath));
    }

    private static QaContext Qa(ProjectFolder project, IReadOnlyList<CharacterEntry> characters) =>
        new(project.LoadAllText(), [], characters, FormattingProfile.Default);
}
