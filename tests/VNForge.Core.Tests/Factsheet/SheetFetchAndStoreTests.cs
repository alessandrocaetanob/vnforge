using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;
using VNForge.Vndb;
using static VNForge.Core.Tests.Factsheet.SheetSamples;

namespace VNForge.Core.Tests.Factsheet;

public sealed class SheetFetchAndStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Jogo_que_o_VNDB_nao_tem_recusa_em_portugues()
    {
        using var folder = new TempFolder();

        var ex = await Assert.ThrowsAsync<LibraryException>(() => SheetFetcher.FetchAsync(Catalog(), new SheetStore(folder.Path), "v1", cancellationToken: Ct));

        Assert.Contains("v1", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../v1")]
    [InlineData("v")]
    public void Id_que_nao_e_de_jogo_nao_vira_nome_de_arquivo(string id)
    {
        using var folder = new TempFolder();

        Assert.Throws<LibraryException>(() => new SheetStore(folder.Path).LoadGame(id));
    }

    [Fact]
    public void Arquivo_ilegivel_e_recusado_citando_o_caminho_e_nunca_sobrescrito()
    {
        using var folder = new TempFolder();
        var store = new SheetStore(folder.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(store.GamePath(Game1))!);
        File.WriteAllText(store.GamePath(Game1), "{ não é json");
        File.WriteAllText(store.TermsPath, "{\"version\":9,\"data\":{\"terms\":[]}}");

        Assert.Contains(Game1, Assert.Throws<LibraryException>(() => store.LoadGame(Game1)).Message, StringComparison.Ordinal);
        Assert.Contains("versão 9", Assert.Throws<LibraryException>(store.LoadTerms).Message, StringComparison.Ordinal);
        Assert.Equal("{ não é json", File.ReadAllText(store.GamePath(Game1)));
    }

    [Fact]
    public async Task Sem_arquivo_a_ficha_nao_existe_e_gravar_nao_deixa_temporario()
    {
        using var folder = new TempFolder();
        var store = new SheetStore(folder.Path);
        Assert.Null(store.LoadGame(Game1));
        Assert.Empty(store.LoadTerms().All);
        Assert.Empty(store.LoadAllCharacters());

        await SheetFetcher.FetchAsync(Catalog(), store, Game1, cancellationToken: Ct);

        Assert.DoesNotContain(Directory.EnumerateFiles(store.Root, "*.tmp", SearchOption.AllDirectories), _ => true);
        Assert.Equal(3, store.LoadAllCharacters().Count);
    }
}
