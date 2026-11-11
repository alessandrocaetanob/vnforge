using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;
using VNForge.Vndb;
using static VNForge.Core.Tests.Factsheet.SheetSamples;

namespace VNForge.Core.Tests.Factsheet;

public sealed class SheetTranslationTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public SheetTranslationTests() => Store = new SheetStore(_folder.Path);

    private SheetStore Store { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    private async Task Fetch(FakeVndbCatalog? catalog = null, string id = Game1) =>
        await SheetFetcher.FetchAsync(catalog ?? Catalog(), Store, id, cancellationToken: Ct);

    [Fact]
    public async Task Rodar_de_novo_nao_pede_nada_e_o_plano_diz_o_que_falta()
    {
        await Fetch();
        var runner = new SheetTranslationRunner(new StubSheetTranslator(), Store);
        var plan = runner.Plan(Game1);
        Assert.Equal((1, 3, 3, 4), (plan.Count(SheetItemKind.Synopsis), plan.Count(SheetItemKind.CharacterDescription), plan.Count(SheetItemKind.TagName), plan.Count(SheetItemKind.TraitName)));
        await runner.RunAsync(Game1, cancellationToken: Ct);
        var again = new StubSheetTranslator();

        var result = await new SheetTranslationRunner(again, Store).RunAsync(Game1, cancellationToken: Ct);

        Assert.Equal((0, 0), (result.Calls, result.Translated));
        Assert.Empty(again.Requests);
        Assert.True(runner.Plan(Game1).IsEmpty);
    }

    [Fact]
    public async Task Item_que_a_IA_nao_devolveu_ou_perdeu_o_spoiler_fica_pendente_e_os_outros_sao_gravados()
    {
        await Fetch();
        var translator = new StubSheetTranslator(i => i.Key switch
        {
            "synopsis" => "A sinopse sem marcador",
            "character:c1" => null,
            _ => "PT: " + i.Source,
        });

        var result = await new SheetTranslationRunner(translator, Store).RunAsync(Game1, cancellationToken: Ct);

        Assert.Equal(2, result.Failed);
        Assert.Contains(result.Failures, f => f.Keys.SequenceEqual(["synopsis"]) && f.Message.Contains("spoiler", StringComparison.Ordinal));
        Assert.Contains(result.Failures, f => f.Keys.SequenceEqual(["character:c1"]));
        Assert.Equal(TranslationStatus.Untranslated, Store.LoadGame(Game1)!.Synopsis!.Status);
        Assert.Equal(TranslationStatus.Untranslated, Store.LoadCharacter("c1")!.Description!.Status);
        Assert.Equal(TranslationStatus.Machine, Store.LoadCharacter("c2")!.Description!.Status);
        Assert.Equal(["synopsis", "character:c1"], new SheetTranslationRunner(translator, Store).Plan(Game1).Items.Select(i => i.Key));
    }

    [Fact]
    public async Task Lote_que_falha_nao_perde_os_outros_lotes()
    {
        await Fetch();
        var calls = 0;
        var translator = new ThrowingOnceTranslator(() => calls++ == 0);

        var result = await new SheetTranslationRunner(translator, Store).RunAsync(Game1, cancellationToken: Ct);

        var failure = Assert.Single(result.Failures);
        Assert.Contains("recusou", failure.Message, StringComparison.Ordinal);
        Assert.Equal(11 - failure.Keys.Count, result.Translated);
        Assert.True(result.Calls >= 2);
    }

    [Fact]
    public async Task O_que_a_pessoa_escreveu_nao_e_refeito_nem_com_force_mas_o_da_IA_e()
    {
        await Fetch();
        await new SheetTranslationRunner(new StubSheetTranslator(), Store).RunAsync(Game1, cancellationToken: Ct);
        SheetEditor.Edit(Store, Game1, SheetField.OfCharacter("c2"), "Amiga de infância (minha versão).");
        SheetEditor.Review(Store, Game1, [SheetField.OfTag("g10")]);
        var translator = new StubSheetTranslator(i => "NOVO: " + i.Source);

        var result = await new SheetTranslationRunner(translator, Store).RunAsync(Game1, new SheetTranslationOptions { Force = true }, cancellationToken: Ct);

        Assert.Equal("Amiga de infância (minha versão).", Store.LoadCharacter("c2")!.Description!.Target);
        Assert.Equal("PT: Comedy", Store.LoadTerms().Find(SheetTermKind.Tag, "g10")!.Text.Target);
        Assert.Equal("NOVO: The hero. [spoiler]He is a ghost.[/spoiler]", Store.LoadCharacter("c1")!.Description!.Target);
        Assert.Equal(9, result.Translated);
    }

    [Fact]
    public async Task Divide_em_lotes_de_nomes_e_de_textos()
    {
        await Fetch();
        var translator = new StubSheetTranslator();

        await new SheetTranslationRunner(translator, Store).RunAsync(Game1, new SheetTranslationOptions { NamesPerCall = 3, TextsPerCall = 2 }, cancellationToken: Ct);

        var sizes = translator.Requests.Select(r => r.Items.Count).ToList();
        Assert.Equal([3, 3, 1, 2, 2], sizes);
        Assert.All(translator.Requests, r => Assert.True(r.Items.All(i => i.IsProse) || r.Items.All(i => !i.IsProse)));
    }

    [Fact]
    public void Traduzir_jogo_sem_ficha_manda_buscar_primeiro()
    {
        var ex = Assert.Throws<LibraryException>(() => new SheetTranslationRunner(new StubSheetTranslator(), Store).Plan(Game1));

        Assert.Contains("sheet fetch", ex.Message, StringComparison.Ordinal);
    }

    private sealed class ThrowingOnceTranslator(Func<bool> fail) : ISheetTranslator
    {
        public Task<IReadOnlyList<SheetTranslation>> TranslateAsync(SheetTranslationRequest request, CancellationToken ct) =>
            fail()
                ? throw new InvalidOperationException("A IA recusou o pedido.")
                : Task.FromResult<IReadOnlyList<SheetTranslation>>([.. request.Items.Select(i => new SheetTranslation(i.Key, "PT: " + i.Source))]);
    }
}
