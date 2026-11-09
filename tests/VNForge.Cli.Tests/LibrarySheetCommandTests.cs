using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.AI.Sheet;
using VNForge.Core.Factsheet;
using VNForge.Core.Glossary;
using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Cli.Tests;

/// <summary>#554: <c>vnf library sheet</c> busca, traduz com o provedor falso, revisa, mostra com o spoiler escondido e leva a ficha ao projeto.</summary>
public sealed class LibrarySheetCommandTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static VndbVisualNovel Game() =>
        new("v4", "CLANNAD", "クラナド", [], [], "ja", 2, "2004-04-28", ["ja"], ["win"], null, 4, 2700, 80,
            "A town. [spoiler]She dies.[/spoiler] Tomoya [b]stays[/b].", [new VndbTag("g10", "Comedy", "cont", 2.5, 0, false), new VndbTag("g11", "Plot Twist", "cont", 2.0, 2, false)]);

    private static VndbCharacter Tomoya() =>
        new("c1", "Tomoya Okazaki", "岡崎 朋也", [], "The hero. [spoiler]He is a ghost.[/spoiler]", null, new VndbGender("m", "m"),
            [new VndbCharacterAppearance("v4", "main", 0, null)], [new VndbTrait("i1", "Long Hair", "i10", "Hair", 0, false, false), new VndbTrait("i2", "Amnesia", "i20", "Personality", 1, false, false)]);

    private static VndbCharacter Sister() =>
        new("c2", "Ghost Sister", null, [], "Who is she?", new VndbImage("https://t.vndb.org/ch/2.jpg", null, 10, 10, 1.6, 0), new VndbGender("f", "f"),
            [new VndbCharacterAppearance("v4", "side", 2, null)], []);

    private FakeSheetTranslator Prepare(bool confirmed = true)
    {
        var game = new LibraryGame("g1", "C:\\Jogos\\CLANNAD", null, LibraryEngine.Unknown, "CLANNAD", null, T0);
        if (confirmed)
        {
            game = game with { VndbId = "v4", Confirmation = LibraryConfirmation.Manual };
        }

        new LibraryFile(_cli.UserConfigDirectory).Save(new GameLibrary([game]));
        var catalog = new FakeVndbCatalog([Game()], [Tomoya(), Sister()]);
        _cli.Cli.VndbCatalogFactory = () => catalog;
        var fake = new FakeSheetTranslator();
        _cli.Cli.SheetTranslatorFactory = (_, _) => fake;
        return fake;
    }

    private async Task<RunResult> Run(params string[] args) => await _cli.RunAsync(args, Ct);

    private static JsonElement Data(RunResult result) => JsonDocument.Parse(result.Output).RootElement.GetProperty("data");

    private SheetStore Store => new(_cli.UserConfigDirectory);

    [Fact]
    public async Task Fetch_traz_a_ficha_do_VNDB_e_diz_o_que_falta_traduzir()
    {
        Prepare();

        var result = await Run("library", "sheet", "fetch", "CLANNAD");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("2 personagens, 2 tags", result.Output, StringComparison.Ordinal);
        Assert.Contains("4 nomes de tag e traço novos", result.Output, StringComparison.Ordinal);
        Assert.Contains("vndb.org", result.Output, StringComparison.Ordinal);
        Assert.Equal(["c1", "c2"], Store.LoadGame("v4")!.CharacterIds);
    }

    [Fact]
    public async Task Revisar_em_lote_diz_quantos_desatualizados_ficaram_de_fora()
    {
        Prepare();
        await Run("library", "sheet", "fetch", "CLANNAD");
        await Run("library", "sheet", "translate", "CLANNAD", "--yes");
        await Run("library", "sheet", "edit", "CLANNAD", "c1", "O herói. [spoiler]Ele é um fantasma.[/spoiler]");
        _cli.Cli.VndbCatalogFactory = () => new FakeVndbCatalog([Game()], [Tomoya() with { Description = "The hero, rewritten." }, Sister()]);
        await Run("library", "sheet", "fetch", "CLANNAD");

        var review = await Run("library", "sheet", "review", "CLANNAD");
        var json = await Run("library", "sheet", "review", "CLANNAD", "--json");

        Assert.Contains("6 textos revisados; 0 ainda não tinham tradução ou já estavam revisados.", review.Output, StringComparison.Ordinal);
        Assert.Contains("1 texto desatualizado ficou de fora", review.Output, StringComparison.Ordinal);
        Assert.Equal(1, Data(json).GetProperty("skippedOutdated").GetInt32());
        Assert.True(Store.LoadCharacter("c1")!.Description!.Outdated);
    }

    [Fact]
    public async Task Jogo_sem_o_VNDB_confirmado_manda_casar_antes()
    {
        Prepare(confirmed: false);

        var result = await Run("library", "sheet", "fetch", "CLANNAD");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("vnf library match", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Traduz_com_o_provedor_falso_e_o_show_mostra_a_traducao_com_o_spoiler_escondido()
    {
        var fake = Prepare();
        await Run("library", "sheet", "fetch", "CLANNAD");

        var translate = await Run("library", "sheet", "translate", "CLANNAD", "--yes");
        var show = await Run("library", "sheet", "show", "CLANNAD");

        Assert.Equal(0, translate.ExitCode);
        Assert.Contains("traduzidos pela IA", translate.Output, StringComparison.Ordinal);
        Assert.NotEmpty(fake.Requests);
        Assert.Equal(0, show.ExitCode);
        Assert.Contains("Sinopse (traduzido pela IA)", show.Output, StringComparison.Ordinal);
        Assert.Contains("[pt-BR] A town. [spoiler escondido] Tomoya stays.", show.Output, StringComparison.Ordinal);
        Assert.Contains("[pt-BR] Comedy", show.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("She dies", show.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Plot Twist", show.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Ghost Sister", show.Output, StringComparison.Ordinal);
        Assert.Contains("mais 1 escondido por spoiler", show.Output, StringComparison.Ordinal);
        Assert.Contains("Tomoya Okazaki (岡崎 朋也) · protagonista · masculino", show.Output, StringComparison.Ordinal);
        Assert.Contains("use --show-spoilers", show.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task So_com_o_pedido_o_spoiler_e_o_conteudo_adulto_aparecem()
    {
        Prepare();
        await Run("library", "sheet", "fetch", "CLANNAD");
        await Run("library", "sheet", "translate", "CLANNAD", "--yes");

        var shown = await Run("library", "sheet", "show", "CLANNAD", "--show-spoilers");
        var adult = await Run("library", "sheet", "show", "CLANNAD", "--show-spoilers", "--show-adult");

        Assert.Contains("She dies.", shown.Output, StringComparison.Ordinal);
        Assert.Contains("Ghost Sister", shown.Output, StringComparison.Ordinal);
        Assert.Contains("Plot Twist", shown.Output, StringComparison.Ordinal);
        Assert.Contains("Retrato: escondido (conteúdo adulto; use --show-adult)", shown.Output, StringComparison.Ordinal);
        Assert.Contains("Retrato: https://t.vndb.org/ch/2.jpg", adult.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_privacidade_da_biblioteca_tambem_vale_na_ficha()
    {
        Prepare();
        await Run("library", "sheet", "fetch", "CLANNAD");
        await Run("library", "privacy", "--spoilers", "sim");

        var show = await Run("library", "sheet", "show", "CLANNAD");

        Assert.Contains("Ghost Sister", show.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_json_nao_vaza_o_que_esta_escondido()
    {
        Prepare();
        await Run("library", "sheet", "fetch", "CLANNAD");

        var result = await Run("library", "sheet", "show", "CLANNAD", "--json");

        Assert.DoesNotContain("She dies", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Ghost Sister", result.Output, StringComparison.Ordinal);
        var sheet = Data(result).GetProperty("sheet");
        Assert.Equal(1, sheet.GetProperty("hiddenCharacters").GetInt32());
        Assert.Equal(1, sheet.GetProperty("characters").GetArrayLength());
    }

    [Fact]
    public async Task Dry_run_estima_sem_chamar_a_IA_e_sem_gravar()
    {
        var fake = Prepare();
        await Run("library", "sheet", "fetch", "CLANNAD");

        var result = await Run("library", "sheet", "translate", "CLANNAD", "--dry-run", "--json");

        Assert.Equal(0, result.ExitCode);
        var data = Data(result);
        Assert.True(data.GetProperty("dryRun").GetBoolean());
        Assert.Equal(1, data.GetProperty("pending").GetProperty("synopsis").GetInt32());
        Assert.Equal(2, data.GetProperty("pending").GetProperty("characters").GetInt32());
        Assert.Equal(2, data.GetProperty("pending").GetProperty("tags").GetInt32());
        Assert.Equal(2, data.GetProperty("pending").GetProperty("traits").GetInt32());
        Assert.True(data.GetProperty("estimate").GetProperty("dollars").GetDecimal() > 0);
        Assert.Empty(fake.Requests);
        Assert.Equal(TranslationStatus.Untranslated, Store.LoadGame("v4")!.Synopsis!.Status);
    }

    [Fact]
    public async Task Traduzir_de_novo_nao_pede_nada_a_IA()
    {
        var fake = Prepare();
        await Run("library", "sheet", "fetch", "CLANNAD");
        await Run("library", "sheet", "translate", "CLANNAD", "--yes");
        var calls = fake.Requests.Count;

        var again = await Run("library", "sheet", "translate", "CLANNAD", "--yes");

        Assert.Equal(0, again.ExitCode);
        Assert.Contains("nada a traduzir", again.Output, StringComparison.Ordinal);
        Assert.Equal(calls, fake.Requests.Count);
    }

    [Fact]
    public async Task Sem_chave_e_sem_provedor_falso_pede_a_chave_em_vez_de_chamar_a_rede()
    {
        Prepare();
        _cli.Cli.SheetTranslatorFactory = null;
        await Run("library", "sheet", "fetch", "CLANNAD");

        var result = await Run("library", "sheet", "translate", "CLANNAD", "--yes");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("ANTHROPIC_API_KEY", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Item_que_a_IA_perde_deixa_o_comando_com_falha_e_pendente_para_tentar_de_novo()
    {
        Prepare();
        _cli.Cli.SheetTranslatorFactory = (_, _) => new FakeSheetTranslator(i => i.Key == "synopsis" ? "sem marcador" : "ok: " + i.Source);
        await Run("library", "sheet", "fetch", "CLANNAD");

        var result = await Run("library", "sheet", "translate", "CLANNAD", "--yes");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("spoiler", result.Output, StringComparison.Ordinal);
        Assert.Contains("pendente", result.Output, StringComparison.Ordinal);
        Assert.Equal(TranslationStatus.Untranslated, Store.LoadGame("v4")!.Synopsis!.Status);
    }

    [Fact]
    public async Task Revisa_aprova_e_corrige_cada_texto_como_uma_fala()
    {
        Prepare();
        await Run("library", "sheet", "fetch", "CLANNAD");
        await Run("library", "sheet", "translate", "CLANNAD", "--yes");

        var early = await Run("library", "sheet", "approve", "CLANNAD", "synopsis");
        var edit = await Run("library", "sheet", "edit", "CLANNAD", "c1", "O herói. [spoiler]Ele é um fantasma.[/spoiler]");
        var review = await Run("library", "sheet", "review", "CLANNAD");
        var approve = await Run("library", "sheet", "approve", "CLANNAD");
        var show = await Run("library", "sheet", "show", "CLANNAD", "--ids");

        Assert.Equal(1, early.ExitCode);
        Assert.Contains("Revise", early.Error, StringComparison.Ordinal);
        Assert.Equal(0, edit.ExitCode);
        Assert.Contains("traduzido à mão", edit.Output, StringComparison.Ordinal);
        Assert.Contains("7 textos revisados", review.Output, StringComparison.Ordinal);
        Assert.Contains("7 textos aprovados", approve.Output, StringComparison.Ordinal);
        Assert.Contains("[synopsis]", show.Output, StringComparison.Ordinal);
        Assert.Contains("[character:c1]", show.Output, StringComparison.Ordinal);
        Assert.Contains("[tag:g10]", show.Output, StringComparison.Ordinal);
        Assert.Contains("0 sem tradução, 0 traduzidos pela IA, 0 à mão, 0 revisados, 7 aprovados", show.Output, StringComparison.Ordinal);
        Assert.Contains("O herói. [spoiler escondido]", show.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Texto_que_nao_e_da_ficha_ou_nome_invalido_recusa_em_portugues()
    {
        Prepare();
        await Run("library", "sheet", "fetch", "CLANNAD");

        var missing = await Run("library", "sheet", "review", "CLANNAD", "c99");
        var invalid = await Run("library", "sheet", "review", "CLANNAD", "qualquer");

        Assert.Equal(1, missing.ExitCode);
        Assert.Contains("c99 não está na ficha", missing.Error, StringComparison.Ordinal);
        Assert.Equal(1, invalid.ExitCode);
        Assert.Contains("não é um texto da ficha", invalid.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_leva_os_personagens_e_a_sinopse_revisados_para_o_projeto_sem_spoiler()
    {
        Prepare();
        await Run("library", "sheet", "fetch", "CLANNAD");
        await Run("library", "sheet", "translate", "CLANNAD", "--yes");
        var project = NewProject();

        var early = await Run("library", "sheet", "apply", "CLANNAD", "--project", project.Root, "--json");
        await Run("library", "sheet", "review", "CLANNAD");
        var result = await Run("library", "sheet", "apply", "CLANNAD", "--project", project.Root, "--json");

        Assert.Equal(1, Data(early).GetProperty("matched").GetInt32());
        Assert.False(Data(early).GetProperty("synopsisApplied").GetBoolean());
        Assert.True(Data(early).GetProperty("notReady").GetInt32() > 0);
        var data = Data(result);
        Assert.True(data.GetProperty("synopsisApplied").GetBoolean());
        Assert.Equal(0, data.GetProperty("notReady").GetInt32());
        var saved = project.LoadCharacters().ToDictionary(c => c.Source);
        Assert.Equal("[pt-BR] The hero.", saved["岡崎朋也"].Description);
        Assert.Equal(CharacterGender.Male, saved["岡崎朋也"].Gender);
        Assert.Equal("[pt-BR] A town. Tomoya stays.", project.LoadBible().Synopsis);
        Assert.DoesNotContain("dies", File.ReadAllText(project.BiblePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_sem_projeto_ligado_pede_o_projeto()
    {
        Prepare();
        await Run("library", "sheet", "fetch", "CLANNAD");

        var result = await Run("library", "sheet", "apply", "CLANNAD");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--project", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_de_jogo_sem_ficha_manda_buscar()
    {
        Prepare();

        var result = await Run("library", "sheet", "show", "CLANNAD");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("vnf library sheet fetch", result.Error, StringComparison.Ordinal);
    }

    private ProjectFolder NewProject()
    {
        var project = ProjectFolder.Create(Path.Combine(_cli.Root, "projeto"), new ProjectConfig(
            "CLANNAD",
            new GameConfig("D:/Jogos/CLANNAD", "CLANNAD.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        project.SaveText("yst00001", [new TextUnit("yst00001:000000", TextUnitKind.Name, null, "岡崎朋也", null, TranslationStatus.Untranslated)]);
        return project;
    }
}
