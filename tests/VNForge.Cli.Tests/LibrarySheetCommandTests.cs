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
[Trait("Risco", "R5")]
[Trait("Risco", "R9")]
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
    public async Task Sem_chave_e_sem_provedor_falso_pede_a_chave_em_vez_de_chamar_a_rede()
    {
        Prepare();
        _cli.Cli.SheetTranslatorFactory = null;
        await Run("library", "sheet", "fetch", "CLANNAD");

        var result = await Run("library", "sheet", "translate", "CLANNAD", "--yes");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("ANTHROPIC_API_KEY", result.Error, StringComparison.Ordinal);
    }

    private void MakeSheetOld()
    {
        Store.Update(s => s.SaveCharacter(s.LoadCharacter("c1")! with { Appearances = [.. s.LoadCharacter("c1")!.Appearances.Select(a => a with { Editions = null, Spoiler = 0 })] }));
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
