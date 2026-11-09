using System.Text.Json;
using VNForge.Vndb;

namespace VNForge.Cli.Tests;

public sealed class VndbCommandTests : IDisposable
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
    public async Task Set_confere_o_token_no_VNDB_e_guarda_no_cofre_sem_repeti_lo()
    {
        var fake = UseFake();
        _cli.SetInput(Secret + "\n");

        var result = await _cli.RunAsync(["vndb", "token", "set"], Ct);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Secret, _cli.VndbTokens.Load());
        Assert.Contains("cofre do Windows", result.Output, StringComparison.Ordinal);
        Assert.Contains("fake (u1)", result.Output, StringComparison.Ordinal);
        Assert.Contains("listread, listwrite", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.Output + result.Error, StringComparison.Ordinal);
        Assert.Equal(["GetAuthInfoAsync(***)"], fake.Calls);
    }

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

    [Fact]
    public async Task Set_com_token_recusado_pelo_VNDB_nao_guarda_nada()
    {
        UseFake();
        _cli.SetInput("token-errado\n");

        var result = await _cli.RunAsync(["vndb", "token", "set"], Ct);

        Assert.Equal(1, result.ExitCode);
        Assert.Null(_cli.VndbTokens.Load());
        Assert.DoesNotContain("token-errado", result.Output + result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("com espaço no meio")]
    public async Task Set_com_entrada_vazia_ou_com_espacos_e_erro_de_uso(string line)
    {
        UseFake();
        _cli.SetInput(line + "\n");

        var result = await _cli.RunAsync(["vndb", "token", "set"], Ct);

        Assert.Equal(1, result.ExitCode);
        Assert.Null(_cli.VndbTokens.Load());
    }

    [Fact]
    public async Task Clear_apaga_o_cofre_e_avisa_quando_a_variavel_continua_valendo()
    {
        _cli.VndbTokens.Save(Secret);
        _cli.Environment["VNDB_TOKEN"] = "do-ambiente";

        var result = await _cli.RunAsync(["vndb", "token", "clear"], Ct);

        Assert.Equal(0, result.ExitCode);
        Assert.Null(_cli.VndbTokens.Load());
        Assert.Contains("Token apagado", result.Output, StringComparison.Ordinal);
        Assert.Contains("VNDB_TOKEN continua definida", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("do-ambiente", result.Output, StringComparison.Ordinal);

        var again = await _cli.RunAsync(["vndb", "token", "clear"], Ct);
        Assert.Contains("Não havia token", again.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_usa_a_variavel_de_ambiente_antes_do_cofre()
    {
        UseFake();
        _cli.VndbTokens.Save("token-do-cofre-que-nao-vale");
        _cli.Environment["VNDB_TOKEN"] = Secret;

        var result = await _cli.RunAsync(["vndb", "token", "check", "--json"], Ct);

        Assert.Equal(0, result.ExitCode);
        var data = Data(result);
        Assert.True(data.GetProperty("valid").GetBoolean());
        Assert.Equal("environment", data.GetProperty("source").GetString());
        Assert.Equal("u1", data.GetProperty("account").GetProperty("userId").GetString());
        Assert.DoesNotContain(Secret, result.Output + result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_sem_token_diz_como_configurar()
    {
        UseFake();

        var result = await _cli.RunAsync(["vndb", "token", "check"], Ct);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("vnf vndb token set", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_com_token_do_cofre_mostra_a_origem_e_as_permissoes()
    {
        UseFake();
        _cli.VndbTokens.Save(Secret);

        var result = await _cli.RunAsync(["vndb", "token", "check"], Ct);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Token válido (cofre do Windows (DPAPI))", result.Output, StringComparison.Ordinal);
        Assert.Contains("Permissões: listread, listwrite.", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.Output + result.Error, StringComparison.Ordinal);
    }
}
