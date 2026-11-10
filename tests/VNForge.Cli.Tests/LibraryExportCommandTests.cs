using System.Text.Json;
using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Text;
using VNForge.Core.Vndb;

namespace VNForge.Cli.Tests;

public sealed class LibraryExportCommandTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Target => Path.Combine(_cli.WorkingDirectory, "catalogo.html");

    private void Prepare(bool adult = false)
    {
        var confirmed = new LibraryGame("g1", "C:\\Jogos\\CLANNAD", null, LibraryEngine.Unknown, "CLANNAD", null, T0)
        {
            VndbId = "v4",
            Confirmation = LibraryConfirmation.Manual,
            Info = new LibraryVndbInfo(
                "CLANNAD",
                "クラナド",
                "2004-04-28",
                "https://t.vndb.org/cv/34/1234.jpg",
                "Uma história. [spoiler]Final secreto.[/spoiler]",
                5,
                null,
                0,
                [new LibraryTag("g1", "Drama", "cont", 3, 0)],
                adult,
                86),
            List = new LibraryListState("v4", new LibraryListFields { Vote = 90, Notes = "Nota privada" }, T0),
        };
        var unmatched = new LibraryGame("g2", "C:\\Jogos\\Outro", null, LibraryEngine.Unknown, "Outro", null, T0);
        new LibraryFile(_cli.UserConfigDirectory).Save(new GameLibrary([confirmed, unmatched]));
    }

    private async Task<RunResult> Run(params string[] args) => await _cli.RunAsync(args, Ct);

    [Fact]
    public async Task Export_grava_o_catalogo_na_pasta_atual_e_conta_o_que_ficou_de_fora()
    {
        Prepare();

        var result = await Run("library", "export");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Target));
        var html = await File.ReadAllTextAsync(Target, Ct);
        Assert.Contains("CLANNAD", html, StringComparison.Ordinal);
        Assert.Contains("Sua nota: 9", html, StringComparison.Ordinal);
        Assert.Contains("Open Database License", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Outro", html, StringComparison.Ordinal);
        Assert.Contains("1 VN gravado", result.Output, StringComparison.Ordinal);
        Assert.Contains("1 jogo ficou de fora", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_respeita_a_privacidade_e_nao_poe_as_notas_de_texto_por_padrao()
    {
        Prepare(adult: true);

        await Run("library", "export");

        var html = await File.ReadAllTextAsync(Target, Ct);
        Assert.DoesNotContain("1234.jpg", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Final secreto", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Nota privada", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_com_as_opcoes_libera_capa_spoiler_e_notas_so_nesta_chamada()
    {
        Prepare(adult: true);

        var result = await Run("library", "export", "--show-adult", "--show-spoilers", "--with-notes");

        Assert.Equal(0, result.ExitCode);
        var html = await File.ReadAllTextAsync(Target, Ct);
        Assert.Contains("1234.jpg", html, StringComparison.Ordinal);
        Assert.Contains("Final secreto", html, StringComparison.Ordinal);
        Assert.Contains("Nota privada", html, StringComparison.Ordinal);
        Assert.False(new LibraryFile(_cli.UserConfigDirectory).Load().Privacy.ShowAdult);
    }

    [Fact]
    public async Task Export_nao_troca_um_arquivo_que_existe_sem_force()
    {
        Prepare();
        await File.WriteAllTextAsync(Target, "antigo", Ct);

        var refused = await Run("library", "export");
        var forced = await Run("library", "export", "--force");

        Assert.NotEqual(0, refused.ExitCode);
        Assert.Contains("--force", refused.Error, StringComparison.Ordinal);
        Assert.Equal(0, forced.ExitCode);
        Assert.Contains("CLANNAD", await File.ReadAllTextAsync(Target, Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_grava_no_arquivo_pedido_e_cria_a_pasta()
    {
        Prepare();
        var path = Path.Combine(_cli.WorkingDirectory, "site", "vns.html");

        var result = await Run("library", "export", "--output", path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Export_com_json_devolve_o_caminho_e_as_contagens()
    {
        Prepare();

        var result = await Run("library", "export", "--json");

        Assert.Equal(0, result.ExitCode);
        var data = JsonDocument.Parse(result.Output).RootElement.GetProperty("data");
        Assert.Equal(Target, data.GetProperty("path").GetString());
        Assert.Equal(1, data.GetProperty("games").GetInt32());
        Assert.Equal(1, data.GetProperty("skipped").GetInt32());
        Assert.False(data.GetProperty("includesNotes").GetBoolean());
    }

    [Fact]
    public async Task Export_usa_a_ficha_traduzida_quando_existe_e_o_texto_do_VNDB_quando_nao()
    {
        Prepare();
        var store = new SheetStore(_cli.UserConfigDirectory);
        store.SaveGame(new GameSheet(
            "v4",
            "CLANNAD",
            null,
            new SheetText("A story.", "Uma história em português.", TranslationStatus.Translated),
            null,
            null,
            0,
            [new LibraryTag("g1", "Drama", "cont", 3, 0)],
            ["c1"],
            false,
            T0));
        store.SaveCharacter(new CharacterSheet("c1", "Tomoya Okazaki", null, [], null, null, new SheetText("The hero.", "O herói da história.", TranslationStatus.Translated), [new VndbTrait("i1", "Long Hair", "i10", "Hair", 0, false, false)], [new SheetAppearance("v4", "main", 0, [new SheetAppearanceEdition(null, "main", 0)])]));
        store.Update(s =>
        {
            var terms = new SheetTerms();
            terms.Ensure(SheetTermKind.Tag, "g1", "Drama", null);
            terms.Replace(SheetTermKind.Tag, "g1", new SheetText("Drama", "Drama em português", TranslationStatus.Translated));
            terms.Ensure(SheetTermKind.Trait, "i1", "Long Hair", "Hair");
            terms.Replace(SheetTermKind.Trait, "i1", new SheetText("Long Hair", "Cabelo comprido", TranslationStatus.Translated));
            s.SaveTerms(terms);
        });

        var result = await Run("library", "export");

        Assert.Equal(0, result.ExitCode);
        var html = await File.ReadAllTextAsync(Target, Ct);
        Assert.Contains("Uma história em português.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("texto original do VNDB", html, StringComparison.Ordinal);
        Assert.Contains("Drama em português", html, StringComparison.Ordinal);
        Assert.Contains("Tomoya Okazaki", html, StringComparison.Ordinal);
        Assert.Contains("O herói da história.", html, StringComparison.Ordinal);
        Assert.Contains("Cabelo comprido", html, StringComparison.Ordinal);
        Assert.Contains("protagonista", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_nao_mostra_o_traco_adulto_da_ficha_sem_a_opcao()
    {
        Prepare();
        var store = new SheetStore(_cli.UserConfigDirectory);
        store.SaveGame(new GameSheet("v4", "CLANNAD", null, null, null, null, 0, [], ["c1"], false, T0));
        store.SaveCharacter(new CharacterSheet(
            "c1",
            "Tomoya Okazaki",
            null,
            [],
            null,
            null,
            null,
            [new VndbTrait("i1", "Long Hair", "i10", "Hair", 0, false, false), new VndbTrait("i2", "Traço adulto", "i20", "Sexual", 0, false, true), new VndbTrait("i3", "Traço antigo", "i20", "Sexual", 0, false, null)],
            [new SheetAppearance("v4", "main", 0, [new SheetAppearanceEdition(null, "main", 0)])]));

        await Run("library", "export");
        var hidden = await File.ReadAllTextAsync(Target, Ct);
        await Run("library", "export", "--force", "--show-adult");
        var shown = await File.ReadAllTextAsync(Target, Ct);

        Assert.Contains("Long Hair", hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("Traço adulto", hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("Traço antigo", hidden, StringComparison.Ordinal);
        Assert.Contains("Traço adulto", shown, StringComparison.Ordinal);
        Assert.Contains("Traço antigo", shown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_recusa_destino_dentro_da_pasta_de_um_jogo_mesmo_com_force()
    {
        var gameFolder = Path.Combine(_cli.WorkingDirectory, "jogos", "CLANNAD");
        Directory.CreateDirectory(gameFolder);
        var existing = Path.Combine(gameFolder, "catalogo.html");
        await File.WriteAllTextAsync(existing, "do jogo", Ct);
        var game = new LibraryGame("g1", gameFolder, null, LibraryEngine.Unknown, "CLANNAD", null, T0)
        {
            VndbId = "v4",
            Confirmation = LibraryConfirmation.Manual,
            Info = new LibraryVndbInfo("CLANNAD", null, null, null, null, null, null, 0, [], false),
        };
        new LibraryFile(_cli.UserConfigDirectory).Save(new GameLibrary([game]));

        var inside = await Run("library", "export", "--output", existing, "--force");
        var nested = await Run("library", "export", "--output", Path.Combine(gameFolder, "novo", "vns.html"));
        var outside = await Run("library", "export", "--output", Path.Combine(_cli.WorkingDirectory, "fora.html"));

        Assert.NotEqual(0, inside.ExitCode);
        Assert.Contains("dentro da pasta do jogo", inside.Error, StringComparison.Ordinal);
        Assert.Contains("CLANNAD", inside.Error, StringComparison.Ordinal);
        Assert.Equal("do jogo", await File.ReadAllTextAsync(existing, Ct));
        Assert.NotEqual(0, nested.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(gameFolder, "novo")));
        Assert.Equal(0, outside.ExitCode);
    }

    [Fact]
    public async Task Export_sem_jogo_confirmado_explica_o_que_fazer_e_nao_grava()
    {
        var result = await Run("library", "export");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("vnf library match", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Target));
    }
}
