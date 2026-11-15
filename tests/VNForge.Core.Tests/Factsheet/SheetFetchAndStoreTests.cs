using System.Text.Json.Nodes;
using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;
using VNForge.Vndb;
using static VNForge.Core.Tests.Factsheet.SheetSamples;

namespace VNForge.Core.Tests.Factsheet;

[Trait("Risco", "R7")]
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
    public void Termo_de_tipo_desconhecido_e_ignorado_na_leitura_e_volta_intacto_na_gravacao()
    {
        using var folder = new TempFolder();
        var store = new SheetStore(folder.Path);
        var terms = new SheetTerms();
        terms.Ensure(SheetTermKind.Tag, "g1", "Comedy", null);
        store.SaveTerms(terms);
        var file = JsonNode.Parse(File.ReadAllText(store.TermsPath))!;
        file["data"]!["terms"]!.AsArray().Add(JsonNode.Parse("{\"kind\":\"future\",\"id\":\"z9\",\"source\":\"Novo\"}"));
        File.WriteAllText(store.TermsPath, file.ToJsonString());

        var loaded = store.LoadTerms();
        store.SaveTerms(loaded);

        Assert.Equal(["tag:g1"], loaded.All.Select(t => SheetTerms.KeyOf(t.Kind, t.Id)));
        Assert.Contains("\"future\"", File.ReadAllText(store.TermsPath), StringComparison.Ordinal);
    }
}
