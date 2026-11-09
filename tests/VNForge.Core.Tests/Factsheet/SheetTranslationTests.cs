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
    public async Task Traduz_sinopse_descricoes_tags_e_tracos_e_guarda_como_traduzido_pela_IA()
    {
        await Fetch();
        var translator = new StubSheetTranslator();

        var result = await new SheetTranslationRunner(translator, Store).RunAsync(Game1, cancellationToken: Ct);

        Assert.Equal((11, 0), (result.Translated, result.Failed));
        var game = Store.LoadGame(Game1)!;
        Assert.Equal(("PT: A rainy town. [spoiler]He dies.[/spoiler] Kenta returns.", TranslationStatus.Machine), (game.Synopsis!.Target, game.Synopsis.Status));
        Assert.Equal("PT: The hero. [spoiler]He is a ghost.[/spoiler]", Store.LoadCharacter("c1")!.Description!.Target);
        var terms = Store.LoadTerms();
        Assert.All(terms.All, t => Assert.Equal(TranslationStatus.Machine, t.Text.Status));
        Assert.Equal("PT: Comedy", terms.Find(SheetTermKind.Tag, "g10")!.Text.Target);
    }

    [Fact]
    public async Task Pede_o_contexto_de_cada_item_e_o_titulo_do_jogo()
    {
        await Fetch();
        var translator = new StubSheetTranslator();

        await new SheetTranslationRunner(translator, Store).RunAsync(Game1, cancellationToken: Ct);

        Assert.All(translator.Requests, r => Assert.Equal("Amaoto no Hakoniwa", r.GameTitle));
        var items = translator.Requests.SelectMany(r => r.Items).ToDictionary(i => i.Key);
        Assert.Equal("Kenta Aizawa", items["character:c1"].Context);
        Assert.Equal("cont", items["tag:g10"].Context);
        Assert.Equal("Hair", items["trait:i1"].Context);
        Assert.Equal(SheetItemKind.Synopsis, items["synopsis"].Kind);
    }

    [Fact]
    public async Task Tag_traduzida_uma_vez_nao_volta_para_a_IA_no_segundo_jogo()
    {
        var catalog = new FakeVndbCatalog([Game(), Game(Game2, "Sequel") with { Tags = [Comedy, new("g99", "Isekai", "cont", 2.0, 0, false)] }], [Kenta(), Aoi(), Ghost()]);
        await Fetch(catalog);
        await Fetch(catalog, Game2);
        var first = new StubSheetTranslator();
        await new SheetTranslationRunner(first, Store).RunAsync(Game1, cancellationToken: Ct);
        var second = new StubSheetTranslator();

        var result = await new SheetTranslationRunner(second, Store).RunAsync(Game2, cancellationToken: Ct);

        Assert.Equal(["tag:g99"], second.Keys.Where(k => k.StartsWith("tag:", StringComparison.Ordinal)));
        Assert.DoesNotContain(second.Keys, k => k.StartsWith("trait:", StringComparison.Ordinal));
        Assert.DoesNotContain(second.Keys, k => k.StartsWith("character:", StringComparison.Ordinal));
        Assert.Equal("PT: Isekai", Store.LoadTerms().Find(SheetTermKind.Tag, "g99")!.Text.Target);
        Assert.Equal(1 + 1, result.Translated);
    }

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
    public async Task Item_com_os_marcadores_de_spoiler_na_ordem_trocada_fica_pendente()
    {
        await Fetch();
        var translator = new StubSheetTranslator(i => i.Key == "character:c1" ? "The hero. [/spoiler]He is a ghost.[spoiler]" : "PT: " + i.Source);

        var result = await new SheetTranslationRunner(translator, Store).RunAsync(Game1, cancellationToken: Ct);

        Assert.Equal(1, result.Failed);
        Assert.Equal(TranslationStatus.Untranslated, Store.LoadCharacter("c1")!.Description!.Status);
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
    public async Task Force_nao_sobrescreve_o_texto_da_pessoa_que_ficou_desatualizado()
    {
        await Fetch();
        await new SheetTranslationRunner(new StubSheetTranslator(), Store).RunAsync(Game1, cancellationToken: Ct);
        SheetEditor.Edit(Store, Game1, SheetField.Synopsis, "Minha sinopse. [spoiler]Ele morre.[/spoiler] Kenta volta.");
        await Fetch(Catalog(Game(description: "A new synopsis. [spoiler]He dies.[/spoiler]")));
        var outdated = Store.LoadGame(Game1)!.Synopsis!;
        Assert.True(outdated.Outdated);
        var translator = new StubSheetTranslator(i => "NOVO: " + i.Source);
        var runner = new SheetTranslationRunner(translator, Store);

        Assert.DoesNotContain(runner.Plan(Game1, force: true).Items, i => i.Key == SheetField.Synopsis.Key);
        await runner.RunAsync(Game1, new SheetTranslationOptions { Force = true }, cancellationToken: Ct);

        Assert.DoesNotContain(SheetField.Synopsis.Key, translator.Keys);
        Assert.Equal(outdated, Store.LoadGame(Game1)!.Synopsis);
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
    public async Task Cancelar_deixa_gravado_o_que_ja_voltou()
    {
        await Fetch();
        using var cts = new CancellationTokenSource();
        var translator = new CancellingTranslator(cts);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new SheetTranslationRunner(translator, Store).RunAsync(Game1, new SheetTranslationOptions { NamesPerCall = 3 }, cancellationToken: cts.Token));

        Assert.Equal(3, Store.LoadTerms().All.Count(t => t.Text.Status == TranslationStatus.Machine));
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

    private sealed class CancellingTranslator(CancellationTokenSource cts) : ISheetTranslator
    {
        private int _calls;

        public Task<IReadOnlyList<SheetTranslation>> TranslateAsync(SheetTranslationRequest request, CancellationToken ct)
        {
            if (++_calls == 2)
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
            }

            return Task.FromResult<IReadOnlyList<SheetTranslation>>([.. request.Items.Select(i => new SheetTranslation(i.Key, "PT: " + i.Source))]);
        }
    }
}
