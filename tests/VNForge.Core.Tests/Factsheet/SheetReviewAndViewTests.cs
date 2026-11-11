using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;
using VNForge.Core.Vndb;
using static VNForge.Core.Tests.Factsheet.SheetSamples;

namespace VNForge.Core.Tests.Factsheet;

public sealed class SheetReviewAndViewTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public SheetReviewAndViewTests() => Store = new SheetStore(_folder.Path);

    private SheetStore Store { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    private async Task Translated()
    {
        await SheetFetcher.FetchAsync(Catalog(), Store, Game1, cancellationToken: Ct);
        await new SheetTranslationRunner(new StubSheetTranslator(), Store).RunAsync(Game1, cancellationToken: Ct);
    }

    private GameSheetCard Card(LibraryPrivacy? privacy = null)
    {
        var game = Store.LoadGame(Game1)!;
        return SheetViewer.Card(game, Store.LoadCharacters(game.CharacterIds), Store.LoadTerms(), privacy ?? new LibraryPrivacy());
    }

    [Fact]
    public async Task Revisar_tudo_e_depois_aprovar_tudo_leva_cada_texto_de_uma_etapa_a_outra()
    {
        await Translated();

        Assert.Equal(new SheetStatusCounts(0, 11, 0, 0, 0), Card().Counts);
        Assert.Equal(new SheetMarkResult(11, 0), SheetEditor.Review(Store, Game1));
        Assert.Equal(new SheetStatusCounts(0, 0, 0, 11, 0), Card().Counts);
        Assert.Equal(new SheetMarkResult(11, 0), SheetEditor.Approve(Store, Game1));
        Assert.Equal(new SheetStatusCounts(0, 0, 0, 0, 11), Card().Counts);
        Assert.Equal(new SheetMarkResult(0, 11), SheetEditor.Approve(Store, Game1));
    }

    [Fact]
    public async Task Texto_de_outro_jogo_ou_que_nao_existe_na_ficha_recusa()
    {
        await Translated();

        Assert.Contains("c99", Assert.Throws<LibraryException>(() => SheetEditor.Review(Store, Game1, [SheetField.OfCharacter("c99")])).Message, StringComparison.Ordinal);
        Assert.Throws<LibraryException>(() => SheetEditor.Review(Store, Game1, [SheetField.OfTag("g77")]));
        Assert.Throws<LibraryException>(() => SheetEditor.Review(Store, "v1"));
    }

    [Fact]
    public async Task Conteudo_adulto_some_por_padrao_retrato_tag_erotica_e_traco_sexual()
    {
        await Translated();

        var hidden = Card(new LibraryPrivacy(Adult: AdultMode.Hide));
        var blurred = Card();
        var shown = Card(new LibraryPrivacy(Adult: AdultMode.Show, ShowSpoilers: true));

        var aoi = hidden.Characters[1];
        Assert.Equal((null, true), (aoi.Image, aoi.ImageHidden));
        Assert.NotNull(blurred.Characters[1].Image);
        Assert.True(blurred.Characters[1].ImageHidden);
        Assert.NotNull(hidden.Characters[0].Image);
        Assert.DoesNotContain(hidden.Tags, t => t.Category == "ero");
        Assert.DoesNotContain(hidden.Characters[0].Traits, t => t.Id == Adult.Id);
        Assert.NotNull(shown.Characters[1].Image);
        Assert.Contains(shown.Tags, t => t.Category == "ero");
        Assert.Contains(shown.Characters[0].Traits, t => t.Id == Adult.Id);
    }

    [Fact]
    public async Task A_duracao_sai_em_portugues()
    {
        await SheetFetcher.FetchAsync(Catalog(), Store, Game1, cancellationToken: Ct);

        Assert.Equal("cerca de 25 h", Card().Length);
        Assert.Equal("curta (2 a 10 h)", SheetLabels.Length(null, 2));
        Assert.Equal("45 min", SheetLabels.Length(45, null));
    }

    [Theory]
    [InlineData("c12", SheetFieldKind.Character, "c12")]
    [InlineData("i34", SheetFieldKind.Trait, "i34")]
    public void O_texto_da_ficha_se_le_pelo_nome(string text, SheetFieldKind kind, string? id) =>
        Assert.Equal(new SheetField(kind, id), SheetField.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("tag:c1")]
    [InlineData("personagem")]
    public void Nome_que_nao_e_de_um_texto_da_ficha_recusa_em_portugues(string text) =>
        Assert.Throws<LibraryException>(() => SheetField.Parse(text));
}
