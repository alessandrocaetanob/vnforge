using System.Text.Json;
using VNForge.Core.Library;

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
    public async Task Export_sem_jogo_confirmado_explica_o_que_fazer_e_nao_grava()
    {
        var result = await Run("library", "export");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("vnf library match", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Target));
    }
}
