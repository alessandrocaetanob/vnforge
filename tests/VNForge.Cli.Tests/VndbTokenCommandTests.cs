using System.Text.Json;
using VNForge.Vndb;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R5")]
public sealed class VndbTokenCommandTests : IDisposable
{
    private const string Secret = FakeVndbCatalog.ValidToken;

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeVndbCatalog UseFake()
    {
        var fake = new FakeVndbCatalog();
        _cli.Cli.VndbUserListFactory = () => fake;
        return fake;
    }

    private static JsonElement Data(RunResult result) => JsonDocument.Parse(result.Output).RootElement.GetProperty("data");

    [Fact]
    public async Task Set_com_json_devolve_a_conta_e_nunca_o_token()
    {
        UseFake();
        _cli.SetInput(Secret + "\n");

        var result = await _cli.RunAsync(["vndb", "token", "set", "--json"], Ct);

        Assert.Equal(0, result.ExitCode);
        var data = Data(result);
        Assert.True(data.GetProperty("saved").GetBoolean());
        Assert.True(data.GetProperty("account").GetProperty("canWrite").GetBoolean());
        Assert.Equal("vault", data.GetProperty("source").GetString());
        Assert.DoesNotContain(Secret, result.Output + result.Error, StringComparison.Ordinal);
    }
}
