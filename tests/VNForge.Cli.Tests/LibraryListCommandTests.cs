using System.Text.Json;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R5")]
public sealed class LibraryListCommandTests : IDisposable
{
    private const string Secret = FakeVndbCatalog.ValidToken;

    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LibraryFile File => new(_cli.UserConfigDirectory);

    private static VndbListEntry Entry(string id, int? vote, DateTimeOffset lastModified, params int[] labels) =>
        new(id, "Jogo", T0, lastModified, null, vote, null, null, null, [.. labels.Select(LibraryListLabels.Named)]);

    private FakeVndbCatalog Prepare(IEnumerable<VndbListEntry>? remote = null, bool withToken = true)
    {
        var games = new[]
        {
            new LibraryGame("g1", "C:\\Jogos\\CLANNAD", null, LibraryEngine.Unknown, "CLANNAD", null, T0) { VndbId = "v4", Confirmation = LibraryConfirmation.Manual },
            new LibraryGame("g2", "C:\\Jogos\\Outro", null, LibraryEngine.Unknown, "Outro", null, T0),
        };
        File.Save(new GameLibrary(games));
        var fake = new FakeVndbCatalog(list: remote ?? []);
        _cli.Cli.VndbUserListFactory = () => fake;
        if (withToken)
        {
            _cli.Environment["VNDB_TOKEN"] = Secret;
        }

        return fake;
    }

    private async Task<RunResult> Run(params string[] args) => await _cli.RunAsync(args, Ct);

    private static JsonElement Data(RunResult result) => JsonDocument.Parse(result.Output).RootElement.GetProperty("data");

    private static bool Updated(FakeVndbCatalog fake) => fake.Calls.Any(c => c.StartsWith("UpdateEntryAsync", StringComparison.Ordinal));

    private LibraryGame Saved() => File.Load().Find("g1");

    private void SeedDeletedOnSite(bool edited = true)
    {
        var fields = new LibraryListFields { Vote = 70, Labels = [LibraryListLabels.Named(2)] };
        var game = new LibraryGame("g1", "C:\\Jogos\\CLANNAD", null, LibraryEngine.Unknown, "CLANNAD", null, T0)
        {
            VndbId = "v4",
            Confirmation = LibraryConfirmation.Manual,
            List = new LibraryListState("v4", edited ? fields with { Vote = 95 } : fields, T0.AddDays(3)) { Base = fields, SyncedAt = T0, OnVndb = true },
        };
        File.Save(new GameLibrary([game]));
    }

    [Fact]
    public async Task Sync_com_token_recusado_falha_sem_mostrar_o_token()
    {
        Prepare();
        _cli.Environment["VNDB_TOKEN"] = "token-vencido";

        var result = await Run("library", "sync");

        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain("token-vencido", result.Output + result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ficha_e_json_do_jogo_trazem_a_lista_e_o_token_nao_vai_para_o_arquivo_da_biblioteca()
    {
        Prepare();
        await Run("library", "status", "CLANNAD", "--label", "terminado", "--vote", "8");

        var sheet = await Run("library", "list", "CLANNAD");
        var json = await Run("library", "list", "CLANNAD", "--json");

        Assert.Contains("Rótulos: Terminado", sheet.Output, StringComparison.Ordinal);
        var list = Data(json).GetProperty("game").GetProperty("list");
        Assert.Equal(80, list.GetProperty("vote").GetInt32());
        Assert.True(list.GetProperty("pending").GetBoolean());
        Assert.Equal("Terminado", list.GetProperty("labels")[0].GetProperty("name").GetString());
        var raw = System.IO.File.ReadAllText(File.Path);
        Assert.DoesNotContain(Secret, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("token", raw, StringComparison.OrdinalIgnoreCase);
    }
}
